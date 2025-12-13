using System.Diagnostics;
using System.Text;

namespace CodeAssessment.Tests.Internal;

internal static class ProcessRunner
{
    public static async Task<(int ExitCode, string StdOut, string StdErr)> RunAsync(
        string file, string args, string workingDir, int timeoutMs = 120_000)
    {
        var psi = new ProcessStartInfo(file, args)
        {
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var p = new Process { StartInfo = psi };
        var so = new StringBuilder();
        var se = new StringBuilder();

        p.OutputDataReceived += (_, e) => { if (e.Data != null) so.AppendLine(e.Data); };
        p.ErrorDataReceived  += (_, e) => { if (e.Data != null) se.AppendLine(e.Data); };

        p.Start();
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();

        using var cts = new CancellationTokenSource(timeoutMs);
        try
        {
            await p.WaitForExitAsync(cts.Token);
        }
        catch
        {
            try { p.Kill(entireProcessTree: true); } catch { }

            return (-1, so.ToString(), $"Timeout after {timeoutMs} ms");
        }

        return (p.ExitCode, so.ToString(), se.ToString());
    }
}
