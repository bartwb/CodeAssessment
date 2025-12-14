using Microsoft.AspNetCore.Mvc;
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
    [FromBody] CodeRequest req,
    IRuntimeService runtime,
    IRuntimeExecutionService exec,
    IAssessmentOrchestrator orchestrator) =>
{
    if (string.IsNullOrWhiteSpace(req.Action))
        return Results.BadRequest(new { error = "action ontbreekt" });

    if (string.IsNullOrWhiteSpace(req.Code))
        return Results.BadRequest(new { error = "code ontbreekt" });

    var codeReq = new CodeRequest(req.Code, req.LanguageVersion)
    {
        CandidateId = req.CandidateId,
        CandidateName = req.CandidateName,
        CandidateEmail = req.CandidateEmail,
        AssignmentId = req.AssignmentId,
        AssignmentName = req.AssignmentName
    };

    var action = req.Action.Trim().ToLowerInvariant();

    return action switch
    {
        "compile" => Results.Ok(await runtime.CompileOnlyAsync(codeReq)),
        "run"     => Results.Ok(await exec.RunAsync(codeReq)),
        "analyse" => Results.Ok(await orchestrator.AnalyzeAsync(codeReq)),
        _         => Results.BadRequest(new { error = $"onbekende action '{req.Action}' (verwacht: compile|run|analyse)" })
    };
});

var port = 6000;
app.Urls.Clear();
app.Urls.Add($"http://0.0.0.0:{port}");
Console.WriteLine("Listening on: " + string.Join(", ", app.Urls));
Console.WriteLine("ASPNETCORE_URLS=" + Environment.GetEnvironmentVariable("ASPNETCORE_URLS"));
Console.WriteLine("PORT=" + Environment.GetEnvironmentVariable("PORT"));
app.Run();
