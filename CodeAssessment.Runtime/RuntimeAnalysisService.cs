using CodeAssessment.Shared;
using System.Diagnostics;

namespace CodeAssessment.Runtime;

public class RuntimeAnalysisService : IRuntimeAnalysisService
{
    private const int InfoTimeoutMs    = 60_000;
    private const int NugetTimeoutMs   = 60_000;
    private const int InitTimeoutMs    = 120_000;

    private const int RestoreTimeoutMs = 900_000; // 15 min
    private const int BuildTimeoutMs   = 600_000; // 10 min
    private const int PublishTimeoutMs = 600_000; // 10 min
    private const int RunTimeoutMs     = 180_000; // 3 min

    public async Task<RuntimeAnalysisResult> AnalyzeAsync(CodeRequest req, int samplingIntervalMs = 500)
    {
        var work = Path.Combine(Path.GetTempPath(), $"run-analyze-{Guid.NewGuid():N}");
        Directory.CreateDirectory(work);

        var result = new RuntimeAnalysisResult();
        var phases = new List<RuntimePhaseSummary>();

        void Log(string msg) => Console.WriteLine($"[ANALYZE] {msg}");

        void AddPhase(string name, Stopwatch sw, ProcessRunner.ProcessResult pr)
        {
            phases.Add(new RuntimePhaseSummary
            {
                Name = name,
                DurationMs = sw.ElapsedMilliseconds,
                CpuTimeMs = null,
                PeakWorkingSet = null,
                LogStdOut = pr.StdOut,
                LogStdErr = pr.StdErr,
                Samples = null
            });
        }

        static string JoinOut(params string[] parts) => string.Join("\n\n", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
        static string JoinErr(params string[] parts) => string.Join("\n\n", parts.Where(p => !string.IsNullOrWhiteSpace(p)));

        static IEnumerable<string> SafeListFiles(string dir, int max = 50)
        {
            try
            {
                if (!Directory.Exists(dir)) return new[] { $"(dir missing) {dir}" };
                return Directory.GetFiles(dir, "*", SearchOption.AllDirectories).Take(max);
            }
            catch (Exception ex)
            {
                return new[] { $"(list failed) {ex.GetType().Name}: {ex.Message}" };
            }
        }

        try
        {
            Log($"START workDir={work} samplingIntervalMs={samplingIntervalMs}");

            if (string.IsNullOrWhiteSpace(req.Code))
            {
                Log("ERROR empty code request");
                result.StdErr = "Empty code request.";
                result.ExitCode = -1;
                result.Phases = phases;
                return result;
            }

            var projDir = Path.Combine(work, "UserApp");

            // ENV: dotnet --info
            {
                Log($"PHASE env_dotnet_info START timeoutMs={InfoTimeoutMs}");
                var sw = Stopwatch.StartNew();
                var info = await ProcessRunner.RunAsync("dotnet", "--info", work, InfoTimeoutMs);
                sw.Stop();
                Log($"PHASE env_dotnet_info END exitCode={info.ExitCode} elapsedMs={sw.ElapsedMilliseconds}");
                AddPhase("env_dotnet_info", sw, info);

                if (info.ExitCode != 0)
                {
                    Log("ERROR dotnet --info failed; stopping early.");
                    result.StdOut = info.StdOut;
                    result.StdErr = info.StdErr;
                    result.ExitCode = info.ExitCode;
                    result.Phases = phases;
                    return result;
                }
            }

            // ENV: nuget sources
            {
                Log($"PHASE env_nuget_sources START timeoutMs={NugetTimeoutMs}");
                var sw = Stopwatch.StartNew();
                var src = await ProcessRunner.RunAsync("dotnet", "nuget list source", work, NugetTimeoutMs);
                sw.Stop();
                Log($"PHASE env_nuget_sources END exitCode={src.ExitCode} elapsedMs={sw.ElapsedMilliseconds}");
                AddPhase("env_nuget_sources", sw, src);
            }

            // INIT
            {
                Log($"PHASE init START wd={work} timeoutMs={InitTimeoutMs}");
                var sw = Stopwatch.StartNew();
                var init = await ProcessRunner.RunAsync(
                    "dotnet",
                    "new console -o UserApp --no-restore --nologo",
                    work,
                    InitTimeoutMs
                );
                sw.Stop();
                Log($"PHASE init END exitCode={init.ExitCode} elapsedMs={sw.ElapsedMilliseconds}");
                AddPhase("init", sw, init);

                if (init.ExitCode != 0)
                {
                    Log("ERROR init failed");
                    result.StdOut = init.StdOut;
                    result.StdErr = init.StdErr;
                    result.ExitCode = init.ExitCode;
                    result.Phases = phases;
                    return result;
                }
            }

            // Write Program.cs
            Directory.CreateDirectory(projDir);
            var programPath = Path.Combine(projDir, "Program.cs");
            await File.WriteAllTextAsync(programPath, req.Code);

            var bytes = new FileInfo(programPath).Length;
            Log($"WROTE programPath={programPath} bytes={bytes}");

            // RESTORE
            ProcessRunner.ProcessResult rr;
            {
                Log($"PHASE restore START wd={projDir} timeoutMs={RestoreTimeoutMs}");
                var sw = Stopwatch.StartNew();
                rr = await ProcessRunner.RunAsync(
                    "dotnet",
                    "restore --disable-parallel --nologo --verbosity minimal",
                    projDir,
                    RestoreTimeoutMs
                );
                sw.Stop();
                Log($"PHASE restore END exitCode={rr.ExitCode} elapsedMs={sw.ElapsedMilliseconds}");
                AddPhase("restore", sw, rr);

                if (rr.ExitCode != 0)
                {
                    Log("ERROR restore failed; listing project dir files (first 50):");
                    foreach (var f in SafeListFiles(projDir, 50))
                        Log($"  FILE {f}");

                    result.StdOut = rr.StdOut;
                    result.StdErr = rr.StdErr;
                    result.ExitCode = rr.ExitCode;
                    result.Phases = phases;
                    return result;
                }
            }

            // BUILD
            ProcessRunner.ProcessResult rb;
            {
                Log($"PHASE build START wd={projDir} timeoutMs={BuildTimeoutMs}");
                var sw = Stopwatch.StartNew();
                rb = await ProcessRunner.RunAsync(
                    "dotnet",
                    "build -c Release -m:1 --no-restore --nologo --verbosity minimal",
                    projDir,
                    BuildTimeoutMs
                );
                sw.Stop();
                Log($"PHASE build END exitCode={rb.ExitCode} elapsedMs={sw.ElapsedMilliseconds}");
                AddPhase("build", sw, rb);

                if (rb.ExitCode != 0)
                {
                    Log("ERROR build failed; listing project dir files (first 50):");
                    foreach (var f in SafeListFiles(projDir, 50))
                        Log($"  FILE {f}");

                    result.StdOut = JoinOut(rr.StdOut, rb.StdOut);
                    result.StdErr = JoinErr(rr.StdErr, rb.StdErr);
                    result.ExitCode = rb.ExitCode;
                    result.Phases = phases;
                    return result;
                }
            }

            // PUBLISH
            ProcessRunner.ProcessResult rp;
            var publishOut = Path.Combine(projDir, "publish");
            Directory.CreateDirectory(publishOut);

            {
                Log($"PHASE publish START wd={projDir} out={publishOut} timeoutMs={PublishTimeoutMs}");
                var sw = Stopwatch.StartNew();
                rp = await ProcessRunner.RunAsync(
                    "dotnet",
                    $"publish UserApp.csproj -c Release -o \"{publishOut}\" --no-build --no-restore --nologo --verbosity minimal",
                    projDir,
                    PublishTimeoutMs
                );
                sw.Stop();
                Log($"PHASE publish END exitCode={rp.ExitCode} elapsedMs={sw.ElapsedMilliseconds}");
                AddPhase("publish", sw, rp);

                if (rp.ExitCode != 0)
                {
                    Log("ERROR publish failed; listing publish dir files (first 50):");
                    foreach (var f in SafeListFiles(publishOut, 50))
                        Log($"  FILE {f}");

                    result.StdOut = JoinOut(rr.StdOut, rb.StdOut, rp.StdOut);
                    result.StdErr = JoinErr(rr.StdErr, rb.StdErr, rp.StdErr);
                    result.ExitCode = rp.ExitCode;
                    result.Phases = phases;
                    return result;
                }
            }

            // DLL locate
            var outDll = Path.Combine(publishOut, "UserApp.dll");
            if (!File.Exists(outDll))
            {
                outDll = Directory.EnumerateFiles(publishOut, "*.dll", SearchOption.TopDirectoryOnly)
                                  .FirstOrDefault()
                         ?? throw new InvalidOperationException("Publish-output DLL niet gevonden.");
            }
            Log($"PUBLISH DLL={outDll} sizeBytes={new FileInfo(outDll).Length}");

            Log("PUBLISH output files (top-level):");
            foreach (var f in Directory.EnumerateFiles(publishOut, "*", SearchOption.TopDirectoryOnly).Take(30))
                Log($"  OUT {f}");

            // RUN with sampling
            Log($"PHASE run START wd={projDir} timeoutMs={RunTimeoutMs} samplingIntervalMs={samplingIntervalMs}");
            var run = await RunnerHelpers.RunWithSamplingAsync(
                "dotnet",
                $"\"{outDll}\"",
                projDir,
                samplingIntervalMs,
                RunTimeoutMs
            );
            Log($"PHASE run END exitCode={run.ExitCode} durationMs={run.DurationMs} cpuTimeMs={run.TotalCpuTimeMs} peakWs={run.PeakWorkingSet}");

            result.StdOut = JoinOut(rr.StdOut, rb.StdOut, rp.StdOut, run.StdOut);
            result.StdErr = JoinErr(rr.StdErr, rb.StdErr, rp.StdErr, run.StdErr);
            result.ExitCode = run.ExitCode;

            var samples = run.Samples.Select(s => new RuntimeMetricSample
            {
                T = s.T,
                CpuPct = s.CpuPct,
                WorkingSet = s.WorkingSet
            }).ToList();

            phases.Add(new RuntimePhaseSummary
            {
                Name = "run",
                DurationMs = run.DurationMs,
                CpuTimeMs = run.TotalCpuTimeMs,
                PeakWorkingSet = run.PeakWorkingSet,
                LogStdOut = null,
                LogStdErr = null,
                Samples = samples
            });

            double? avgCpuPct = null;
            if (run.DurationMs > 0)
            {
                avgCpuPct = (run.TotalCpuTimeMs / (double)run.DurationMs) * 100.0 / Math.Max(1, Environment.ProcessorCount);
            }

            result.RunDurationMs = run.DurationMs;
            result.RunTotalCpuTimeMs = run.TotalCpuTimeMs;
            result.RunAverageCpuPct = avgCpuPct;
            result.RunPeakWorkingSet = run.PeakWorkingSet;

            result.Phases = phases;

            Log($"DONE exitCode={result.ExitCode} runDurationMs={result.RunDurationMs} avgCpuPct={result.RunAverageCpuPct:F2} peakWs={result.RunPeakWorkingSet}");
            return result;
        }
        catch (Exception ex)
        {
            Log($"UNHANDLED EXCEPTION: {ex.GetType().Name}: {ex.Message}");
            Log($"STACK:\n{ex}");

            result.StdErr = (result.StdErr ?? "") + "\n" + ex;
            result.ExitCode = result.ExitCode == 0 ? -999 : result.ExitCode;
            result.Phases = phases;
            return result;
        }
        finally
        {
            Log($"CLEANUP workDir={work}");
            try { Directory.Delete(work, true); } catch (Exception ex) { Log($"CLEANUP FAILED: {ex.GetType().Name}: {ex.Message}"); }
        }
    }
}
