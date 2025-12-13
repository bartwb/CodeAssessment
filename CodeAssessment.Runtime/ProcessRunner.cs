using System.Diagnostics;
using System.Text;

namespace CodeAssessment.Runtime;

public static class ProcessRunner
{
    public sealed record ProcessResult(
        int ExitCode,
        string StdOut,
        string StdErr
    );

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
            RedirectStandardError  = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var p = new Process { StartInfo = psi };

        var so = new StringBuilder();
        var se = new StringBuilder();

        var tcs = new TaskCompletionSource<bool>();

        p.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null)
                so.AppendLine(e.Data);
        };

        p.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null)
                se.AppendLine(e.Data);
        };

        p.EnableRaisingEvents = true;
        p.Exited += (_, _) => tcs.TrySetResult(true);

        p.Start();
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();

        using var cts = new CancellationTokenSource(timeoutMs);

        var completedTask = await Task.WhenAny(tcs.Task, Task.Delay(Timeout.Infinite, cts.Token));
        if (completedTask != tcs.Task)
        {
            try { p.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException($"Process '{fileName} {arguments}' timed out after {timeoutMs} ms.");
        }

        await tcs.Task; // zorg dat alles klaar is

        return new ProcessResult(
            ExitCode: p.ExitCode,
            StdOut: so.ToString(),
            StdErr: se.ToString()
        );
    }
}
