// ═══════════════════════════════════════════════════
// 积木: shell.exec
// ID:   BRIK-SHELL-001
// 类别: SHELL
// 作用: 有界 PowerShell 执行——能力分类 + 白名单直行 + 审批制 + 超时 + 进程树清理
// 依赖: 无
// 引用: System · System.Diagnostics · System.Text · System.Threading · Mau.Runtime
// 原理: 命令首词能力分类（0=只读白名单直行 / 1=非白名单需审批 / 2=危险拒绝）
//       → 审批经 DataBox 绑定 IShellApprovalHandler（宿主弹窗），无处理器=默认拒绝
//       → 独立子进程执行（固定工作目录 + 输出 ≤8KB + 超时杀进程树）
// 常用: CH4 ShellCat / 系统命令执行 / 环境诊断
// 安全: P0-5 安全审查项——默认拒绝任意 Shell；写/删/网络/进程管理类必须审批
// ═══════════════════════════════════════════════════
using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Mau.Runtime;
namespace Mau.Bricks
{
    /// <summary>
    /// Shell 积木——shell.exec 有界 PowerShell 执行（能力分类 + 审批制）
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
        /// 只读白名单——直接执行不审批（命令首词小写匹配）
        /// </summary>
        private static readonly string[] ReadOnlyWhitelist = new string[]
        {
            "dir", "type", "findstr", "where", "echo", "ipconfig", "ping",
            "nslookup", "whoami", "hostname", "tasklist", "ver", "date", "time",
            "get-childitem", "get-content", "get-process", "get-service", "get-help",
            "select-string", "measure-object"
        };

        /// <summary>
        /// 危险片段——无条件拒绝（不可审批）：格式化/关机/递归删除/注册表写
        /// </summary>
        private static readonly string[] DangerousPatterns = new string[]
        {
            "format", "shutdown", "restart-computer", "stop-computer",
            "remove-item -r", "rm -rf", "reg add", "reg delete"
        };

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
        /// 命令能力分类——0=只读白名单直行 1=非白名单需审批 2=危险拒绝
        /// </summary>
        /// <param name="command">完整命令</param>
        /// <returns>类别</returns>
        public static int Classify(string command)
        {
            string head = CommandHead(command);
            if (head.Length == 0)
            {
                return 2;
            }
            string lower = command == null ? "" : command.ToLowerInvariant();
            for (int i = 0; i < DangerousPatterns.Length; i++)
            {
                if (lower.IndexOf(DangerousPatterns[i], StringComparison.Ordinal) >= 0)
                {
                    return 2;
                }
            }
            for (int i = 0; i < ReadOnlyWhitelist.Length; i++)
            {
                if (head == ReadOnlyWhitelist[i])
                {
                    return 0;
                }
            }
            return 1;
        }

        /// <summary>
        /// 提取命令首词——去前导引号/空白/参数
        /// </summary>
        /// <param name="command">完整命令</param>
        /// <returns>首词（小写）</returns>
        private static string CommandHead(string command)
        {
            string trimmed = (command ?? "").Trim();
            if (trimmed.Length == 0)
            {
                return "";
            }
            int start = 0;
            while (start < trimmed.Length
                && (trimmed[start] == '"' || trimmed[start] == '\'' || char.IsWhiteSpace(trimmed[start])))
            {
                start = start + 1;
            }
            int end = start;
            while (end < trimmed.Length && !char.IsWhiteSpace(trimmed[end]))
            {
                end = end + 1;
            }
            return trimmed.Substring(start, end - start).ToLowerInvariant();
        }

        /// <summary>
        /// 执行 PowerShell 命令——能力分类 + 审批制（P0-5 安全审查项）
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

            // [安全段] 能力分类——白名单直行 / 非白名单审批 / 危险拒绝
            int category = Classify(command);
            if (category == 2)
            {
                output = "ERR|SHELL_DANGEROUS|Command is not allowed: " + command;
                return false;
            }
            if (category == 1)
            {
                IShellApprovalHandler? handler;
                DataBox.TryResolve<IShellApprovalHandler>(out handler);
                if (handler == null || !handler.Approve(command))
                {
                    output = "ERR|SHELL_APPROVAL_DENIED|Command requires approval: " + command;
                    return false;
                }
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
// #MAU_CHECKSUM:SHA256:54BEF29EB984B6B78F43AD3CD35053BB6A74B333BB2F18861D83C60D4E4C883D
