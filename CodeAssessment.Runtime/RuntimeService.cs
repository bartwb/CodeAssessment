using System.Diagnostics;
using System.Text;
using CodeAssessment.Shared;

namespace CodeAssessment.Runtime;

public class RuntimeService : IRuntimeService
{
    public async Task<CompileResponse> CompileOnlyAsync(CodeRequest req)
    {
        // tijdelijke werkmap
        var work = Path.Combine(Path.GetTempPath(), $"compile-{Guid.NewGuid():N}");
        Directory.CreateDirectory(work);

        string stdOut = "";
        string stdErr = "";
        int exitCode = -1;

        try
        {
            // 1) nieuw console-project
            var init = await ProcessRunner.RunAsync("dotnet", "new console -n UserApp", work, 60_000);
            if (init.ExitCode != 0)
            {
                return new CompileResponse(
                    Success: false,
                    StdOut: init.StdOut,
                    StdErr: init.StdErr,
                    ExitCode: init.ExitCode
                );
            }

            var projDir = Path.Combine(work, "UserApp");

            // 2) vervang Program.cs door de code van de kandidaat
            var programPath = Path.Combine(projDir, "Program.cs");
            await File.WriteAllTextAsync(programPath, req.Code);

            // 3) restore
            var restore = await ProcessRunner.RunAsync("dotnet", "restore", projDir, 120_000);
            if (restore.ExitCode != 0)
            {
                return new CompileResponse(
                    Success: false,
                    StdOut: restore.StdOut,
                    StdErr: restore.StdErr,
                    ExitCode: restore.ExitCode
                );
            }

            // 4) build (geen run)
            var build = await ProcessRunner.RunAsync("dotnet", "build --configuration Release", projDir, 180_000);

            stdOut = string.Join("\n\n", restore.StdOut, build.StdOut);
            stdErr = string.Join("\n\n", restore.StdErr, build.StdErr);
            exitCode = build.ExitCode;

            var success = build.ExitCode == 0;

            return new CompileResponse(
                Success: success,
                StdOut: stdOut,
                StdErr: stdErr,
                ExitCode: exitCode
            );
        }
        catch (Exception ex)
        {
            return new CompileResponse(
                Success: false,
                StdOut: stdOut,
                StdErr: stdErr + "\n" + ex.Message,
                ExitCode: exitCode
            );
        }
        finally
        {
            try { Directory.Delete(work, true); } catch { }
        }
    }
}
