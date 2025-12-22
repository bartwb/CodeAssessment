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
            // 1) Kandidaten-classlib
            var libInit = await ProcessRunner.RunAsync("dotnet", "new classlib -o AssesmentSolution", work);
            Console.WriteLine($"[TESTS] libInit exit={libInit.ExitCode}");
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
            var baseDir  = AppContext.BaseDirectory; // bin\Debug\net10.0 van Api
            var srcTpl   = Path.Combine(baseDir, "templates", "Tests");
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

            Console.WriteLine($"[TESTS] testsCsprojRel = {testsCsprojRel}");
            Console.WriteLine($"[TESTS] testsCsprojFull = {testsCsprojFull}");

            // 3) Project reference: Tests -> AssesmentSolution
            var addRef = await ProcessRunner.RunAsync(
                "dotnet",
                $"add \"{testsCsprojRel}\" reference AssesmentSolution/AssesmentSolution.csproj",
                work
            );

            Console.WriteLine($"[TESTS] addRef exit={addRef.ExitCode}");
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
            Console.WriteLine($"[TESTS] restore exit={rr.ExitCode}");
            if (rr.ExitCode != 0)
            {
                result.BinaryScore = "FOUT";
                result.RawStdOut   = rr.StdOut;
                result.RawStdErr   = rr.StdErr;
                result.ExitCode    = rr.ExitCode;
                return result;
            }

            var rb = await ProcessRunner.RunAsync(
                "dotnet",
                $"build \"{testsCsprojRel}\" --configuration Release",
                work,
                180_000
            );
            Console.WriteLine($"[TESTS] build exit={rb.ExitCode}");
            if (rb.ExitCode != 0)
            {
                result.BinaryScore = "FOUT";
                result.RawStdOut   = rb.StdOut;
                result.RawStdErr   = rb.StdErr;
                result.ExitCode    = rb.ExitCode;
                return result;
            }

            // Maak TRX output deterministisch
            var resultsDir = Path.Combine(work, "_results");
            Directory.CreateDirectory(resultsDir);

            var rt = await ProcessRunner.RunAsync(
                "dotnet",
                $"test \"{testsCsprojRel}\" --configuration Release " +
                $"--results-directory \"{resultsDir}\" " +
                $"--logger \"trx;LogFileName=results.trx\" " +
                $"--diag \"{Path.Combine(resultsDir, "vstest-diag.txt")}\"",
                work,
                240_000
            );

            Console.WriteLine($"[TESTS] test exit={rt.ExitCode}");
            if (!string.IsNullOrWhiteSpace(rt.StdErr))
                Console.WriteLine($"[TESTS] test stderr:\n{rt.StdErr}");
            if (!string.IsNullOrWhiteSpace(rt.StdOut))
                Console.WriteLine($"[TESTS] test stdout:\n{rt.StdOut}");

            // Combineer output voor terugkoppeling
            result.RawStdOut = string.Join("\n\n", rr.StdOut, rb.StdOut, rt.StdOut);
            result.RawStdErr = string.Join("\n\n", rr.StdErr, rb.StdErr, rt.StdErr);
            result.ExitCode  = rt.ExitCode;

            // 5) TRX zoeken + parse (nu op vaste plek)
            var expectedTrx = Path.Combine(resultsDir, "results.trx");
            Console.WriteLine($"[TESTS] expected trx = {expectedTrx} exists={File.Exists(expectedTrx)}");

            string? trx = File.Exists(expectedTrx)
                ? expectedTrx
                : Directory.GetFiles(resultsDir, "*.trx", SearchOption.AllDirectories)
                           .OrderBy(File.GetLastWriteTimeUtc)
                           .LastOrDefault();

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
