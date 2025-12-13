using CodeAssessment.Shared;

namespace CodeAssessment.Api;

public class AssessmentOrchestrator : IAssessmentOrchestrator
{
    private readonly IAiReviewService _ai;
    private readonly IRuntimeService _runtimeCompile;
    private readonly IStaticAnalysisService _staticAnalysis;
    private readonly IRuntimeAnalysisService _runtimeAnalysis;
    private readonly ITestRunnerService _testRunner;
    private readonly IReportWriter _reportWriter;

    public AssessmentOrchestrator(
        IAiReviewService ai,
        IRuntimeService runtimeCompile,
        IStaticAnalysisService staticAnalysis,
        IRuntimeAnalysisService runtimeAnalysis,
        ITestRunnerService testRunner,
        IReportWriter reportWriter)
    {
        _ai = ai;
        _runtimeCompile = runtimeCompile;
        _staticAnalysis = staticAnalysis;
        _runtimeAnalysis = runtimeAnalysis;
        _testRunner = testRunner;
        _reportWriter = reportWriter;
    }

    public async Task<FullAnalysisResponse> AnalyzeAsync(CodeRequest req)
    {
        var aiResult        = await _ai.ReviewAsync(req);
        var compileResult   = await _runtimeCompile.CompileOnlyAsync(req);
        var staticResult    = await _staticAnalysis.AnalyzeAsync(req, "Warning");
        var runtimeAnalysis = await _runtimeAnalysis.AnalyzeAsync(req, 500);
        var testsResult     = await _testRunner.RunTestsAsync(req);

        var summary = new OverallSummary
        {
            FinalScore     = aiResult.FinalScore,       // later combineer je alles
            AiScore        = aiResult.FinalScore,
            Compiles       = compileResult.Success,
            AllTestsPassed = testsResult.Total > 0 && testsResult.Failed == 0
        };

        var response = new FullAnalysisResponse
        {
            Summary        = summary,
            AiReview       = aiResult,
            StaticAnalysis = staticResult,
            Runtime        = runtimeAnalysis,
            Tests          = testsResult
        };

         try
        {
            var reportPath = await _reportWriter.WriteReportAsync(req, response);
            Console.WriteLine($"[REPORT] Rapport weggeschreven naar: {reportPath}");
        }
        catch (Exception ex)
        {
            // Fout bij rapportage mag nooit de analyse zelf breken
            Console.WriteLine($"[REPORT] Fout bij wegschrijven rapport: {ex}");
        }

        return response;
    }
}
