using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace CodeAssessment.Static;

public static class DocRoot
{
    public const string VIRTUAL_ROOT = "/workspace";
    public const string VIRTUAL_SOURCE_PATH = "/workspace/Submission/Program.cs";
    public const string VIRTUAL_EDITORCONFIG_PATH = "/workspace/.editorconfig";
}

public static class AnalyzerSeverity
{
    public static bool IsAtLeast(string min, DiagnosticSeverity sev)
    {
        int Rank(DiagnosticSeverity s) => s switch
        {
            DiagnosticSeverity.Hidden  => 0,
            DiagnosticSeverity.Info    => 1,
            DiagnosticSeverity.Warning => 2,
            DiagnosticSeverity.Error   => 3,
            _ => 0
        };

        int minRank = min?.ToLowerInvariant() switch
        {
            "hidden"  => 0,
            "info"    => 1,
            "warning" => 2,
            "error"   => 3,
            _         => 2 // default Warning
        };

        return Rank(sev) >= minRank;
    }
}

public static class AnalyzerDefaults
{
    public const string EditorConfigContent = """
        root = true

        [*.cs]
        # Zet analyzers hoog en recent
        dotnet_analysis_level = latest
        dotnet_analyzer_diagnostic.severity = warning

        # Promoot alle relevante categorieën
        dotnet_analyzer_diagnostic.category-Performance.severity = warning
        dotnet_analyzer_diagnostic.category-Reliability.severity = warning
        dotnet_analyzer_diagnostic.category-Design.severity = warning
        dotnet_analyzer_diagnostic.category-Usage.severity = warning
        dotnet_analyzer_diagnostic.category-Security.severity = warning
        dotnet_analyzer_diagnostic.category-Naming.severity = warning
        dotnet_analyzer_diagnostic.category-Documentation.severity = warning

        # Ruis door ad-hoc compilatie
        dotnet_diagnostic.CA1016.severity = none   # Mark assemblies with assembly version

        # Specifieke regels die je zeker wilt zien
        dotnet_diagnostic.CA2000.severity = warning   # Dispose objects before losing scope
        dotnet_diagnostic.CA1305.severity = warning   # Specify IFormatProvider

        # Roslynator / StyleCop (zet gerust hoger als je wil)
        dotnet_diagnostic.RCS1102.severity = warning  # Make class static
        dotnet_diagnostic.RCS1075.severity = warning  # Avoid empty catch (System.Exception)
        dotnet_diagnostic.SA1300.severity = warning   # Element names should begin with upper-case

        # Tips lager zetten (optioneel)
        dotnet_diagnostic.CA1852.severity = suggestion  # Seal class
        """;
}

public static class InMemoryAdditionalTextFactory
{
    public static AdditionalText Create(string path, string content) => new InMemoryAdditionalText(path, content);

    private sealed class InMemoryAdditionalText : AdditionalText
    {
        private readonly string _path;
        private readonly SourceText _text;

        public InMemoryAdditionalText(string path, string content)
        {
            _path = path;
            _text = SourceText.From(content);
        }

        public override string Path => _path;

        public override SourceText GetText(CancellationToken cancellationToken = default) => _text;
    }
}

public sealed class SimpleAnalyzerConfigOptionsProvider : AnalyzerConfigOptionsProvider
{
    private readonly AnalyzerConfigOptions _global;

    public SimpleAnalyzerConfigOptionsProvider(IDictionary<string, string> values)
    {
        _global = new DictAnalyzerConfigOptions(values);
    }

    public override AnalyzerConfigOptions GlobalOptions => _global;

    public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => _global;

    public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => _global;

    private sealed class DictAnalyzerConfigOptions : AnalyzerConfigOptions
    {
        private readonly IDictionary<string, string> _values;

        public DictAnalyzerConfigOptions(IDictionary<string, string> values)
        {
            _values = values;
        }

        public override bool TryGetValue(string key, out string value) => _values.TryGetValue(key, out value);
    }
}

public static class EditorConfigParser
{
    public static IDictionary<string, string> Parse(string content)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var sr = new StringReader(content);
        string? line;

        while ((line = sr.ReadLine()) is not null)
        {
            line = line.Trim();
            if (line.Length == 0 || line.StartsWith("#") || line.StartsWith("["))
                continue;

            var idx = line.IndexOf('=');
            if (idx <= 0) continue;

            var key = line.Substring(0, idx).Trim();
            var val = line.Substring(idx + 1).Trim();

            dict[key] = val;
        }

        return dict;
    }
}

public sealed class GlobalAnalyzerAssemblyLoader : IAnalyzerAssemblyLoader
{
    public static readonly GlobalAnalyzerAssemblyLoader Instance = new();

    private static readonly HashSet<string> _searchDirectories = new(StringComparer.OrdinalIgnoreCase);

    static GlobalAnalyzerAssemblyLoader()
    {
        AssemblyLoadContext.Default.Resolving += OnAssemblyResolve;
    }

    private GlobalAnalyzerAssemblyLoader() { }

    private static Assembly? OnAssemblyResolve(AssemblyLoadContext context, AssemblyName name)
    {
        Console.WriteLine($"[DEBUG_LOAD] De 'Default' context zoekt naar: '{name.FullName}'");

        foreach (var dir in _searchDirectories)
        {
            var candidate = Path.Combine(dir, name.Name + ".dll");
            if (File.Exists(candidate))
            {
                Console.WriteLine($"[DEBUG_LOAD]   > GEVONDEN in onze map: {candidate}");
                return context.LoadFromAssemblyPath(candidate);
            }
        }

        Console.WriteLine($"[DEBUG_LOAD]   > NIET GEVONDEN in onze mappen. .NET zoekt verder.");
        return null;
    }

    public Assembly LoadFromPath(string fullPath)
    {
        var dir = Path.GetDirectoryName(fullPath);
        if (dir != null)
        {
            _searchDirectories.Add(dir);
            Console.WriteLine($"[DEBUG_CONTEXT] Map toegevoegd aan zoeklijst: {dir}");
        }

        return AssemblyLoadContext.Default.LoadFromAssemblyPath(fullPath);
    }

    public void AddDependencyLocation(string fullPath)
    {
        var dir = Path.GetDirectoryName(fullPath);
        if (dir != null)
        {
            _searchDirectories.Add(dir);
            Console.WriteLine($"[DEBUG_CONTEXT] Map toegevoegd aan zoeklijst: {dir}");
        }
    }
}

public static class RoslynUtil
{
    // 1) Namespace-wrap
    public static string EnsureNamespace(string code)
    {
        try
        {
            var tree = CSharpSyntaxTree.ParseText(code, new CSharpParseOptions(LanguageVersion.Preview));
            var root = tree.GetRoot();
            bool hasNs = root.DescendantNodes().Any(n =>
                n is NamespaceDeclarationSyntax || n is FileScopedNamespaceDeclarationSyntax);

            if (hasNs) return code;

            return $"namespace Submission {{\n{code}\n}}";
        }
        catch
        {
            return code;
        }
    }

    // 2) Compilation maken
    public static CSharpCompilation CreateCompilation(string code, string? languageVersion)
    {
        var parseOptions = new CSharpParseOptions(
            languageVersion: ParseLanguage(languageVersion),
            documentationMode: DocumentationMode.Parse,
            kind: SourceCodeKind.Regular
        );

        var tree = CSharpSyntaxTree.ParseText(
            code,
            parseOptions,
            path: DocRoot.VIRTUAL_SOURCE_PATH
        );

        var tpa = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? "";
        var refs = tpa.Split(Path.PathSeparator)
                      .Distinct()
                      .Select(p => MetadataReference.CreateFromFile(p))
                      .Cast<MetadataReference>()
                      .ToList();

        var coreLib = typeof(object).Assembly.Location;
        if (!refs.Any(r => string.Equals(Path.GetFileName(r.Display), "System.Private.CoreLib.dll", StringComparison.OrdinalIgnoreCase)))
        {
            refs.Add(MetadataReference.CreateFromFile(coreLib));
        }

        return CSharpCompilation.Create(
            assemblyName: "Adhoc",
            syntaxTrees: new[] { tree },
            references: refs,
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release,
                warningLevel: 4
            )
        );
    }

    // 3) Load analyzers from folder
    public static ImmutableArray<DiagnosticAnalyzer> LoadAnalyzersFrom(string directory)
    {
        if (!Directory.Exists(directory))
            return ImmutableArray<DiagnosticAnalyzer>.Empty;

        var results = new List<DiagnosticAnalyzer>();
        var loader = GlobalAnalyzerAssemblyLoader.Instance;

        var dlls = Directory.EnumerateFiles(directory, "*.dll", SearchOption.AllDirectories)
                            .Where(p => !p.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                                          .Any(seg => string.Equals(seg, "vb", StringComparison.OrdinalIgnoreCase)));

        foreach (var dll in dlls)
        {
            try
            {
                var afr = new AnalyzerFileReference(dll, loader);
                afr.AnalyzerLoadFailed += (_, e) =>
                {
                    Console.WriteLine($"[ANALYZER LOAD FAILED] {e.Message} (Type: {e.Exception?.GetType().Name})");
                };

                var analyzers = afr.GetAnalyzers(LanguageNames.CSharp);
                results.AddRange(analyzers);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ANALYZER LOAD ERROR] Failed to process {dll}: {ex.Message}");
            }
        }

        return results.Distinct().ToImmutableArray();
    }

    private static LanguageVersion ParseLanguage(string? v)
        => string.IsNullOrWhiteSpace(v)
           ? LanguageVersion.Preview
           : (Enum.TryParse(v, true, out LanguageVersion lv) ? lv : LanguageVersion.Preview);
}
