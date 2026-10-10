using System;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using Mau.Runtime;

namespace Mau.Development
{
    /// <summary>
    /// 子进程运行结果。
    /// </summary>
    public sealed class ProcessRunResult
    {
        /// <summary>
        /// 进程是否成功启动
        /// </summary>
        public bool Started;

        /// <summary>
        /// 是否在 watchdog 时限内正常退出（false = 超时强杀）
        /// </summary>
        public bool Exited;

        /// <summary>
        /// 退出码（未正常退出时访问无意义——保持 0）
        /// </summary>
        public int ExitCode;

        /// <summary>
        /// 标准输出全文
        /// </summary>
        public string Stdout;

        /// <summary>
        /// 标准错误全文
        /// </summary>
        public string Stderr;

        /// <summary>
        /// 创建空结果
        /// </summary>
        public ProcessRunResult()
        {
            Stdout = "";
            Stderr = "";
        }
    }

    /// <summary>
    /// 子进程运行器——双流异步并行读 + watchdog 强杀。
    /// 管道死锁根治法：启动后立即并行消费 stdout/stderr——顺序 ReadToEnd 在单侧输出填满管道时死锁（Codex 审查 P1 判例：MauTestRunner/MauGroupBuilder 同源问题）。
    /// </summary>
    public static class ProcessRunner
    {
        /// <summary>
        /// 运行子进程并捕获全部输出——双流异步并行读 + watchdog 超时强杀
        /// </summary>
        /// <param name="fileName">可执行文件</param>
        /// <param name="arguments">参数文本</param>
        /// <param name="workingDirectory">工作目录（空 = 继承当前目录）</param>
        /// <param name="timeoutMs">watchdog 超时毫秒</param>
        /// <returns>运行结果（Started=false 表示无法启动）</returns>
        public static ProcessRunResult RunAndCapture(string fileName, string arguments, string? workingDirectory, int timeoutMs)
        {
            ProcessRunResult result = new ProcessRunResult();
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = fileName;
            psi.Arguments = arguments;
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            // 输出编码显式 UTF-8——子进程（dotnet / Mau 系）输出为 UTF-8，默认按控制台编码解会乱码（中文 Windows 控制台 = GBK，判例 A210）
            psi.StandardOutputEncoding = new UTF8Encoding(false);
            psi.StandardErrorEncoding = new UTF8Encoding(false);
            if (!string.IsNullOrEmpty(workingDirectory))
            {
                psi.WorkingDirectory = workingDirectory;
            }
            Process? process;
            try
            {
                process = Process.Start(psi);
            }
            catch (Exception ex)
            {
                LogStore.Add("Mau", 2, "进程启动失败: " + ex.Message, "PROC");
                return result;
            }
            if (process == null)
            {
                return result;
            }
            result.Started = true;
            // [段1] 启动后立即双流并行读——顺序 ReadToEnd 与 WaitForExit 组合是经典管道死锁（一侧写满 64KB 管道阻塞，双方互等）
            Task<string> outTask = process.StandardOutput.ReadToEndAsync();
            Task<string> errTask = process.StandardError.ReadToEndAsync();
            bool exited = process.WaitForExit(timeoutMs);
            if (!exited)
            {
                try
                {
                    process.Kill(true);
                }
                catch (InvalidOperationException ex)
                {
                    // 进程已在强杀前自行退出
                    LogStore.Add("Mau", 2, "强杀前进程已自行退出: " + ex.Message, "PROC");
                }
            }
            try
            {
                result.Stdout = outTask.GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                // 读取异常不掩盖主结果
                LogStore.Add("Mau", 2, "子进程 stdout 读取失败: " + ex.Message, "PROC");
            }
            try
            {
                result.Stderr = errTask.GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                // 读取异常不掩盖主结果
                LogStore.Add("Mau", 2, "子进程 stderr 读取失败: " + ex.Message, "PROC");
            }
            result.Exited = exited;
            if (exited)
            {
                result.ExitCode = process.ExitCode;
            }
            process.Dispose();
            return result;
        }
    }
}