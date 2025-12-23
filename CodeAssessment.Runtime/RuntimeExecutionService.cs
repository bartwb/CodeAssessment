using CodeAssessment.Shared;

namespace CodeAssessment.Runtime;

public class RuntimeExecutionService : IRuntimeExecutionService
{
public static async Task<ProcessResult> RunAsync(
    string fileName,
    string arguments,
    string workingDirectory,
    int timeoutMs)
{
    var psi = new ProcessStartInfo(fileName, arguments)
    {
        WorkingDirectory = workingDirectory,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
    };

    // Container-safe defaults (zoals je al had)
    psi.Environment["HOME"] = "/tmp";
    psi.Environment["DOTNET_CLI_HOME"] = "/tmp/dotnet";
    psi.Environment["NUGET_PACKAGES"] = "/tmp/nuget";
    psi.Environment["TMPDIR"] = "/tmp";
    psi.Environment["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1";
    psi.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
    psi.Environment["DOTNET_NOLOGO"] = "1";
    psi.Environment["NUGET_XMLDOC_MODE"] = "skip";

    using var p = new Process { StartInfo = psi, EnableRaisingEvents = true };

    var so = new StringBuilder();
    var se = new StringBuilder();

    var exitedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

    var sw = Stopwatch.StartNew();
    var lastOut = DateTimeOffset.UtcNow;
    var lastErr = DateTimeOffset.UtcNow;

    void TouchOut() => lastOut = DateTimeOffset.UtcNow;
    void TouchErr() => lastErr = DateTimeOffset.UtcNow;

    p.OutputDataReceived += (_, e) =>
    {
        if (e.Data != null)
        {
            TouchOut();
            so.AppendLine(e.Data);
        }
    };

    p.ErrorDataReceived += (_, e) =>
    {
        if (e.Data != null)
        {
            TouchErr();
            se.AppendLine(e.Data);
        }
    };

    p.Exited += (_, _) => exitedTcs.TrySetResult(true);

    Console.WriteLine(
        $"PROC START file='{fileName}' pid=? args='{arguments}' wd='{workingDirectory}' timeoutMs={timeoutMs}"
    );

    try
    {
        p.Start();
    }
    catch (Exception startEx)
    {
        Console.WriteLine($"PROC START FAILED file='{fileName}' args='{arguments}' ex={startEx}");
        throw;
    }

    Console.WriteLine($"PROC PID={p.Id} startedAtUtc={DateTimeOffset.UtcNow:O}");

    p.BeginOutputReadLine();
    p.BeginErrorReadLine();

    using var cts = new CancellationTokenSource(timeoutMs);

    // Heartbeat elke 5 seconden
    using var hbCts = new CancellationTokenSource();
    var heartbeatTask = Task.Run(async () =>
    {
        while (!hbCts.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), hbCts.Token).ContinueWith(_ => { });
            if (hbCts.IsCancellationRequested) break;

            var outAge = (DateTimeOffset.UtcNow - lastOut).TotalSeconds;
            var errAge = (DateTimeOffset.UtcNow - lastErr).TotalSeconds;

            long memKb = 0;
            try { memKb = p.WorkingSet64 / 1024; } catch { }

            Console.WriteLine(
                $"PROC HB pid={SafePid(p)} elapsedMs={sw.ElapsedMilliseconds} outAgeSec={outAge:F0} errAgeSec={errAge:F0} memKb={memKb} outLen={so.Length} errLen={se.Length}"
            );
        }
    });

    static int SafePid(Process proc) { try { return proc.Id; } catch { return -1; } }

    var completedTask = await Task.WhenAny(exitedTcs.Task, Task.Delay(Timeout.Infinite, cts.Token));
    hbCts.Cancel();

    if (completedTask != exitedTcs.Task)
    {
        Console.WriteLine($"PROC TIMEOUT pid={SafePid(p)} elapsedMs={sw.ElapsedMilliseconds} file='{fileName}' args='{arguments}' wd='{workingDirectory}'");

        // Dump extra context bij timeout
        try
        {
            Console.WriteLine($"PROC TIMEOUT dirExists={Directory.Exists(workingDirectory)}");
            if (Directory.Exists(workingDirectory))
            {
                foreach (var f in Directory.GetFiles(workingDirectory, "*", SearchOption.AllDirectories).Take(50))
                    Console.WriteLine($"PROC TIMEOUT FILE {f}");
            }
        }
        catch (Exception dumpEx)
        {
            Console.WriteLine($"PROC TIMEOUT DUMP FAILED: {dumpEx.GetType().Name}: {dumpEx.Message}");
        }

        try { p.Kill(entireProcessTree: true); } catch { }

        throw new TimeoutException($"Process '{fileName} {arguments}' timed out after {timeoutMs} ms.");
    }

    await exitedTcs.Task;
    p.WaitForExit();

    // kleine snippet altijd, ook bij succes (super handig)
    static string Clip(string? s, int max = 800) =>
        string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s[..max] + "\n...(clipped)");

    Console.WriteLine(
        $"PROC END pid={SafePid(p)} file='{fileName}' args='{arguments}' wd='{workingDirectory}' " +
        $"exitCode={p.ExitCode} elapsedMs={sw.ElapsedMilliseconds} outLen={so.Length} errLen={se.Length}"
    );

    Console.WriteLine($"PROC OUT SNIP:\n{Clip(so.ToString())}");
    Console.WriteLine($"PROC ERR SNIP:\n{Clip(se.ToString())}");

    return new ProcessResult(
        ExitCode: p.ExitCode,
        StdOut: so.ToString(),
        StdErr: se.ToString()
    );
}

}
