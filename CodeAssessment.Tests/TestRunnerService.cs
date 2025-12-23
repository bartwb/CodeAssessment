using System.Text;
using CodeAssessment.Shared;
using CodeAssessment.Tests.Internal;

namespace CodeAssessment.Tests;

public class TestRunnerService : ITestRunnerService
{
    public async Task<TestsAnalysisResult> RunTestsAsync(CodeRequest req)
    {
        var result = new TestsAnalysisResult();

        void Log(string msg) => Console.WriteLine($"[TESTS] {msg}");

        static string TrimHuge(string s, int max = 40_000)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.Length <= max) return s;
            return s[..max] + "\n[TESTS] ... (truncated) ...";
        }

        static void TryListFiles(Action<string> log, string dir, string pattern = "*")
        {
            try
            {
                if (!Directory.Exists(dir))
                {
                    log($"listFiles: dir does not exist: {dir}");
                    return;
                }

                log($"listFiles: {dir} (pattern={pattern})");
                foreach (var f in Directory.GetFiles(dir, pattern, SearchOption.AllDirectories))
                    log($"  - {f}");
            }
            catch (Exception ex)
            {
                log($"listFiles failed for {dir}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        if (string.IsNullOrWhiteSpace(req.Code))
        {
            result.BinaryScore = "FOUT";
            return result;
        }

        var work = Path.Combine(Path.GetTempPath(), $"run-{Guid.NewGuid():N}");
        Directory.CreateDirectory(work);

        Log($"work dir = {work}");
        Log($"baseDir = {AppContext.BaseDirectory}");
        Log($"tempPath = {Path.GetTempPath()}");

        try
        {
            // 1) Kandidaten-classlib
            Log("STEP 1: dotnet new classlib");
            var libInit = await ProcessRunner.RunAsync("dotnet", "new classlib -o AssesmentSolution", work);
            Log($"libInit exit={libInit.ExitCode}");
            if (!string.IsNullOrWhiteSpace(libInit.StdErr)) Log($"libInit stderr:\n{TrimHuge(libInit.StdErr)}");
            if (!string.IsNullOrWhiteSpace(libInit.StdOut)) Log($"libInit stdout:\n{TrimHuge(libInit.StdOut)}");

            if (libInit.ExitCode != 0)
            {
                result.BinaryScore = "FOUT";
                result.RawStdOut   = libInit.StdOut;
                result.RawStdErr   = libInit.StdErr;
                result.ExitCode    = libInit.ExitCode;
                return result;
            }

            var solutionFile = Path.Combine(work, "AssesmentSolution", "Solution.cs");
            await File.WriteAllTextAsync(solutionFile, req.Code);
            Log($"Wrote candidate code to {solutionFile} (bytes={new FileInfo(solutionFile).Length})");

            // 2) Tests-template kopiëren vanuit Api-output
            Log("STEP 2: copy test template");
            var baseDir  = AppContext.BaseDirectory;
            var srcTpl   = Path.Combine(baseDir, "templates", "Tests");
            var dstTests = Path.Combine(work, "Tests");

            Log($"srcTpl = {srcTpl}");
            Log($"srcTpl exists? {Directory.Exists(srcTpl)}");

            if (!Directory.Exists(srcTpl))
            {
                result.BinaryScore = "FOUT";
                result.RawStdErr   = $"Templates-directory niet gevonden: {srcTpl}";
                return result;
            }

            FsUtil.CopyDirectory(srcTpl, dstTests);
            Log($"Copied template to {dstTests}");
            TryListFiles(Log, dstTests);

            // 2b) Zoeken naar het echte test-csproj
            Log("STEP 2b: find tests csproj");
            var testsCsprojFull = Directory.GetFiles(dstTests, "*.csproj", SearchOption.AllDirectories)
                                           .FirstOrDefault();

            if (testsCsprojFull is null)
            {
                result.BinaryScore = "FOUT";
                result.RawStdErr   = $"Geen .csproj gevonden in test-template map: {dstTests}";
                return result;
            }

            var testsCsprojRel = Path.GetRelativePath(work, testsCsprojFull).Replace('\\', '/');

            Log($"testsCsprojFull = {testsCsprojFull}");
            Log($"testsCsprojRel  = {testsCsprojRel}");

            // 3) Project reference: Tests -> AssesmentSolution
            Log("STEP 3: dotnet add reference");
            var addRef = await ProcessRunner.RunAsync(
                "dotnet",
                $"add \"{testsCsprojRel}\" reference AssesmentSolution/AssesmentSolution.csproj",
                work
            );

            Log($"addRef exit={addRef.ExitCode}");
            if (!string.IsNullOrWhiteSpace(addRef.StdErr)) Log($"addRef stderr:\n{TrimHuge(addRef.StdErr)}");
            if (!string.IsNullOrWhiteSpace(addRef.StdOut)) Log($"addRef stdout:\n{TrimHuge(addRef.StdOut)}");

            if (addRef.ExitCode != 0)
            {
                result.BinaryScore = "FOUT";
                result.RawStdOut   = addRef.StdOut;
                result.RawStdErr   = addRef.StdErr;
                result.ExitCode    = addRef.ExitCode;
                return result;
            }


            // 4) Restore/build/test
            Log("STEP 4a: dotnet restore");
            var rr = await ProcessRunner.RunAsync(
                "dotnet",
                $"restore \"{testsCsprojRel}\" --disable-parallel --verbosity normal",
                work,
                900_000
            );

            Log($"restore exit={rr.ExitCode}");
            if (!string.IsNullOrWhiteSpace(rr.StdErr)) Log($"restore stderr:\n{TrimHuge(rr.StdErr)}");
            if (!string.IsNullOrWhiteSpace(rr.StdOut)) Log($"restore stdout:\n{TrimHuge(rr.StdOut)}");

            if (rr.ExitCode != 0)
            {
                result.BinaryScore = "FOUT";
                result.RawStdOut   = rr.StdOut;
                result.RawStdErr   = rr.StdErr;
                result.ExitCode    = rr.ExitCode;
                return result;
            }

            Log("STEP 4b: dotnet build");
            var rb = await ProcessRunner.RunAsync(
                "dotnet",
                $"build \"{testsCsprojRel}\" -c Release -m:1",
                work,
                240_000
            );

            Log($"build exit={rb.ExitCode}");
            if (!string.IsNullOrWhiteSpace(rb.StdErr)) Log($"build stderr:\n{TrimHuge(rb.StdErr)}");
            if (!string.IsNullOrWhiteSpace(rb.StdOut)) Log($"build stdout:\n{TrimHuge(rb.StdOut)}");

            if (rb.ExitCode != 0)
            {
                result.BinaryScore = "FOUT";
                result.RawStdOut   = rb.StdOut;
                result.RawStdErr   = rb.StdErr;
                result.ExitCode    = rb.ExitCode;
                return result;
            }

            // TRX output deterministisch
            var resultsDir = Path.Combine(work, "_results");
            Directory.CreateDirectory(resultsDir);
            Log($"resultsDir = {resultsDir}");

            Log("STEP 4c-1: dotnet test --list-tests (discovery only)");
            var list = await ProcessRunner.RunAsync(
                "dotnet",
                $"test \"{testsCsprojRel}\" -c Release --no-build --no-restore --list-tests --verbosity normal",
                work,
                360_000
            );
            Log($"list-tests exit={list.ExitCode}");

            Log("STEP 4c: dotnet test");
            Log("ENV: dotnet --info");
            var rt = await ProcessRunner.RunAsync(
                "dotnet",
                $"test \"{testsCsprojRel}\" --configuration Release --no-build --no-restore " +
                $"--results-directory \"{resultsDir}\" " +
                $"--logger \"trx;LogFileName=results.trx\" " +
                $"--diag \"{Path.Combine(resultsDir, "vstest-diag.txt")}\" " +
                $"--blame --blame-hang --blame-hang-timeout 5m",
                work,
                360_000
            );

            Log($"test exit={rt.ExitCode}");
            if (!string.IsNullOrWhiteSpace(rt.StdErr)) Log($"test stderr:\n{TrimHuge(rt.StdErr)}");
            if (!string.IsNullOrWhiteSpace(rt.StdOut)) Log($"test stdout:\n{TrimHuge(rt.StdOut)}");

            // Combineer output voor terugkoppeling
            result.RawStdOut = string.Join("\n\n", rr.StdOut, rb.StdOut, rt.StdOut);
            result.RawStdErr = string.Join("\n\n", rr.StdErr, rb.StdErr, rt.StdErr);
            result.ExitCode  = rt.ExitCode;

            // 5) TRX zoeken + parse
            Log("STEP 5: find/parse trx");
            var expectedTrx = Path.Combine(resultsDir, "results.trx");
            var expectedDiag = Path.Combine(resultsDir, "vstest-diag.txt");

            Log($"expected trx  = {expectedTrx} exists={File.Exists(expectedTrx)}");
            Log($"expected diag = {expectedDiag} exists={File.Exists(expectedDiag)}");

            if (!File.Exists(expectedTrx))
            {
                // Dump wat er wel geschreven is
                TryListFiles(Log, resultsDir);
            }

            string? trx = File.Exists(expectedTrx)
                ? expectedTrx
                : Directory.GetFiles(resultsDir, "*.trx", SearchOption.AllDirectories)
                           .OrderBy(File.GetLastWriteTimeUtc)
                           .LastOrDefault();

            Log(trx is null ? "Geen TRX-bestand gevonden." : $"TRX gevonden: {trx}");

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
            result.Tests  = parsedTests.Select(t => new TestCaseResult
            {
                Name    = t.name,
                Outcome = t.outcome,
                Message = string.IsNullOrWhiteSpace(t.message) ? null : t.message
            }).ToList();

            result.BinaryScore = (total > 0 && failed == 0) ? "GOED" : "FOUT";
            return result;
        }
        catch (Exception ex)
        {
            // Dit vangt ook “vage” issues af die anders alleen tot ontbrekende logs leiden.
            Console.WriteLine($"[TESTS] UNHANDLED EXCEPTION: {ex.GetType().Name}: {ex.Message}");
            Console.WriteLine($"[TESTS] STACK:\n{ex}");

            result.BinaryScore = "FOUT";
            result.RawStdErr   = ex.ToString();
            result.ExitCode    = -999;
            return result;
        }
        finally
        {
            // Als alles werkt kun je dit weer aanzetten:
            // try { Directory.Delete(work, true); } catch { }
        }
    }
}
