using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using CodeAssessment.Shared;

namespace CodeAssessment.Static;

public class StaticAnalysisService : IStaticAnalysisService
{
    public async Task<StaticAnalysisResult> AnalyzeAsync(CodeRequest req, string? minSeverity = "Warning")
    {
        if (string.IsNullOrWhiteSpace(req.Code))
            throw new ArgumentException("Code is leeg.", nameof(req));

        var source = RoslynUtil.EnsureNamespace(req.Code);
        var compilation = RoslynUtil.CreateCompilation(source, req.LanguageVersion);

        // 1) Compiler diagnostics
        var compilationDiags = compilation
            .GetDiagnostics()
            .Where(d => AnalyzerSeverity.IsAtLeast(minSeverity ?? "Warning", d.Severity))
            .Select(MapDiagnostic)
            .ToList();

        // 2) Analyzer diagnostics (.NET analyzers / Roslynator / StyleCop / etc.)
        var analyzerDir = Path.Combine(AppContext.BaseDirectory, "analyzers", "dotnet");
        var analyzers = RoslynUtil.LoadAnalyzersFrom(analyzerDir);

        var analyzerDiagsMapped = new List<StaticDiagnostic>();

        if (analyzers.Length > 0)
        {
            // in-memory .editorconfig
            var additionalFiles = ImmutableArray.Create<AdditionalText>(
                InMemoryAdditionalTextFactory.Create(
                    DocRoot.VIRTUAL_EDITORCONFIG_PATH,
                    AnalyzerDefaults.EditorConfigContent));

            var ecDict = EditorConfigParser.Parse(AnalyzerDefaults.EditorConfigContent);
            var optionsProvider = new SimpleAnalyzerConfigOptionsProvider(ecDict);

            var cwa = compilation.WithAnalyzers(
                analyzers,
                new CompilationWithAnalyzersOptions(
                    new AnalyzerOptions(additionalFiles, optionsProvider),
                    onAnalyzerException: (ex, analyzer, diag) =>
                    {
                        Console.WriteLine($"[ANALYZER RUN ERROR] Analyzer: {analyzer.GetType().Name}");
                        Console.WriteLine($"[ANALYZER RUN ERROR] Exception: {ex.Message}");
                        Console.WriteLine("---");
                    },
                    concurrentAnalysis: true,
                    logAnalyzerExecutionTime: false
                )
            );

            var analyzerDiags = await cwa.GetAnalyzerDiagnosticsAsync();

            analyzerDiagsMapped = analyzerDiags
                .Where(d => AnalyzerSeverity.IsAtLeast(minSeverity ?? "Warning", d.Severity))
                .OrderBy(d => d.Severity)
                .ThenBy(d => d.Id)
                .Select(MapDiagnostic)
                .ToList();
        }
        else
        {
            Console.WriteLine($"[STATIC] Geen analyzer DLLs gevonden in {analyzerDir}");
        }

        var combined = new List<StaticDiagnostic>();
        combined.AddRange(compilationDiags);
        combined.AddRange(analyzerDiagsMapped);

        return new StaticAnalysisResult
        {
            AnalyzerName = ".NET Code Analysis (Roslyn + analyzers)",
            Diagnostics = combined
        };
    }

    private static StaticDiagnostic MapDiagnostic(Diagnostic d)
    {
        string? path = null;
        int? line = null;
        int? col = null;

        if (d.Location.IsInSource)
        {
            path = d.Location.SourceTree?.FilePath ?? "(in-memory)";
            var span = d.Location.GetLineSpan();
            line = span.StartLinePosition.Line + 1;
            col  = span.StartLinePosition.Character + 1;
        }

        return new StaticDiagnostic
        {
            Id       = d.Id,
            Severity = d.Severity.ToString(),
            Message  = d.GetMessage(),
            FilePath = path,
            Line     = line,
            Column   = col
        };
    }
}
