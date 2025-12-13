using CodeAssessment.Shared;
using CodeAssessment.Tests.Internal;

namespace CodeAssessment.Tests;

public class TestRunnerService : ITestRunnerService
{
    public async Task<TestsAnalysisResult> RunTestsAsync(CodeRequest req)
    {
        var result = new TestsAnalysisResult();

        if (string.IsNullOrWhiteSpace(req.Code))
        {
            result.BinaryScore = "FOUT";
            return result;
        }

        var work = Path.Combine(Path.GetTempPath(), $"run-{Guid.NewGuid():N}");
        Directory.CreateDirectory(work);

        Console.WriteLine($"[TESTS] work dir = {work}");

        try
        {
            string stdout = "", stderr = "";
            int exit = 0;

            // 1) Kandidaten-classlib
            var libInit = await ProcessRunner.RunAsync("dotnet", "new classlib -o AssesmentSolution", work);
            if (libInit.ExitCode != 0)
            {
                result.BinaryScore = "FOUT";
                result.RawStdOut   = libInit.StdOut;
                result.RawStdErr   = libInit.StdErr;
                result.ExitCode    = libInit.ExitCode;
                return result;
            }

            await File.WriteAllTextAsync(
                Path.Combine(work, "AssesmentSolution", "Solution.cs"),
                req.Code
            );

            // 2) Tests-template kopiëren vanuit Api-output
            var baseDir = AppContext.BaseDirectory; // bin\Debug\net10.0 van Api
            var srcTpl  = Path.Combine(baseDir, "templates", "Tests");
            var dstTests = Path.Combine(work, "Tests");

            Console.WriteLine($"[TESTS] srcTpl = {srcTpl}");
            Console.WriteLine($"[TESTS] exists? {Directory.Exists(srcTpl)}");

            if (!Directory.Exists(srcTpl))
            {
                result.BinaryScore = "FOUT";
                result.RawStdErr   = $"Templates-directory niet gevonden: {srcTpl}";
                return result;
            }

            FsUtil.CopyDirectory(srcTpl, dstTests);

            // 2b) Zoeken naar het echte test-csproj (maakt niet uit hoe diep)
            var testsCsprojFull = Directory.GetFiles(dstTests, "*.csproj", SearchOption.AllDirectories)
                                           .FirstOrDefault();

            if (testsCsprojFull is null)
            {
                result.BinaryScore = "FOUT";
                result.RawStdErr   = $"Geen .csproj gevonden in test-template map: {dstTests}";
                return result;
            }

            var testsCsprojRel = Path.GetRelativePath(work, testsCsprojFull)
                                     .Replace('\\', '/'); // voor Windows

            Console.WriteLine($"[TESTS] testsCsprojFull = {testsCsprojFull}");
            Console.WriteLine($"[TESTS] testsCsprojRel  = {testsCsprojRel}");

            // 3) Project reference: Tests -> AssesmentSolution
            var addRef = await ProcessRunner.RunAsync(
                "dotnet",
                $"add \"{testsCsprojRel}\" reference AssesmentSolution/AssesmentSolution.csproj",
                work
            );

            if (addRef.ExitCode != 0)
            {
                result.BinaryScore = "FOUT";
                result.RawStdOut   = addRef.StdOut;
                result.RawStdErr   = addRef.StdErr;
                result.ExitCode    = addRef.ExitCode;
                return result;
            }

            // 4) Restore/build/test op het gevonden Tests-project
            var rr = await ProcessRunner.RunAsync(
                "dotnet",
                $"restore \"{testsCsprojRel}\"",
                work,
                120_000
            );

            var rb = await ProcessRunner.RunAsync(
                "dotnet",
                $"build \"{testsCsprojRel}\" --configuration Release",
                work,
                180_000
            );

            var rt = await ProcessRunner.RunAsync(
                "dotnet",
                $"test \"{testsCsprojRel}\" --configuration Release --logger \"trx;LogFileName=results.trx\"",
                work,
                240_000
            );

            stdout = string.Join("\n\n", rr.StdOut, rb.StdOut, rt.StdOut);
            stderr = string.Join("\n\n", rr.StdErr, rb.StdErr, rt.StdErr);
            exit   = rt.ExitCode;

            result.RawStdOut = stdout;
            result.RawStdErr = stderr;
            result.ExitCode  = exit;

            // 5) TRX zoeken + parse
            string? trx = null;

            var trDir = Path.Combine(work, "Tests", "TestResults");
            if (Directory.Exists(trDir))
            {
                trx = Directory.GetFiles(trDir, "*.trx", SearchOption.AllDirectories)
                               .OrderBy(File.GetLastWriteTimeUtc)
                               .LastOrDefault();
            }

            if (trx is null)
            {
                trx = Directory.GetFiles(work, "*.trx", SearchOption.AllDirectories)
                               .OrderBy(File.GetLastWriteTimeUtc)
                               .LastOrDefault();
            }

            Console.WriteLine(trx is null
                ? "[TESTS] Geen TRX-bestand gevonden."
                : $"[TESTS] TRX gevonden: {trx}");

            int total = 0, passed = 0, failed = 0;
            List<(string name, string outcome, string message)> parsedTests;

            if (trx != null)
            {
                (total, passed, failed, parsedTests) = TrxParser.Parse(trx);
            }
            else
            {
                parsedTests = new();
            }

            result.Total  = total;
            result.Passed = passed;
            result.Failed = failed;
            result.Tests  = parsedTests
                .Select(t => new TestCaseResult
                {
                    Name    = t.name,
                    Outcome = t.outcome,
                    Message = string.IsNullOrWhiteSpace(t.message) ? null : t.message
                })
                .ToList();

            result.BinaryScore = (total > 0 && failed == 0) ? "GOED" : "FOUT";

            return result;
        }
        finally
        {
            // Als alles werkt kun je dit weer aanzetten:
            // try { Directory.Delete(work, true); } catch { }
        }
    }
}
