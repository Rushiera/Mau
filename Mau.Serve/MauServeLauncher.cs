using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace Mau.Serve
{
    /// <summary>
    /// 服务启动器——按项目派生 Roslyn 工作进程。
    /// 管道名 = 项目哈希（mau-{hash8}），同项目复用同一工作进程，跨项目天然隔离。
    /// </summary>
    public static class MauServeLauncher
    {
        /// <summary>
        /// 管道名前缀
        /// </summary>
        public const string PipePrefix = "mau-";

        /// <summary>
        /// 由项目根计算管道名——mau-{SHA256 前 8 位}
        /// </summary>
        /// <param name="projectRoot">项目根</param>
        /// <returns>管道名</returns>
        public static string PipeNameFor(string projectRoot)
        {
            string full = Path.GetFullPath(projectRoot);
            byte[] bytes = Encoding.UTF8.GetBytes(full);
            byte[] hash = SHA256.HashData(bytes);
            StringBuilder hex = new StringBuilder();
            for (int i = 0; i < 4; i = i + 1)
            {
                hex.Append(hash[i].ToString("X2"));
            }
            return PipePrefix + hex.ToString();
        }

        /// <summary>
        /// 口袋编译输出根——系统临时目录按项目隔离
        /// </summary>
        /// <param name="projectRoot">项目根</param>
        /// <returns>输出根</returns>
        public static string PocketRootFor(string projectRoot)
        {
            string hash = PipeNameFor(projectRoot).Substring(PipePrefix.Length);
            return Path.Combine(Path.GetTempPath(), "mau_pocket_" + hash);
        }

        /// <summary>
        /// 派生工作进程——已在线则复用
        /// </summary>
        /// <param name="projectRoot">项目源码根</param>
        /// <param name="exePath">工作进程可执行文件路径——空则用当前进程路径</param>
        /// <param name="timeoutMs">等待就绪超时（毫秒）</param>
        /// <returns>管道名</returns>
        public static string Spawn(string projectRoot, string? exePath, int timeoutMs)
        {
            string root = Path.GetFullPath(projectRoot);
            if (!Directory.Exists(root))
            {
                throw new DirectoryNotFoundException("项目根不存在: " + root);
            }
            string pipe = PipeNameFor(root);
            if (MauServeClient.Ping(pipe, 1000))
            {
                return pipe;
            }
            string? executable = exePath;
            if (string.IsNullOrWhiteSpace(executable))
            {
                executable = Environment.ProcessPath;
            }
            if (string.IsNullOrWhiteSpace(executable))
            {
                throw new InvalidOperationException("无法确定工作进程可执行文件路径。");
            }
            string pocketRoot = PocketRootFor(root);
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = executable;
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.ArgumentList.Add("serve-work");
            psi.ArgumentList.Add(root);
            psi.ArgumentList.Add(pipe);
            psi.ArgumentList.Add(pocketRoot);
            // ⑦ 单实例互斥豁免（D22/D31）——serve-work 是服务工作进程（管道通讯常驻），由父进程 spawn 协调，不参与指令互斥
            psi.EnvironmentVariables["MAU_INNER_CHILD"] = "1";
            Process? process = Process.Start(psi);
            if (process == null)
            {
                throw new InvalidOperationException("工作进程启动失败。");
            }
            process.Dispose();

            // 轮询等待就绪
            int waited = 0;
            while (waited < timeoutMs)
            {
                if (MauServeClient.Ping(pipe, 500))
                {
                    return pipe;
                }
                Thread.Sleep(200);
                waited = waited + 200;
            }
            throw new TimeoutException("工作进程 " + timeoutMs + " 毫秒内未就绪。");
        }

        /// <summary>
        /// 停止工作进程——通过管道发 stop，进程优雅退出
        /// </summary>
        /// <param name="projectRoot">项目源码根</param>
        /// <returns>是否发出停止指令</returns>
        public static bool Stop(string projectRoot)
        {
            string root = Path.GetFullPath(projectRoot);
            string pipe = PipeNameFor(root);
            return MauServeClient.Stop(pipe, 1000);
        }

        /// <summary>
        /// 查询服务状态
        /// </summary>
        /// <param name="projectRoot">项目源码根</param>
        /// <returns>是否在线</returns>
        public static bool IsRunning(string projectRoot)
        {
            string root = Path.GetFullPath(projectRoot);
            string pipe = PipeNameFor(root);
            return MauServeClient.Ping(pipe, 1000);
        }
    }
}
