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
            var init = await ProcessRunner.RunAsync("dotnet", "new console -n UserApp", work, 300_000);
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
