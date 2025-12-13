using System.Text;
using CodeAssessment.Shared;

namespace CodeAssessment.Api;

public class ReportWriter : IReportWriter
{
    private readonly string _reportsRoot;

    public ReportWriter()
    {
        // bv. mapje "reports" naast je bin\Debug\netX
        _reportsRoot = Path.Combine(AppContext.BaseDirectory, "reports");
    }

    public async Task<string> WriteReportAsync(CodeRequest req, FullAnalysisResponse response)
    {
        Directory.CreateDirectory(_reportsRoot);

        var candidateId   = SafeFilePart(req.CandidateId ?? "unknown");
        var assignmentId  = SafeFilePart(req.AssignmentId ?? "assignment");
        var fileName      = $"{DateTime.UtcNow:yyyyMMdd_HHmmss}_{assignmentId}_{candidateId}.txt";
        var path          = Path.Combine(_reportsRoot, fileName);

        var sb = new StringBuilder();

        // ===== Header =====
        sb.AppendLine("===== CODE ASSESSMENT RAPPORT =====");
        sb.AppendLine($"Datum/tijd (UTC): {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine();

        // ===== Kandidaat =====
        sb.AppendLine("== Kandidaat ==");
        sb.AppendLine($"Id      : {req.CandidateId ?? "-"}");
        sb.AppendLine($"Naam    : {req.CandidateName ?? "-"}");
        sb.AppendLine($"Email   : {req.CandidateEmail ?? "-"}");
        sb.AppendLine();

        // ===== Opdracht =====
        sb.AppendLine("== Opdracht ==");
        sb.AppendLine($"AssignmentId  : {req.AssignmentId ?? "-"}");
        sb.AppendLine($"AssignmentName: {req.AssignmentName ?? "-"}");
        sb.AppendLine();

        // ===== Overall summary =====
        var sum = response.Summary;
        sb.AppendLine("== Samenvatting ==");
        sb.AppendLine($"Einds score     : {sum.FinalScore?.ToString() ?? "-"}");
        sb.AppendLine($"AI score        : {sum.AiScore?.ToString() ?? "-"}");
        sb.AppendLine($"Compileert      : {(sum.Compiles ? "JA" : "NEE")}");
        sb.AppendLine($"Alle tests groen: {(sum.AllTestsPassed ? "JA" : "NEE")}");
        sb.AppendLine();

        // ===== AI Review =====
        if (response.AiReview is { } ai)
        {
            sb.AppendLine("== AI review ==");
            sb.AppendLine($"Score: {ai.FinalScore?.ToString() ?? "-"} / 10");
            sb.AppendLine();
            sb.AppendLine("Algemene feedback:");
            sb.AppendLine(ai.GeneralFeedback ?? "(geen)");
            sb.AppendLine();

            if (ai.Issues?.Count > 0)
            {
                sb.AppendLine("Issues:");
                foreach (var issue in ai.Issues)
                {
                    sb.AppendLine(
                        $"- Regels {issue.LineStart}" +
                        (issue.LineEnd.HasValue && issue.LineEnd != issue.LineStart
                            ? $"–{issue.LineEnd}"
                            : "") +
                        $", Severity: {issue.Severity}"
                    );
                    sb.AppendLine($"  Suggestie: {issue.Suggestion}");
                }
            }
            else
            {
                sb.AppendLine("Geen AI-issues gevonden.");
            }

            sb.AppendLine();
        }

        // ===== Static analysis =====
        if (response.StaticAnalysis is { } sa)
        {
            sb.AppendLine("== Static analysis ==");
            sb.AppendLine($"Analyzer: {sa.AnalyzerName}");
            sb.AppendLine($"Aantal diagnostics: {sa.Diagnostics.Count}");
            sb.AppendLine();

            foreach (var d in sa.Diagnostics)
            {
                sb.AppendLine($"- {d.Id} [{d.Severity}] @ {d.Line}:{d.Column}");
                sb.AppendLine($"  {d.Message}");
            }

            sb.AppendLine();
        }

        // ===== Runtime =====
        if (response.Runtime is RuntimeAnalysisResult runtime)
        {
            sb.AppendLine("== Runtime ==");
            sb.AppendLine($"ExitCode         : {runtime.ExitCode}");
            sb.AppendLine($"RunDurationMs    : {runtime.RunDurationMs?.ToString() ?? "-"}");
            sb.AppendLine($"RunTotalCpuTimeMs: {runtime.RunTotalCpuTimeMs?.ToString() ?? "-"}");
            sb.AppendLine($"RunAverageCpuPct : {(runtime.RunAverageCpuPct.HasValue ? runtime.RunAverageCpuPct.Value.ToString("0.0") : "-")}");
            sb.AppendLine($"RunPeakWorkingSet: {(runtime.RunPeakWorkingSet.HasValue ? Math.Round(runtime.RunPeakWorkingSet.Value / (1024.0 * 1024.0), 1) + " MB" : "-")}");
            sb.AppendLine();

            if (runtime.Phases is { Count: > 0 })
            {
                sb.AppendLine("Fases:");
                foreach (var phase in runtime.Phases)
                {
                    sb.AppendLine($"- {phase.Name}");
                    sb.AppendLine($"  DurationMs    : {phase.DurationMs}");
                    sb.AppendLine($"  CpuTimeMs     : {phase.CpuTimeMs?.ToString() ?? "-"}");
                    sb.AppendLine($"  PeakWorkingSet: {(phase.PeakWorkingSet.HasValue ? Math.Round(phase.PeakWorkingSet.Value / (1024.0 * 1024.0), 1) + " MB" : "-")}");

                    if (phase.Samples is { Count: > 0 })
                    {
                        var first = phase.Samples[0];
                        var last  = phase.Samples[^1];

                        sb.AppendLine($"  Samples count : {phase.Samples.Count}");
                        sb.AppendLine($"  Samples range : {first.T} ms → {last.T} ms");
                    }

                    sb.AppendLine();
                }
            }
            else
            {
                sb.AppendLine("Geen fases geregistreerd.");
                sb.AppendLine();
            }

            // StdOut (snippet) laten we ook staan
            if (!string.IsNullOrWhiteSpace(runtime.StdOut))
            {
                sb.AppendLine("StdOut (snippet):");
                sb.AppendLine(runtime.StdOut.Length > 400
                    ? runtime.StdOut.Substring(0, 400) + "..."
                    : runtime.StdOut);
                sb.AppendLine();
            }

            if (!string.IsNullOrWhiteSpace(runtime.StdErr))
            {
                sb.AppendLine("StdErr:");
                sb.AppendLine(runtime.StdErr);
                sb.AppendLine();
            }
        }

        // ===== Tests =====
        if (response.Tests is { } tests)
        {
            sb.AppendLine("== Tests ==");
            sb.AppendLine($"Binary score: {tests.BinaryScore ?? "-"}");
            sb.AppendLine($"Total       : {tests.Total}");
            sb.AppendLine($"Passed      : {tests.Passed}");
            sb.AppendLine($"Failed      : {tests.Failed}");
            sb.AppendLine();

            if (tests.Tests.Count > 0)
            {
                sb.AppendLine("Details per test:");
                foreach (var t in tests.Tests)
                {
                    sb.AppendLine($"- {t.Name} → {t.Outcome}");
                    if (!string.IsNullOrWhiteSpace(t.Message))
                        sb.AppendLine($"  Message: {t.Message}");
                }
                sb.AppendLine();
            }
            else
            {
                sb.AppendLine("Geen individuele tests geregistreerd.");
                sb.AppendLine();
            }

            // 🔹 HIER: raw stdout/stderr van `dotnet test` loggen
            if (!string.IsNullOrWhiteSpace(tests.RawStdOut))
            {
                sb.AppendLine("== dotnet test stdout ==");
                sb.AppendLine(tests.RawStdOut);
                sb.AppendLine();
            }

            if (!string.IsNullOrWhiteSpace(tests.RawStdErr))
            {
                sb.AppendLine("== dotnet test stderr ==");
                sb.AppendLine(tests.RawStdErr);
                sb.AppendLine();
            }
        }

        // ===== Code =====
        if (!string.IsNullOrWhiteSpace(req.Code))
        {
            sb.AppendLine("== Ingezonden code ==");
            sb.AppendLine(req.Code);
        }

        await File.WriteAllTextAsync(path, sb.ToString(), Encoding.UTF8);
        return path;
    }

    private static string SafeFilePart(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(value.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "value" : cleaned;
    }
}