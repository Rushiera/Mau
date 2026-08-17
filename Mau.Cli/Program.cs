using System;
using System.IO;
using Mau.Translator;

namespace Mau.Cli
{
    /// <summary>
    /// Mau 编译器入口（v3 最小命令面）——verify / gen / build / test / check。
    /// 时序报告：每个指令自报起始/完成/耗时/退出码（工具主动观测——design-mau-cli §三 原则保留）。
    /// 命令面重审（Supervisor/组工程/debug/bricks）归 P6 工具链收拢轮。
    /// </summary>
    public static class Program
    {
        /// <summary>
        /// 入口——时序包装 + 命令路由
        /// </summary>
        /// <param name="args">命令行参数</param>
        /// <returns>退出码——0 成功，非 0 失败</returns>
        public static int Main(string[] args)
        {
            // 控制台 UTF-8 输出——中文诊断在 GBK 控制台下不乱码
            try
            {
                Console.OutputEncoding = System.Text.Encoding.UTF8;
            }
            catch (Exception)
            {
                // 输出编码设置失败不影响功能
            }
            DateTime start = DateTime.Now;
            string commandName = args.Length > 0 ? args[0] : "(help)";
            Console.WriteLine("[mau " + commandName + "] 起始 " + start.ToString("HH:mm:ss.fff"));
            int exitCode = Dispatch(args);
            DateTime end = DateTime.Now;
            long elapsedMs = (long)(end - start).TotalMilliseconds;
            Console.WriteLine("[mau " + commandName + "] 完成 " + end.ToString("HH:mm:ss.fff") + " | " + elapsedMs + "ms | exit " + exitCode);
            return exitCode;
        }

        /// <summary>
        /// 命令路由——公开（聚合器进程内复用形态保留，聚合器 P6 重立）
        /// </summary>
        /// <param name="args">命令行参数</param>
        /// <returns>退出码</returns>
        public static int Dispatch(string[] args)
{
    CliSupport.ParseVerbose(args);
    if (args.Length == 0)
    {
        PrintHelp();
        return 0;
    }
    string command = args[0];
    int commandId;
    if (!CommandIds.TryResolve(command, out commandId))
    {
        Console.WriteLine("未知命令: " + command);
        PrintHelp();
        return 1;
    }
    switch (commandId)
    {
        case CommandIds.Verify:
            return CommandVerify(args);
        case CommandIds.Gen:
            return CommandGen(args);
        case CommandIds.Build:
            return CommandBuild(args);
        case CommandIds.Test:
        {
            bool update = args.Length > 1 && args[1] == "--update";
            return MauTestRunner.Run(update);
        }
        case CommandIds.Check:
            return CommandCheckV3.Run();
        case CommandIds.Debug:
            return CommandDebugV3.Run(CliSupport.Tail(args));
        case CommandIds.Bricks:
            return CommandBricksV3.Run(CliSupport.Tail(args));
        case CommandIds.Proj:
            return CommandProjV3.Run(CliSupport.Tail(args));
        default:
            Console.WriteLine("未知指令编号: " + commandId);
            PrintHelp();
            return 1;
    }
}
        /// <summary>
        /// 帮助——最小命令面
        /// </summary>
        private static void PrintHelp()
        {
            Console.WriteLine("Mau Translator v3.0");
            Console.WriteLine("构筑:   mau verify <file.mau> | mau gen <file.mau> -o <dir> | mau build <file.mau> -o <dir>");
            Console.WriteLine("验证:   mau test [--update] | mau check [--verbose]");
            Console.WriteLine("调试:   mau debug <file.mau> [--ticks N] [--step] [--pause-on S_X=Y]");
        }

        /// <summary>
        /// verify 命令——全链编译（词法/解析/验证/分析），不产出
        /// </summary>
        /// <param name="args">命令行参数</param>
        /// <returns>退出码</returns>
        private static int CommandVerify(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("用法: mau verify <file.mau>");
                return 1;
            }
            string path = args[1];
            if (!File.Exists(path))
            {
                Console.WriteLine("文件不存在: " + path);
                return 1;
            }
            string source = File.ReadAllText(path);
            string flowName = FlowNameFromPath(path);
            CompileResultV3 result = MauCompilerV3.Compile(source, flowName);
            PrintDiagnostics(path, result);
            if (result.Success)
            {
                Console.WriteLine("验证通过: " + flowName);
                return 0;
            }
            return 1;
        }

        /// <summary>
        /// gen 命令——全链编译 + 输出 C# 生成物
        /// </summary>
        /// <param name="args">命令行参数</param>
        /// <returns>退出码</returns>
        private static int CommandGen(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("用法: mau gen <file.mau> -o <dir>");
                return 1;
            }
            string path = args[1];
            if (!File.Exists(path))
            {
                Console.WriteLine("文件不存在: " + path);
                return 1;
            }
            string outDir = ".";
            for (int i = 2; i < args.Length - 1; i++)
            {
                if (args[i] == "-o")
                {
                    outDir = args[i + 1];
                }
            }
            if (!Directory.Exists(outDir))
            {
                Directory.CreateDirectory(outDir);
            }
            string source = File.ReadAllText(path);
            string flowName = FlowNameFromPath(path);
            CompileResultV3 result = MauCompilerV3.Compile(source, flowName);
            PrintDiagnostics(path, result);
            if (!result.Success)
            {
                return 1;
            }
            string outFile = Path.Combine(outDir, "FL_" + flowName + ".cs");
            File.WriteAllText(outFile, result.GeneratedCode, System.Text.Encoding.UTF8);
            Console.WriteLine("生成: " + outFile);
            return 0;
        }

        /// <summary>
        /// build 命令——全链编译 + Roslyn Emit（PocketCompiler，无 SDK）
        /// </summary>
        /// <param name="args">命令行参数</param>
        /// <returns>退出码</returns>
        private static int CommandBuild(string[] args)
        {
            string? mauFile = null;
            string outDir = "";
            for (int i = 1; i < args.Length; i = i + 1)
            {
                if (args[i] == "-o" && i + 1 < args.Length)
                {
                    outDir = args[i + 1];
                    i = i + 1;
                }
                else if (mauFile == null)
                {
                    mauFile = args[i];
                }
            }
            if (mauFile == null)
            {
                Console.WriteLine("用法: mau build <组.mauproj> [--build] | mau proj <组.mauproj> [-o <srcDir>] [--build]");
                return 1;
            }
            if (!File.Exists(mauFile))
            {
                Console.WriteLine("文件不存在: " + mauFile);
                return 1;
            }
            // [段0] 统一构筑链路由——.mauproj 走组路径（Roslyn Emit 退役——design-ch4-deploy §六）；.mau 单文件提示建组
            if (mauFile.EndsWith(".mauproj", StringComparison.OrdinalIgnoreCase))
            {
                string[] projArgs;
                if (outDir.Length > 0)
                {
                    projArgs = new string[] { mauFile, "--build", "-o", outDir };
                }
                else
                {
                    projArgs = new string[] { mauFile, "--build" };
                }
                return CommandProjV3.Run(projArgs);
            }
            Console.WriteLine("提示: 单 .mau 直接编译路径已退役（统一构筑链——design-ch4-deploy.md）。");
            Console.WriteLine("      请为该语料创建 mauproj 组声明，然后: mau proj <组.mauproj> --build");
            Console.WriteLine("      中间产物将落盘 public/src/<组名>/，dll 输出 public/app/Flows/FL_<组名>.dll");
            return 1;
        }

        /// <summary>
        /// 输出诊断——统一格式 文件:行号: 错误码: 消息
        /// </summary>
        /// <param name="path">源文件路径</param>
        /// <param name="result">编译结果</param>
        private static void PrintDiagnostics(string path, CompileResultV3 result)
        {
            for (int i = 0; i < result.Diagnostics.Count; i++)
            {
                MauDiagnostic d = result.Diagnostics[i];
                Console.WriteLine(path + ":" + d.Line + ": " + d.Code + ": " + d.Message);
            }
            for (int i = 0; i < result.Reports.Count; i++)
            {
                Console.WriteLine("  " + result.Reports[i]);
            }
        }

        /// <summary>
        /// 从文件路径推导流程名——talk.mau → Talk
        /// </summary>
        /// <param name="path">文件路径</param>
        /// <returns>PascalCase 流程名</returns>
        public static string FlowNameFromPath(string path)
        {
            string baseName = Path.GetFileNameWithoutExtension(path);
            string[] parts = baseName.Split('_');
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].Length == 0)
                {
                    continue;
                }
                string head = parts[i].Substring(0, 1).ToUpperInvariant();
                string tail = parts[i].Length > 1 ? parts[i].Substring(1) : "";
                sb.Append(head);
                sb.Append(tail);
            }
            return sb.ToString();
        }
    }
}
