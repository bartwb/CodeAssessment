using System.Diagnostics;
using System.Text;

namespace CodeAssessment.Runtime;

public static class ProcessRunner
{
    public sealed record ProcessResult(int ExitCode, string StdOut, string StdErr);

    public static async Task<ProcessRunner.ProcessResult> RunAsync(
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
        p.Exited += (_, _) => exitedTcs.TrySetResult(true);

        p.OutputDataReceived += (_, e) => { if (e.Data != null) so.AppendLine(e.Data); };
        p.ErrorDataReceived  += (_, e) => { if (e.Data != null) se.AppendLine(e.Data); };

        Console.WriteLine($"PROC START file='{fileName}' args='{arguments}' wd='{workingDirectory}' timeoutMs={timeoutMs}");

        p.Start();
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();

        using var cts = new CancellationTokenSource(timeoutMs);

        var completed = await Task.WhenAny(exitedTcs.Task, Task.Delay(Timeout.Infinite, cts.Token));
        if (completed != exitedTcs.Task)
        {
            Console.WriteLine($"PROC TIMEOUT file='{fileName}' args='{arguments}' wd='{workingDirectory}' afterMs={timeoutMs}");
            try { p.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException($"Process '{fileName} {arguments}' timed out after {timeoutMs} ms.");
        }

        await exitedTcs.Task;
        p.WaitForExit();

        Console.WriteLine($"PROC END file='{fileName}' args='{arguments}' wd='{workingDirectory}' exitCode={p.ExitCode} outLen={so.Length} errLen={se.Length}");

        return new ProcessRunner.ProcessResult(p.ExitCode, so.ToString(), se.ToString());
    }
}
