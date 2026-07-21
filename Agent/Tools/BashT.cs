using Microsoft.Win32;
using OpenAI.Models.Chat;
using Pandora.Interfaces;
using Pandora.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace Pandora.Agent.Tools
{
    public class BashT : IAgentTool
    {
        private static string? PWSHFileName= GetPWSHFileName();
        public AgentTool GetToolDefinition(ISession session)
        {
            return new AgentTool()
            {
                ToolName = "bash",
                FullDescription = "Execute a shell command and return the output. "
                    + "On this system, PowerShell is the shell — write commands directly (e.g. `Get-ChildItem`, `$var = value`). "
                    + "Do NOT prefix with `powershell` or `pwsh`. Do NOT include comments — just the command itself. "
                    + "Use for: listing files, searching code, git operations, running scripts, "
                    + "building projects, or any CLI task. Do NOT use for long-running servers — "
                    + "this tool waits for the command to finish (max 5 minutes).",
                Parameters = [
                    new() { Name = "command", Type = AgentParametersType.STRING, Description = "The shell command to execute", Required = true },
                    new() { Name = "timeout", Type = AgentParametersType.INT, Description = "Max execution time in seconds (1-300, default 60)", Required = false },
                ],
                FullLoad = true,
                ParametersTypeCheck = true,
                ReadOnly = false,
                Enabled= PWSHFileName!= null,
                SupportedModes = WorkMode.Working | WorkMode.Coding,
                ToolFunction = Execute
            };
        }

        public void Init(ISession session)
        {
            return;
        }

        private (MessageContent? ret, ToolsResult retSatus) Execute(ISession session, AgentToolParameterValue param)
        {
            if(PWSHFileName==null)
                return (new MessageContent("Error: PowerShell is not installed"), ToolsResult.UnKnownError);
            string command = param.GetString("command");
            if (string.IsNullOrWhiteSpace(command))
                return (new MessageContent("Error: command is empty"), ToolsResult.ParametersError);

            int timeoutSec = param.Has("timeout")
                ? Math.Clamp(param.GetInt("timeout"), 1, 300)
                : 60;

            if (!session.SafetyManager.ConfirmBashCommand(command))
                return (new MessageContent("Bash command rejected by user"), ToolsResult.ParametersError);

            string workDir = session.AgentEnvironment.WorkingDirectory ?? Environment.CurrentDirectory;

            var psi = new ProcessStartInfo
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = workDir,
            };

            var sysPath = Environment.GetEnvironmentVariable("PATH") ?? "";
            psi.Environment["PATH"] = sysPath;

            psi.FileName = "powershell.exe";
            var cleanedCommand = command;
            if (cleanedCommand.StartsWith("powershell ", StringComparison.OrdinalIgnoreCase))
                cleanedCommand = cleanedCommand.Substring(11).TrimStart();
            else if (cleanedCommand.StartsWith("pwsh ", StringComparison.OrdinalIgnoreCase))
                cleanedCommand = cleanedCommand.Substring(5).TrimStart();

            const string pwshPrefix =
                "[Console]::OutputEncoding=[Console]::InputEncoding=[System.Text.Encoding]::UTF8;" +
                "$ProgressPreference='SilentlyContinue';" +
                "$InformationPreference='SilentlyContinue';";
            var wrappedCommand = pwshPrefix + cleanedCommand;
            var bytes = Encoding.Unicode.GetBytes(wrappedCommand);
            psi.Arguments = $"-NoProfile -ExecutionPolicy Bypass -EncodedCommand {Convert.ToBase64String(bytes)}";

            psi.StandardOutputEncoding = Encoding.UTF8;
            psi.StandardErrorEncoding = Encoding.UTF8;

            var sbOut = new StringBuilder();
            var sbErr = new StringBuilder();
            int? exitCode = null;
            bool timedOut = false;
            const int maxOutputChars = 8000;

            try
            {
                using var process = new Process { StartInfo = psi };

                process.OutputDataReceived += (_, e) =>
                {
                    if (e.Data != null) sbOut.AppendLine(e.Data);
                };
                process.ErrorDataReceived += (_, e) =>
                {
                    if (e.Data != null) sbErr.AppendLine(e.Data);
                };

                process.Start();


                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                if (!process.WaitForExit(timeoutSec * 1000))
                {
                    timedOut = true;
                    KillProcessTree(process);
                }
                else
                {
                    process.WaitForExit();
                    exitCode = process.ExitCode;
                }
            }
            catch (Exception ex)
            {
                return (new MessageContent($"bash error: {ex.Message}"), ToolsResult.UnKnownError);
            }

            var result = new StringBuilder();

            if (timedOut)
            {
                result.AppendLine("[timeout]");
            }
            else
            {
                result.AppendLine($"[exit:{exitCode}]");
            }

            string stdout = sbOut.ToString();
            string stderr = sbErr.ToString();
            bool truncated = false;

            if (stdout.Length > maxOutputChars)
            {
                stdout = stdout.Substring(0, maxOutputChars);
                truncated = true;
            }

            result.Append(stdout);

            if (stderr.Length > 0)
            {
                stderr = FilterClixml(stderr);
                if (string.IsNullOrWhiteSpace(stderr)) goto skipStderr;

                result.AppendLine("**[stderr]**");
                if (truncated || stderr.Length + stdout.Length > maxOutputChars)
                {
                    int remaining = maxOutputChars - stdout.Length;
                    if (remaining > 0)
                        stderr = stderr.Substring(0, Math.Min(remaining, stderr.Length));
                    else
                        stderr = "";
                    truncated = true;
                }
                result.Append(stderr);
            }
        skipStderr:

            if (truncated)
            {
                result.AppendLine("\n**[truncated]**");
            }

            return (new MessageContent(result.ToString()), ToolsResult.Success);
        }

        private static string FilterClixml(string stderr)
        {
            if (string.IsNullOrWhiteSpace(stderr)) return stderr;
            if (!stderr.Contains("#< CLIXML") && !stderr.Contains("<Objs"))
                return stderr;

            var sb = new StringBuilder();
            foreach (var line in stderr.Split('\n'))
            {
                var t = line.Trim();
                if (t.StartsWith("#< CLIXML") || t.StartsWith("<Objs") ||
                    t.StartsWith("<Obj") || t.StartsWith("</Objs>"))
                    continue;
                sb.AppendLine(line);
            }
            return sb.ToString().TrimEnd();
        }
        private static string? GetPWSHFileName()
        {
            if (!OperatingSystem.IsWindows())
            {
                return null;
            }
            if (HasInstalledPowerShell7())
            {
                return "pwsh.exe";
            }
            if (HasWindowsPowerShell5())
            {
                return "powershell.exe";
            }
            return null;
        }
        private static void KillProcessTree(Process process)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                try { process.Kill(); } catch { }
            }
        }
        private static bool HasInstalledPowerShell7()
        {
            var versions = new List<string>();
            // 两个注册表路径都要遍历
            string[] regPaths =
            {
            @"SOFTWARE\Microsoft\PowerShellCore\InstalledVersions",
            @"SOFTWARE\WOW6432Node\Microsoft\PowerShellCore\InstalledVersions"
        };

            foreach (var path in regPaths)
            {
                using var key = Registry.LocalMachine.OpenSubKey(path);
                if (key == null) continue;

                foreach (var subKeyName in key.GetSubKeyNames())
                {
                    // 匹配7.x 版本
                    if (subKeyName.StartsWith("7."))
                    {
                        return true;
                    }
                }
            }
            return false;
        }
        private static bool HasWindowsPowerShell5()
        {
            try
            {
                using var reg = Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\PowerShell\1\ShellIds\Microsoft.PowerShell");
                return reg != null;
            }
            catch
            {
                return false;
            }
        }
    }
}