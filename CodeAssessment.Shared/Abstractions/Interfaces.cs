namespace CodeAssessment.Shared;

public interface IRuntimeService
{
    Task<CompileResponse> CompileOnlyAsync(CodeRequest req);
}

public interface IRuntimeExecutionService
{
    Task<RunResponse> RunAsync(CodeRequest req);
}

public interface IAiReviewService
{
    Task<AiReviewResult> ReviewAsync(CodeRequest req);
}

public interface IStaticAnalysisService
{
    Task<StaticAnalysisResult> AnalyzeAsync(CodeRequest req, string? minSeverity = "Warning");
}

public interface IRuntimeAnalysisService
{
    Task<RuntimeAnalysisResult> AnalyzeAsync(CodeRequest req, int samplingIntervalMs = 500);
}

public interface IAssessmentOrchestrator
{
    Task<FullAnalysisResponse> AnalyzeAsync(CodeRequest req);
}

public interface ITestRunnerService
{
    Task<TestsAnalysisResult> RunTestsAsync(CodeRequest req);
}

public interface IReportWriter
{
    Task<string> WriteReportAsync(CodeRequest req, FullAnalysisResponse response);
}