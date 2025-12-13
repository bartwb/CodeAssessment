using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using CodeAssessment.Shared;

namespace CodeAssessment.Static;

public static class RoslynHelpers
{
    public static string EnsureNamespace(string code)
    {
        try
        {
            var tree = CSharpSyntaxTree.ParseText(code, new CSharpParseOptions(LanguageVersion.Preview));
            var root = tree.GetRoot();
            bool hasNs = root.DescendantNodes().Any(n =>
                n.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.NamespaceDeclaration) ||
                n.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.FileScopedNamespaceDeclaration));

            if (hasNs) return code;

            return $"namespace Submission {{\n{code}\n}}";
        }
        catch
        {
            return code;
        }
    }

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
            path: "/workspace/Submission/Program.cs"
        );

        // alle runtime assemblies als referentie
        var tpa = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? "";
        var refs = tpa.Split(Path.PathSeparator)
                      .Distinct()
                      .Select(p => MetadataReference.CreateFromFile(p))
                      .Cast<MetadataReference>()
                      .ToList();

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

    private static LanguageVersion ParseLanguage(string? v)
        => string.IsNullOrWhiteSpace(v)
           ? LanguageVersion.Preview
           : (Enum.TryParse(v, true, out LanguageVersion lv) ? lv : LanguageVersion.Preview);
}
