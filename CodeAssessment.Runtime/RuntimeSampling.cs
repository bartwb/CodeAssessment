using System.Diagnostics;
using System.Text;

namespace CodeAssessment.Runtime;

// interne sample en resultaat
public record MetricSample(long T, double CpuPct, long WorkingSet);

public record RunPhaseResult(
    int ExitCode,
    string StdOut,
    string StdErr,
    long DurationMs,
    long TotalCpuTimeMs,
    long PeakWorkingSet,
    List<MetricSample> Samples
);

public static class RunnerHelpers
{
    public static async Task<RunPhaseResult> RunWithSamplingAsync(
        string file,
        string args,
        string workingDir,
        int samplingIntervalMs,
        int timeoutMs)
    {
        var psi = new ProcessStartInfo(file, args)
        {
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var so = new StringBuilder();
        var se = new StringBuilder();

        p.OutputDataReceived += (_, e) => { if (e.Data != null) so.AppendLine(e.Data); };
        p.ErrorDataReceived  += (_, e) => { if (e.Data != null) se.AppendLine(e.Data); };

        var samples = new List<MetricSample>();
        var swWall = Stopwatch.StartNew();

        p.Start();
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();

        var cpuPrev = TimeSpan.Zero;
        var wallPrev = swWall.Elapsed;
        var peakWs = 0L;
        long lastSeenCpuMs = 0;

        using var cts = new CancellationTokenSource(timeoutMs);
        try
        {
            while (!p.HasExited)
            {
                var delayTask = Task.Delay(samplingIntervalMs, cts.Token);
                var exitTask  = p.WaitForExitAsync(cts.Token);
                await Task.WhenAny(delayTask, exitTask);
                if (cts.IsCancellationRequested || p.HasExited) break;

                try
                {
                    var wallNow = swWall.Elapsed;

                    var cpuNowMs  = SafeTotalCpuMs(p);
                    lastSeenCpuMs = cpuNowMs;
                    var wallNowMs = (long)wallNow.TotalMilliseconds;

                    var cpuDeltaMs  = cpuNowMs - (long)cpuPrev.TotalMilliseconds;
                    var wallDeltaMs = wallNowMs - (long)wallPrev.TotalMilliseconds;

                    var cpuPct = 0.0;
                    if (wallDeltaMs > 0)
                        cpuPct = (cpuDeltaMs / (double)wallDeltaMs) * Environment.ProcessorCount * 100.0;

                    var ws = SafeWorkingSet(p);
                    if (ws > peakWs) peakWs = ws;

                    samples.Add(new MetricSample(
                        T: wallNowMs,
                        CpuPct: cpuPct < 0 ? 0 : cpuPct,
                        WorkingSet: ws
                    ));

                    cpuPrev  = TimeSpan.FromMilliseconds(cpuNowMs);
                    wallPrev = TimeSpan.FromMilliseconds(wallNowMs);
                }
                catch
                {
                    // best-effort
                }
            }
        }
        catch
        {
            try { if (!p.HasExited) p.Kill(entireProcessTree: true); } catch { }
            var durMsTimeout = (long)swWall.Elapsed.TotalMilliseconds;
            var totalCpuTimeMsTimeout = SafeTotalCpuMs(p);
            return new RunPhaseResult(
                ExitCode: -1,
                StdOut: so.ToString(),
                StdErr: se.ToString() + $"\nTimeout after {timeoutMs} ms",
                DurationMs: durMsTimeout,
                TotalCpuTimeMs: totalCpuTimeMsTimeout,
                PeakWorkingSet: peakWs,
                Samples: samples
            );
        }

        var preFinalCpuMs = SafeTotalCpuMs(p);
        await p.WaitForExitAsync();

        var tEnd = (long)swWall.Elapsed.TotalMilliseconds;
        var cpuEndMs = SafeTotalCpuMs(p);
        var wsEnd = SafeWorkingSet(p);
        if (samples.Count == 0 || samples[^1].T < tEnd)
        {
            samples.Add(new MetricSample(
                T: tEnd,
                CpuPct: 0,
                WorkingSet: wsEnd
            ));
        }

        var durationMs = (long)swWall.Elapsed.TotalMilliseconds;
        var postFinalCpuMs = SafeTotalCpuMs(p);
        var totalCpuTimeMsFinal = Math.Max(postFinalCpuMs, Math.Max(preFinalCpuMs, lastSeenCpuMs));

        return new RunPhaseResult(
            ExitCode: p.ExitCode,
            StdOut: so.ToString(),
            StdErr: se.ToString(),
            DurationMs: durationMs,
            TotalCpuTimeMs: totalCpuTimeMsFinal,
            PeakWorkingSet: peakWs,
            Samples: samples
        );
    }

    static long SafeTotalCpuMs(Process p)
    {
        try { p.Refresh(); return (long)p.TotalProcessorTime.TotalMilliseconds; }
        catch { return 0; }
    }

    static long SafeWorkingSet(Process p)
    {
        try { p.Refresh(); return p.WorkingSet64; }
        catch { return 0; }
    }
}
