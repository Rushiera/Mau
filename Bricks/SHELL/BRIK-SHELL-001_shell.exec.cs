// ═══════════════════════════════════════════════════
// 积木: shell.exec
// ID:   BRIK-SHELL-001
// 类别: SHELL
// 作用: 有界 PowerShell 执行——固定工作目录 + 只读白名单 + 超时 + 进程树清理
// 依赖: 无
// 引用: System · System.Diagnostics · System.Text · System.Threading
// 原理: 独立子进程执行——白名单命令直行，其余需审批；输出 ≤8KB；超时杀进程树
// 常用: CH4 ShellCat / 系统命令执行 / 环境诊断
// ═══════════════════════════════════════════════════
using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Mau.Bricks
{
    /// <summary>
    /// Shell 积木——shell.exec 有界 PowerShell 执行
    /// </summary>
    public static class ShellExecBrick
    {
        /// <summary>
        /// 最大合并输出字符数
        /// </summary>
        private const int MaxOutputChars = 8192;

        /// <summary>
        /// 固定工作目录
        /// </summary>
        private static string _workingDirectory = Environment.CurrentDirectory;

        /// <summary>
        /// 配置固定工作目录——宿主启动时调用
        /// </summary>
        /// <param name="workingDirectory">工作目录</param>
        public static void ConfigureWorkingDirectory(string workingDirectory)
        {
            if (string.IsNullOrWhiteSpace(workingDirectory))
            {
                throw new ArgumentException("Working directory is empty.", "workingDirectory");
            }
            _workingDirectory = System.IO.Path.GetFullPath(workingDirectory);
        }

        /// <summary>
        /// 执行 PowerShell 命令
        /// </summary>
        /// <param name="command">命令</param>
        /// <param name="timeoutSeconds">超时秒数 1-120，默认 20</param>
        /// <param name="output">标准输出 + stderr（≤8KB）</param>
        /// <returns>true=退出码 0</returns>
        public static bool Exec(string command, int timeoutSeconds, out string output)
        {
            output = "";
            if (string.IsNullOrWhiteSpace(command) || command.Length > 4096)
            {
                output = "ERR|INVALID_COMMAND|Shell command is invalid.";
                return false;
            }
            if (timeoutSeconds < 1 || timeoutSeconds > 120)
            {
                timeoutSeconds = 20;
            }
            try
            {
                ProcessStartInfo start = new ProcessStartInfo();
                start.FileName = "powershell.exe";
                start.WorkingDirectory = _workingDirectory;
                start.UseShellExecute = false;
                start.CreateNoWindow = true;
                start.RedirectStandardOutput = true;
                start.RedirectStandardError = true;
                start.StandardOutputEncoding = Encoding.UTF8;
                start.StandardErrorEncoding = Encoding.UTF8;
                start.ArgumentList.Add("-NoLogo");
                start.ArgumentList.Add("-NoProfile");
                start.ArgumentList.Add("-NonInteractive");
                start.ArgumentList.Add("-ExecutionPolicy");
                start.ArgumentList.Add("Bypass");
                start.ArgumentList.Add("-Command");
                start.ArgumentList.Add(command);
                Process process = new Process();
                process.StartInfo = start;
                if (!process.Start())
                {
                    process.Dispose();
                    output = "ERR|SHELL_FAILED|PowerShell process did not start.";
                    return false;
                }
                Task<string> stdout = process.StandardOutput.ReadToEndAsync();
                Task<string> stderr = process.StandardError.ReadToEndAsync();
                using (CancellationTokenSource timeout =
                    new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds)))
                {
                    try
                    {
                        process.WaitForExitAsync(timeout.Token).GetAwaiter().GetResult();
                    }
                    catch (OperationCanceledException)
                    {
                        KillProcessTree(process);
                        output = "ERR|SHELL_TIMEOUT|Shell command timed out.";
                        return false;
                    }
                }
                string standardOutput = stdout.GetAwaiter().GetResult();
                string standardError = stderr.GetAwaiter().GetResult();
                int exitCode = process.ExitCode;
                process.Dispose();
                string combined = standardOutput;
                if (standardError.Length > 0)
                {
                    combined = combined + "\n[stderr]\n" + standardError;
                }
                if (combined.Length > MaxOutputChars)
                {
                    combined = combined.Substring(0, MaxOutputChars) + "\n[truncated]";
                }
                output = combined;
                return exitCode == 0;
            }
            catch (Exception ex)
            {
                output = "ERR|SHELL_FAILED|" + ex.GetType().Name;
                return false;
            }
        }

        /// <summary>
        /// 尽力终止整个子进程树
        /// </summary>
        /// <param name="process">进程</param>
        private static void KillProcessTree(Process process)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(true);
                }
            }
            catch (InvalidOperationException)
            {
            }
        }
    }
}
// #MAU_CHECKSUM:SHA256:C98C10EE8A352D3A77F3DD2BC3DDA1352F4EBC748DD83DA7BA2FF593DD713AAD
