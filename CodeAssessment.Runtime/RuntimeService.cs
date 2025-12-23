using System.Diagnostics;
using System.Text;
using CodeAssessment.Shared;

namespace CodeAssessment.Runtime;

public class RuntimeService : IRuntimeService
{
    public async Task<CompileResponse> CompileOnlyAsync(CodeRequest req)
    {
        var sw = Stopwatch.StartNew();

        static int Len(string? s) => string.IsNullOrEmpty(s) ? 0 : s.Length;
        static string Clip(string? s, int max = 200) =>
            string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s[..max] + "...");

        // tijdelijke werkmap
        var work = Path.Combine(Path.GetTempPath(), $"compile-{Guid.NewGuid():N}");
        Directory.CreateDirectory(work);

        string stdOut = "";
        string stdErr = "";
        int exitCode = -1;

        Console.WriteLine(
            $"COMPILE IN  codeLen={Len(req?.Code)} lang='{req?.LanguageVersion}' work='{work}'"
        );

        try
        {
            // 1) nieuw console-project
            Console.WriteLine("COMPILE STEP step=init_start");
            var init = await ProcessRunner.RunAsync(
                "dotnet",
                "new console -n UserApp --no-restore --nologo",
                work,
                300_000
            );

            Console.WriteLine(
                $"COMPILE STEP step=init_done " +
                $"exitCode={init.ExitCode} outLen={Len(init.StdOut)} errLen={Len(init.StdErr)} " +
                $"elapsedMs={sw.ElapsedMilliseconds}"
            );

            if (init.ExitCode != 0)
            {
                Console.WriteLine(
                    $"COMPILE BAD reason=init_failed " +
                    $"exitCode={init.ExitCode} err='{Clip(init.StdErr)}' " +
                    $"elapsedMs={sw.ElapsedMilliseconds}"
                );

                return new CompileResponse(
                    Success: false,
                    StdOut: init.StdOut,
                    StdErr: init.StdErr,
                    ExitCode: init.ExitCode
                );
            }

            var projDir = Path.Combine(work, "UserApp");

            // 2) vervang Program.cs door de code van de kandidaat
            Console.WriteLine($"COMPILE STEP step=write_program_start path='{projDir}'");
            var programPath = Path.Combine(projDir, "Program.cs");
            Console.WriteLine(
                $"COMPILE DEBUG writing Program.cs path='{programPath}' exists={File.Exists(programPath)}"
            );
            Console.WriteLine(
                $"COMPILE DEBUG Program.cs content snippet:\n{Clip(req.Code, 300)}"
            );
            await File.WriteAllTextAsync(programPath, req.Code);
            Console.WriteLine($"COMPILE STEP step=write_program_done elapsedMs={sw.ElapsedMilliseconds}");

            // 3) restore
            Console.WriteLine("COMPILE STEP step=restore_start");
            var restore = await ProcessRunner.RunAsync(
                "dotnet",
                "restore",
                projDir,
                120_000
            );

            Console.WriteLine("COMPILE DEBFUG project files:");
            foreach (var f in Directory.GetFiles(work, "*.csproj", SearchOption.AllDirectories))
                Console.WriteLine("  csproj: " + f);

            foreach (var f in Directory.GetFiles(work, "Program.cs", SearchOption.AllDirectories))
                Console.WriteLine("  program: " + f);

            Console.WriteLine(
                $"COMPILE STEP step=restore_done " +
                $"exitCode={restore.ExitCode} outLen={Len(restore.StdOut)} errLen={Len(restore.StdErr)} " +
                $"elapsedMs={sw.ElapsedMilliseconds}"
            );

            if (restore.ExitCode != 0)
            {
                Console.WriteLine(
                    $"COMPILE BAD reason=restore_failed " +
                    $"exitCode={restore.ExitCode} err='{Clip(restore.StdErr)}' " +
                    $"elapsedMs={sw.ElapsedMilliseconds}"
                );

                return new CompileResponse(
                    Success: false,
                    StdOut: restore.StdOut,
                    StdErr: restore.StdErr,
                    ExitCode: restore.ExitCode
                );
            }

            // 4) build (geen run)
            Console.WriteLine("COMPILE STEP step=build_start");
            Console.WriteLine($"COMPILE DEBUG build workingDir='{projDir}'");
            var build = await ProcessRunner.RunAsync(
                "dotnet",
                "build --configuration Release",
                projDir,
                180_000
            );

            Console.WriteLine(
                $"COMPILE STEP step=build_done " +
                $"exitCode={build.ExitCode} outLen={Len(build.StdOut)} errLen={Len(build.StdErr)} " +
                $"elapsedMs={sw.ElapsedMilliseconds}"
            );

            stdOut = string.Join("\n\n", restore.StdOut, build.StdOut);
            stdErr = string.Join("\n\n", restore.StdErr, build.StdErr);
            exitCode = build.ExitCode;

            var success = build.ExitCode == 0;

            sw.Stop();
            Console.WriteLine(
                $"COMPILE OK  success={success} exitCode={exitCode} " +
                $"outLen={Len(stdOut)} errLen={Len(stdErr)} elapsedMs={sw.ElapsedMilliseconds}"
            );

            return new CompileResponse(
                Success: success,
                StdOut: stdOut,
                StdErr: stdErr,
                ExitCode: exitCode
            );
        }
        catch (Exception ex)
        {
            sw.Stop();

            Console.WriteLine(
                $"COMPILE ERR elapsedMs={sw.ElapsedMilliseconds}\n{ex}"
            );

            return new CompileResponse(
                Success: false,
                StdOut: stdOut,
                StdErr: stdErr + "\n" + ex.Message,
                ExitCode: exitCode
            );
        }
        finally
        {
            Console.WriteLine("COMPILE STEP step=cleanup_start");
            try
            {
                Directory.Delete(work, true);
                Console.WriteLine(
                    $"COMPILE STEP step=cleanup_done elapsedMs={sw.ElapsedMilliseconds}"
                );
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"COMPILE WARN step=cleanup_failed err='{Clip(ex.Message)}' " +
                    $"elapsedMs={sw.ElapsedMilliseconds}"
                );
            }
        }
    }
}
