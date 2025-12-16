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

        Console.WriteLine(
            $"PROC START file='{fileName}' args='{arguments}' wd='{workingDirectory}' timeoutMs={timeoutMs}"
        );

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

        static int Len(string? s) => string.IsNullOrEmpty(s) ? 0 : s.Length;
        static string Clip(string? s, int max = 400) =>
            string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s[..max] + "...");

        Console.WriteLine(
            $"PROC END   file='{fileName}' args='{arguments}' wd='{workingDirectory}' " +
            $"exitCode={p.ExitCode} outLen={Len(so.ToString())} errLen={Len(se.ToString())}"
        );

        if (p.ExitCode != 0)
        {
            Console.WriteLine($"PROC OUT  snippet='{Clip(so.ToString())}'");
            Console.WriteLine($"PROC ERR  snippet='{Clip(se.ToString())}'");
        }


        return new ProcessResult(
            ExitCode: p.ExitCode,
            StdOut: so.ToString(),
            StdErr: se.ToString()
        );
    }
}
