using CodeAssessment.Shared;

namespace CodeAssessment.Runtime;

public class RuntimeExecutionService : IRuntimeExecutionService
{
    public async Task<RunResponse> RunAsync(CodeRequest req)
    {
        var work = Path.Combine(Path.GetTempPath(), $"run-{Guid.NewGuid():N}");
        Directory.CreateDirectory(work);

        string stdOut = "";
        string stdErr = "";
        int exitCode = -1;

        try
        {

            Console.WriteLine("[TESTS Runner]: Creating new project");
            // 1) nieuw console-project aanmaken
            async Task<ProcessRunner.ProcessResult> RunInitWithRetryAsync()
            {
                var attempt = 1;
                while (true)
                {
                    try
                    {
                        Console.WriteLine($"[TESTS Runner]: init attempt={attempt}");
                        var r = await ProcessRunner.RunAsync("dotnet", "new console -n UserApp --no-restore", work, 300_000);
                        if (r.ExitCode == 0) return r;
                        if (attempt >= 2) return r;
                        Console.WriteLine($"[TESTS Runner]: init retry after exitCode={r.ExitCode}");
                        await Task.Delay(1500);
                        attempt++;
                    }
                    catch (TimeoutException tex)
                    {
                        if (attempt >= 2) throw;
                        Console.WriteLine($"[TESTS Runner]: init timeout, retrying once: {tex.Message}");
                        await Task.Delay(1500);
                        attempt++;
                    }
                }
            }

            var init = await RunInitWithRetryAsync();
            if (init.ExitCode != 0)
            {
                return new RunResponse(
                    Success: false,
                    StdOut: init.StdOut,
                    StdErr: init.StdErr,
                    ExitCode: init.ExitCode
                );
            }

            var projDir = Path.Combine(work, "UserApp");
            var projFile = Path.Combine(projDir, "UserApp.csproj");
            try
            {
                if (File.Exists(projFile))
                {
                    var xml = await File.ReadAllTextAsync(projFile);
                    if (xml.Contains("<TargetFramework>net10.0</TargetFramework>"))
                    {
                        xml = xml.Replace("<TargetFramework>net10.0</TargetFramework>", "<TargetFramework>net8.0</TargetFramework>");
                        await File.WriteAllTextAsync(projFile, xml);
                        Console.WriteLine($"[TESTS Runner]: TFM set to net8.0 in '{projFile}'");
                    }
                }
            }
            catch (Exception exTfm)
            {
                Console.WriteLine($"[TESTS Runner]: Failed to update TFM in '{projFile}': {exTfm.Message}");
            }

            // 2) user code in Program.cs zetten
            Console.WriteLine("[TESTS Runner]: Writing user code");
            var programPath = Path.Combine(projDir, "Program.cs");
            await File.WriteAllTextAsync(programPath, req.Code);

            // 3) restore
            Console.WriteLine("[TESTS Runner]: Restoring project");
            var restore = await ProcessRunner.RunAsync("dotnet", "restore", projDir, 600_000);
            if (restore.ExitCode != 0)
            {
                Console.WriteLine("[TESTS Runner]: Restore failed..." + restore.StdOut + restore.StdErr + restore.ExitCode);
                return new RunResponse(
                    Success: false,
                    StdOut: restore.StdOut,
                    StdErr: restore.StdErr,
                    ExitCode: restore.ExitCode
                );
            }

            // 4) build (Release)
            Console.WriteLine("[TESTS Runner]: Building project");
            var build = await ProcessRunner.RunAsync("dotnet", "build --configuration Release", projDir, 460_000);
            if (build.ExitCode != 0)
            {
                stdOut = string.Join("\n\n", restore.StdOut, build.StdOut);
                stdErr = string.Join("\n\n", restore.StdErr, build.StdErr);

                Console.WriteLine("[TESTS Runner]: Build failed..." + stdOut + stdErr + build.ExitCode);
                return new RunResponse(
                    Success: false,
                    StdOut: stdOut,
                    StdErr: stdErr,
                    ExitCode: build.ExitCode
                );
            }

            // 5) run zonder opnieuw te builden
            Console.WriteLine("[TESTS Runner]: Run without new build");
            var run = await ProcessRunner.RunAsync(
                "dotnet",
                "run --configuration Release --no-build",
                projDir,
                120_000
            );

            stdOut = string.Join("\n\n", restore.StdOut, build.StdOut) + "\n\n" + run.StdOut;
            stdErr = string.Join("\n\n", restore.StdErr, build.StdErr) + "\n\n" + run.StdErr;
            exitCode = run.ExitCode;

            var success = run.ExitCode == 0;

            return new RunResponse(
                Success: success,
                StdOut: stdOut,
                StdErr: stdErr,
                ExitCode: exitCode
            );
        }
        catch (Exception ex)
        {
            Console.WriteLine("[TESTS Runner]: Fail in project creation: " + ex);
            return new RunResponse(
                Success: false,
                StdOut: stdOut,
                StdErr: stdErr + "\n" + ex.Message,
                ExitCode: exitCode
            );
        }
        finally
        {
            Console.WriteLine("[TESTS Runner]: Project creation finished...");
            try { Directory.Delete(work, true); } catch { }
        }
    }
}
