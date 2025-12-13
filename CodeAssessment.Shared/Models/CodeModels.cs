namespace CodeAssessment.Shared;

public sealed record CodeRequest(string Action, string Code, string? LanguageVersion = null)
{
    public string? CandidateId { get; init; }
    public string? CandidateName { get; init; }
    public string? CandidateEmail { get; init; }

    public string? AssignmentId { get; init; }
    public string? AssignmentName { get; init; }
}

public record CompileResponse(
    bool Success,
    string StdOut,
    string StdErr,
    int ExitCode
);

public record RunResponse(
    bool Success,
    string StdOut,
    string StdErr,
    int ExitCode
);

public class FullAnalysisResponse
{
    public OverallSummary Summary { get; set; } = new();
    public AiReviewResult? AiReview { get; set; }
    public StaticAnalysisResult? StaticAnalysis { get; set; }
    public object? Runtime { get; set; }
    public TestsAnalysisResult? Tests { get; set; }   
}


public class OverallSummary
{
    public int? FinalScore { get; set; }
    public int? AiScore { get; set; }
    public bool Compiles { get; set; }
    public bool AllTestsPassed { get; set; }
}

public class AiIssue
{
    public int? LineStart { get; set; }
    public int? LineEnd { get; set; }
    public string Severity { get; set; } = "";
    public string Suggestion { get; set; } = "";
}

public class AiReviewResult
{
    public int? FinalScore { get; set; }
    public string GeneralFeedback { get; set; } = "";
    public List<AiIssue> Issues { get; set; } = new();
    
    // optioneel: ruwe JSON/tekst voor debugging
    public string? RawJson { get; set; }
}

public class StaticDiagnostic
{
    public string Id { get; set; } = "";
    public string Severity { get; set; } = "";
    public string Message { get; set; } = "";
    public string? FilePath { get; set; }
    public int? Line { get; set; }
    public int? Column { get; set; }
}

public class StaticAnalysisResult
{
    public string AnalyzerName { get; set; } = "";
    public List<StaticDiagnostic> Diagnostics { get; set; } = new();
}

public class RuntimeMetricSample
{
    public long T { get; set; }          // ms sinds start
    public double CpuPct { get; set; }   // CPU% (over alle cores)
    public long WorkingSet { get; set; } // bytes
}

public class RuntimePhaseSummary
{
    public string Name { get; set; } = "";
    public long DurationMs { get; set; }
    public long? CpuTimeMs { get; set; }
    public long? PeakWorkingSet { get; set; }
    public string? LogStdOut { get; set; }
    public string? LogStdErr { get; set; }
    public List<RuntimeMetricSample>? Samples { get; set; }
}

public class RuntimeAnalysisResult
{
    public string? StdOut { get; set; }
    public string? StdErr { get; set; }
    public int ExitCode { get; set; }

    public List<RuntimePhaseSummary> Phases { get; set; } = new();

    public long? RunDurationMs { get; set; }
    public long? RunTotalCpuTimeMs { get; set; }
    public double? RunAverageCpuPct { get; set; }
    public long? RunPeakWorkingSet { get; set; }
}

public class TestCaseResult
{
    public string Name { get; set; } = "";
    public string Outcome { get; set; } = "";   // "Passed", "Failed", ...
    public string? Message { get; set; }
}

public class TestsAnalysisResult
{
    public int Total { get; set; }
    public int Passed { get; set; }
    public int Failed { get; set; }

    public string? BinaryScore { get; set; }

    public List<TestCaseResult> Tests { get; set; } = new();

    // DEBUG: raw output van dotnet test
    public string? RawStdOut { get; set; }
    public string? RawStdErr { get; set; }
    public int? ExitCode { get; set; }
}