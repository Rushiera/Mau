using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace SetUp
{
    /// <summary>
    /// SetUp 启动器——CH4/Mau 一键部署工具。
    /// 定位：单文件 exe 跟随 Git 放仓库根，clone 即用；双击即部署即运行。
    /// 规范：Project/CH4/design-ch4-release.md（D2 定稿）
    /// 双模式：prepare（重建全发布链） / deploy &lt;目标目录&gt;（产出正式运行实例）
    /// 前置检测：.NET 8 Runtime + SDK + 同目录存在 Mau.sln 才运行。
    /// </summary>
    public static partial class Program
    {
        /// <summary>
        /// 主入口——模式分发。
        /// </summary>
        /// <param name="args">命令行参数：无参=帮助 / prepare / deploy &lt;目标目录&gt;</param>
        /// <returns>退出码（0=成功，非0=失败）</returns>
        public static int Main(string[] args)
        {
            // [段1] 帮助提示——无参或 -h/--help
            if (args.Length == 0 || args[0] == "-h" || args[0] == "--help")
            {
                PrintHelp();
                return 0;
            }

            // [段2] 仓库根检测——当前目录存在 Mau.sln 才继续（SetUp.exe 合法位置 = Mau 仓库根）
            string repoRoot = ResolveRepoRoot(Environment.CurrentDirectory);
            if (repoRoot.Length == 0)
            {
                Console.WriteLine("[SetUp] 错误：当前目录未找到 Mau.sln——SetUp.exe 必须位于 Mau 仓库根目录运行。");
                Pause();
                return 1;
            }
            Console.WriteLine("[SetUp] 仓库根: " + repoRoot);

            // [段3] 环境检测——.NET 8 Runtime + SDK
            if (!CheckEnvironment())
            {
                Console.WriteLine("[SetUp] 环境检测未通过，部署中止。");
                Pause();
                return 1;
            }

            // [段4] 模式分发
            string mode = args[0];
            if (mode == "prepare")
            {
                return Prepare(repoRoot);
            }
            if (mode == "deploy")
            {
                if (args.Length < 2)
                {
                    Console.WriteLine("[SetUp] deploy 需要目标目录参数：SetUp.exe deploy <目标目录>");
                    return 1;
                }
                return Deploy(repoRoot, args[1]);
            }

            Console.WriteLine("[SetUp] 未知模式: " + mode);
            PrintHelp();
            return 1;
        }

        /// <summary>
        /// 帮助文本输出。
        /// </summary>
        private static void PrintHelp()
        {
            Console.WriteLine("SetUp —— CH4/Mau 一键部署工具");
            Console.WriteLine("用法:");
            Console.WriteLine("  SetUp.exe            显示本帮助");
            Console.WriteLine("  SetUp.exe prepare    重建全发布链（public/ + Mau-public/）");
            Console.WriteLine("  SetUp.exe deploy <目录>  部署正式运行实例到目标目录");
            Console.WriteLine("前置：.NET 8 Runtime + SDK；本 exe 须位于 Mau 仓库根（含 Mau.sln）。");
        }

        /// <summary>
        /// 仓库根探测——从起始目录向上找含 Mau.sln 的目录。
        /// </summary>
        /// <param name="startDir">起始目录</param>
        /// <returns>仓库根或空字符串</returns>
        private static string ResolveRepoRoot(string startDir)
        {
            string dir = new DirectoryInfo(startDir).FullName;
            while (true)
            {
                if (File.Exists(Path.Combine(dir, "Mau.sln")))
                {
                    return dir;
                }
                string parent = Directory.GetParent(dir)?.FullName;
                if (parent == null)
                {
                    return "";
                }
                dir = parent;
            }
        }

        /// <summary>
        /// 环境检测——.NET 8 Runtime（自启前提）+ SDK（部署前置）。
        /// 检测命令：dotnet --list-runtimes / --list-sdks。
        /// </summary>
        /// <returns>true=环境就绪，false=缺 runtime 或 SDK</returns>
        private static bool CheckEnvironment()
        {
            // [段1] dotnet 本体检测——找不到 dotnet = runtime 缺失
            string dotnetOut = RunProcessCapture("dotnet", "--list-runtimes", Environment.CurrentDirectory);
            if (dotnetOut == null)
            {
                Console.WriteLine("[SetUp] 错误：未检测到 dotnet 命令——需要 .NET 8 Runtime。");
                Console.WriteLine("  安装指引: https://dotnet.microsoft.com/download/dotnet/8.0");
                return false;
            }

            // [段2] Runtime 检测——找 Microsoft.NETCore.App 主版本 ≥ 8（SDK 9 可构建 net8.0——向后兼容，≥8 即通过）
            bool runtime8 = false;
            string[] runtimeLines = dotnetOut.Split('\n');
            for (int i = 0; i < runtimeLines.Length; i = i + 1)
            {
                string line = runtimeLines[i].Trim();
                if (line.StartsWith("Microsoft.NETCore.App", StringComparison.Ordinal))
                {
                    if (VersionAtLeast8(line))
                    {
                        runtime8 = true;
                    }
                }
            }
            if (!runtime8)
            {
                Console.WriteLine("[SetUp] 错误：需要 .NET 8 及以上 Runtime（当前未装）。");
                Console.WriteLine("  安装指引: https://dotnet.microsoft.com/download/dotnet/8.0");
                return false;
            }

            // [段3] SDK 检测——dotnet --list-sdks 找主版本 ≥ 8
            string sdkOut = RunProcessCapture("dotnet", "--list-sdks", Environment.CurrentDirectory);
            bool sdk8 = false;
            if (sdkOut != null)
            {
                string[] sdkLines = sdkOut.Split('\n');
                for (int i = 0; i < sdkLines.Length; i = i + 1)
                {
                    if (VersionAtLeast8(sdkLines[i]))
                    {
                        sdk8 = true;
                    }
                }
            }
            if (!sdk8)
            {
                Console.WriteLine("[SetUp] 错误：需要 .NET 8 及以上 SDK（部署全程 build/test/publish 前置）。");
                Console.WriteLine("  安装指引: https://dotnet.microsoft.com/download/dotnet/8.0");
                return false;
            }

            Console.WriteLine("[SetUp] 环境就绪：.NET 8 Runtime + SDK 已检测到。");
            return true;
        }

        /// <summary>
        /// 子进程执行——统一 Process.Start 封装（工作目录 + 退出码检查；输出继承控制台）。
        /// </summary>
        /// <param name="fileName">可执行文件</param>
        /// <param name="arguments">参数串</param>
        /// <param name="workingDir">工作目录</param>
        /// <returns>true=退出码 0</returns>
        private static bool RunProcess(string fileName, string arguments, string workingDir)
        {
            Console.WriteLine("[SetUp] >>> " + fileName + " " + arguments);
            ProcessStartInfo psi = new ProcessStartInfo(fileName, arguments);
            psi.WorkingDirectory = workingDir;
            psi.UseShellExecute = false;
            Process proc;
            try
            {
                proc = Process.Start(psi);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[SetUp] 错误：进程启动失败——" + fileName + "：" + ex.Message);
                return false;
            }
            if (proc == null)
            {
                Console.WriteLine("[SetUp] 错误：进程启动失败——" + fileName);
                return false;
            }
            proc.WaitForExit();
            if (proc.ExitCode != 0)
            {
                Console.WriteLine("[SetUp] 错误：退出码 " + proc.ExitCode + "——" + fileName + " " + arguments);
                return false;
            }
            return true;
        }

        /// <summary>
        /// 子进程执行并捕获标准输出——检测命令用（不继承控制台）。
        /// </summary>
        /// <param name="fileName">可执行文件</param>
        /// <param name="arguments">参数串</param>
        /// <param name="workingDir">工作目录</param>
        /// <returns>stdout 全文；启动失败返回 null</returns>
        private static string RunProcessCapture(string fileName, string arguments, string workingDir)
        {
            ProcessStartInfo psi = new ProcessStartInfo(fileName, arguments);
            psi.WorkingDirectory = workingDir;
            psi.UseShellExecute = false;
            psi.RedirectStandardOutput = true;
            psi.StandardOutputEncoding = Encoding.UTF8;
            Process proc;
            try
            {
                proc = Process.Start(psi);
            }
            catch (Exception)
            {
                return null;
            }
            if (proc == null)
            {
                return null;
            }
            string output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit();
            return output;
        }

        /// <summary>
        /// 版本行解析——提取首段版本号并判断主版本 ≥ 8（兼容 .NET 9/10——向后构建 net8.0）。
        /// 输入示例：Microsoft.NETCore.App 8.0.15 [...] 或 9.0.314 [...]
        /// </summary>
        /// <param name="line">dotnet 输出行</param>
        /// <returns>true=主版本 ≥ 8</returns>
        private static bool VersionAtLeast8(string line)
        {
            // [段1] 找第一个数字开头段（版本号）
            string[] tokens = line.Split(' ');
            for (int i = 0; i < tokens.Length; i = i + 1)
            {
                string token = tokens[i].Trim();
                if (token.Length == 0)
                {
                    continue;
                }
                int major;
                int dot = token.IndexOf('.');
                string majorStr = dot > 0 ? token.Substring(0, dot) : token;
                if (int.TryParse(majorStr, out major))
                {
                    return major >= 8;
                }
            }
            return false;
        }

        /// <summary>
        /// 失败暂停——保留窗口（双击运行场景看得到错误）。
        /// 自动化场景（stdout 被重定向）不等待——防 shell 无按键输入无限阻塞。
        /// </summary>
        private static void Pause()
        {
            if (Console.IsOutputRedirected)
            {
                return;
            }
            Console.WriteLine("按任意键退出…");
            Console.ReadKey(true);
        }
    }
}
