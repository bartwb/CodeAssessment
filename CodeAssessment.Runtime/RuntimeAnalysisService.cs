using CodeAssessment.Shared;

namespace CodeAssessment.Runtime;

public class RuntimeAnalysisService : IRuntimeAnalysisService
{
    public async Task<RuntimeAnalysisResult> AnalyzeAsync(CodeRequest req, int samplingIntervalMs = 500)
    {
        var work = Path.Combine(Path.GetTempPath(), $"run-analyze-{Guid.NewGuid():N}");
        Directory.CreateDirectory(work);

        var result = new RuntimeAnalysisResult();
        var phases = new List<RuntimePhaseSummary>();

        try
        {
            var projDir = Path.Combine(work, "UserApp");
            Directory.CreateDirectory(projDir);

            // init
            var swInit = System.Diagnostics.Stopwatch.StartNew();
            var init = await ProcessRunner.RunAsync("dotnet", "new console -o UserApp", work, 60_000);
            swInit.Stop();

            phases.Add(new RuntimePhaseSummary
            {
                Name = "init",
                DurationMs = (long)swInit.Elapsed.TotalMilliseconds,
                CpuTimeMs = null,
                PeakWorkingSet = null,
                LogStdOut = init.StdOut,
                LogStdErr = init.StdErr,
                Samples = null
            });

            if (init.ExitCode != 0)
            {
                result.StdOut = init.StdOut;
                result.StdErr = init.StdErr;
                result.ExitCode = init.ExitCode;
                result.Phases = phases;
                return result;
            }

            await File.WriteAllTextAsync(Path.Combine(projDir, "Program.cs"), req.Code);

            // restore
            var swRestore = System.Diagnostics.Stopwatch.StartNew();
            var rr = await ProcessRunner.RunAsync("dotnet", "restore", projDir, 120_000);
            swRestore.Stop();

            phases.Add(new RuntimePhaseSummary
            {
                Name = "restore",
                DurationMs = (long)swRestore.Elapsed.TotalMilliseconds,
                CpuTimeMs = null,
                PeakWorkingSet = null,
                LogStdOut = rr.StdOut,
                LogStdErr = rr.StdErr,
                Samples = null
            });

            if (rr.ExitCode != 0)
            {
                result.StdOut = rr.StdOut;
                result.StdErr = rr.StdErr;
                result.ExitCode = rr.ExitCode;
                result.Phases = phases;
                return result;
            }

            // build
            var swBuild = System.Diagnostics.Stopwatch.StartNew();
            var rb = await ProcessRunner.RunAsync("dotnet", "build --configuration Release", projDir, 180_000);
            swBuild.Stop();

            phases.Add(new RuntimePhaseSummary
            {
                Name = "build",
                DurationMs = (long)swBuild.Elapsed.TotalMilliseconds,
                CpuTimeMs = null,
                PeakWorkingSet = null,
                LogStdOut = rb.StdOut,
                LogStdErr = rb.StdErr,
                Samples = null
            });

            if (rb.ExitCode != 0)
            {
                result.StdOut = string.Join("\n\n", rr.StdOut, rb.StdOut);
                result.StdErr = string.Join("\n\n", rr.StdErr, rb.StdErr);
                result.ExitCode = rb.ExitCode;
                result.Phases = phases;
                return result;
            }

            // publish
            var publishOut = Path.Combine(projDir, "publish");
            Directory.CreateDirectory(publishOut);

            var swPublish = System.Diagnostics.Stopwatch.StartNew();
            var rp = await ProcessRunner.RunAsync(
                "dotnet",
                $"publish UserApp.csproj -c Release -o \"{publishOut}\"",
                projDir,
                120_000);
            swPublish.Stop();

            phases.Add(new RuntimePhaseSummary
            {
                Name = "publish",
                DurationMs = (long)swPublish.Elapsed.TotalMilliseconds,
                CpuTimeMs = null,
                PeakWorkingSet = null,
                LogStdOut = rp.StdOut,
                LogStdErr = rp.StdErr,
                Samples = null
            });

            if (rp.ExitCode != 0)
            {
                result.StdOut = string.Join("\n\n", rr.StdOut, rb.StdOut, rp.StdOut);
                result.StdErr = string.Join("\n\n", rr.StdErr, rb.StdErr, rp.StdErr);
                result.ExitCode = rp.ExitCode;
                result.Phases = phases;
                return result;
            }

            // dll zoeken
            var outDll = Directory.EnumerateFiles(publishOut, "*.dll", SearchOption.TopDirectoryOnly)
                                  .FirstOrDefault()
                        ?? throw new InvalidOperationException("Publish-output niet gevonden.");

            // run met sampling
            var run = await RunnerHelpers.RunWithSamplingAsync(
                "dotnet",
                $"\"{outDll}\"",
                projDir,
                samplingIntervalMs,
                120_000
            );

            result.StdOut = string.Join("\n\n", rr.StdOut, rb.StdOut, rp.StdOut, run.StdOut);
            result.StdErr = string.Join("\n\n", rr.StdErr, rb.StdErr, rp.StdErr, run.StdErr);
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

            // gemiddelde CPU over de hele run-phase
            double? avgCpuPct = null;
            if (run.DurationMs > 0)
            {
                avgCpuPct = (run.TotalCpuTimeMs / (double)run.DurationMs) 
                            * Environment.ProcessorCount * 100.0;
            }

            result.RunDurationMs     = run.DurationMs;
            result.RunTotalCpuTimeMs = run.TotalCpuTimeMs;
            result.RunAverageCpuPct  = avgCpuPct;
            result.RunPeakWorkingSet = run.PeakWorkingSet;

            result.Phases = phases;
            return result;
        }
        finally
        {
            try { Directory.Delete(work, true); } catch { }
        }
    }
}
