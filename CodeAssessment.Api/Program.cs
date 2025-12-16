using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using CodeAssessment.Shared;
using CodeAssessment.Api;
using CodeAssessment.Runtime;
using CodeAssessment.Ai;
using CodeAssessment.Static;
using CodeAssessment.Tests;

Console.WriteLine("BOOT: entering Program.cs");

var builder = WebApplication.CreateBuilder(args);

// CORS zodat je straks vanuit je frontend kunt praten
builder.Services.AddCors(o =>
    o.AddDefaultPolicy(p => p
        .AllowAnyOrigin()
        .AllowAnyHeader()
        .AllowAnyMethod()
    )
);

// Services
builder.Services.AddSingleton<IRuntimeService, RuntimeService>();
builder.Services.AddSingleton<IRuntimeExecutionService, RuntimeExecutionService>();
builder.Services.AddSingleton<IRuntimeAnalysisService, RuntimeAnalysisService>();
builder.Services.AddSingleton<IAiReviewService, AiReviewService>();
builder.Services.AddSingleton<IStaticAnalysisService, StaticAnalysisService>();
builder.Services.AddSingleton<IAssessmentOrchestrator, AssessmentOrchestrator>();
builder.Services.AddSingleton<IReportWriter, ReportWriter>();
builder.Services.AddSingleton<ITestRunnerService, TestRunnerService>();

var app = builder.Build();

app.UseCors();

// ========== 1) HEALTH ==========
app.MapGet("/healthstatus", () => Results.Ok(new { status = "ok" }));

// ========== 2) ACA Runner ==========
app.MapPost("/runner", async (
    HttpContext http,
    [FromBody] CodeRequest req,
    IRuntimeService runtime,
    IRuntimeExecutionService exec,
    IAssessmentOrchestrator orchestrator) =>
{
    var sw = Stopwatch.StartNew();

    // Dynamic Sessions identifier (belangrijk voor correlatie)
    var identifier = http.Request.Query["identifier"].ToString();

    // Correlation id (meest nuttig in logs)
    var corr = Guid.NewGuid().ToString("N")[..12];

    // Kleine helpers voor “niet te veel” log noise
    static int Len(string? s) => string.IsNullOrEmpty(s) ? 0 : s.Length;
    static string Clip(string? s, int max = 200) =>
        string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s[..max] + "...");

    // Inbound log
    Console.WriteLine(
        $"RUNNER IN  corr={corr} id={identifier} method={http.Request.Method} path={http.Request.Path} " +
        $"action='{req?.Action}' codeLen={Len(req?.Code)} lang='{req?.LanguageVersion}' " +
        $"candId='{req?.CandidateId}' assignId='{req?.AssignmentId}' ua='{Clip(http.Request.Headers.UserAgent)}'"
    );

    try
    {
        if (req is null)
        {
            Console.WriteLine($"RUNNER BAD corr={corr} id={identifier} reason=req_null elapsedMs={sw.ElapsedMilliseconds}");
            return Results.BadRequest(new { error = "body ontbreekt", corr, identifier });
        }

        if (string.IsNullOrWhiteSpace(req.Action))
        {
            Console.WriteLine($"RUNNER BAD corr={corr} id={identifier} reason=action_missing elapsedMs={sw.ElapsedMilliseconds}");
            return Results.BadRequest(new { error = "action ontbreekt", corr, identifier });
        }

        if (string.IsNullOrWhiteSpace(req.Code))
        {
            Console.WriteLine($"RUNNER BAD corr={corr} id={identifier} reason=code_missing elapsedMs={sw.ElapsedMilliseconds}");
            return Results.BadRequest(new { error = "code ontbreekt", corr, identifier });
        }

        var action = req.Action.Trim().ToLowerInvariant();

        // Build internal request (log both IN and OUT lengths to catch mapping issues)
        var codeReq = new CodeRequest(action, req.Code, req.LanguageVersion)
        {
            CandidateId = req.CandidateId,
            CandidateName = req.CandidateName,
            CandidateEmail = req.CandidateEmail,
            AssignmentId = req.AssignmentId,
            AssignmentName = req.AssignmentName
        };

        Console.WriteLine(
            $"RUNNER MAP corr={corr} id={identifier} action='{action}' " +
            $"codeLenIn={Len(req.Code)} codeLenOut={Len(codeReq.Code)}"
        );

        object result;

        // Per action timing + step logging
        switch (action)
        {
            case "compile":
                Console.WriteLine($"RUNNER STEP corr={corr} id={identifier} step=compile_start");
                result = await runtime.CompileOnlyAsync(codeReq);
                Console.WriteLine($"RUNNER STEP corr={corr} id={identifier} step=compile_done elapsedMs={sw.ElapsedMilliseconds}");
                break;

            case "run":
                Console.WriteLine($"RUNNER STEP corr={corr} id={identifier} step=run_start");
                result = await exec.RunAsync(codeReq);
                Console.WriteLine($"RUNNER STEP corr={corr} id={identifier} step=run_done elapsedMs={sw.ElapsedMilliseconds}");
                break;

            case "analyse":
                Console.WriteLine($"RUNNER STEP corr={corr} id={identifier} step=analyse_start");
                result = await orchestrator.AnalyzeAsync(codeReq);
                Console.WriteLine($"RUNNER STEP corr={corr} id={identifier} step=analyse_done elapsedMs={sw.ElapsedMilliseconds}");
                break;

            default:
                Console.WriteLine($"RUNNER BAD corr={corr} id={identifier} reason=unknown_action action='{action}' elapsedMs={sw.ElapsedMilliseconds}");
                return Results.BadRequest(new { error = $"onbekende action '{req.Action}' (verwacht: compile|run|analyse)", corr, identifier });
        }

        sw.Stop();
        Console.WriteLine($"RUNNER OK  corr={corr} id={identifier} action='{action}' status=200 elapsedMs={sw.ElapsedMilliseconds}");

        // Return result + correlate (handig in callers)
        return Results.Ok(new { corr, identifier, action, result });
    }
    catch (Exception ex)
    {
        sw.Stop();

        // Log full exception (stacktrace)
        Console.WriteLine($"RUNNER ERR corr={corr} id={identifier} elapsedMs={sw.ElapsedMilliseconds}\n{ex}");

        // Return clean error JSON (zonder huge stacktrace naar client)
        return Results.Problem(
            title: "runner_failed",
            detail: ex.Message,
            statusCode: 500,
            extensions: new Dictionary<string, object?>
            {
                ["corr"] = corr,
                ["identifier"] = identifier,
                ["elapsedMs"] = sw.ElapsedMilliseconds
            }
        );
    }
});

var port = 6000;
app.Urls.Clear();
app.Urls.Add($"http://0.0.0.0:{port}");
Console.WriteLine("Listening on: " + string.Join(", ", app.Urls));
Console.WriteLine("ASPNETCORE_URLS=" + Environment.GetEnvironmentVariable("ASPNETCORE_URLS"));
Console.WriteLine("PORT=" + Environment.GetEnvironmentVariable("PORT"));
app.Run();
