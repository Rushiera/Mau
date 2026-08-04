using System;
using System.IO;
using System.Text;
using Mau.Translator;

namespace Mau.Cli
{
    /// <summary>
    /// Mau 编译器入口——独立编译进程
    /// </summary>
    public static class Program
    {
        /// <summary>
        /// 入口
        /// </summary>
        /// <param name="args">命令行参数</param>
        /// <returns>退出码——0 成功，非 0 失败</returns>
        public static int Main(string[] args)
        {
            // [段1] 注册标准积木——编译期注册表
            Mau.Bricks.StandardBrickRegistration.RegisterAll();

            // [段2] 命令路由
            if (args.Length == 0)
            {
                Console.WriteLine("Mau Translator v0.1");
                Console.WriteLine("用法: mau verify <file.mau> | mau gen <file.mau> -o <dir> | mau build <file.mau> -o <dir> | mau test");
                return 0;
            }

            string command = args[0];
            if (command == "verify")
            {
                return CommandVerify(args);
            }
            if (command == "gen")
            {
                return CommandGen(args);
            }
            if (command == "build")
            {
                return CommandBuild(args);
            }
            if (command == "test")
            {
                return MauTestRunner.Run();
            }

            Console.WriteLine("未知命令: " + command);
            return 1;
        }

        /// <summary>
        /// verify 命令——只静态验证
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
            CompileResult result = MauCompiler.Compile(source, flowName);
            PrintDiagnostics(path, result.Diagnostics);

            if (result.Success)
            {
                Console.WriteLine("验证通过: " + flowName);
                return 0;
            }
            return 1;
        }

        /// <summary>
        /// gen 命令——只生成 C# 源码
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
            CompileResult result = MauCompiler.Compile(source, flowName);
            PrintDiagnostics(path, result.Diagnostics);
            if (!result.Success)
            {
                return 1;
            }

            string outFile = Path.Combine(outDir, "FL_" + flowName + ".cs");
            File.WriteAllText(outFile, result.GeneratedCode);
            Console.WriteLine("生成: " + outFile);
            return 0;
        }

        /// <summary>
        /// build 命令——验证 + 生成 + 编译（生成后调环境 dotnet build）
        /// </summary>
        /// <param name="args">命令行参数</param>
        /// <returns>退出码</returns>
        private static int CommandBuild(string[] args)
        {
            Console.WriteLine("build 待实现——第一期先走 gen + mau test 门禁");
            return 1;
        }

        /// <summary>
        /// 输出诊断——统一格式 文件:行号: 错误码: 消息
        /// </summary>
        /// <param name="path">源文件路径</param>
        /// <param name="diags">诊断列表</param>
        private static void PrintDiagnostics(string path, System.Collections.Generic.List<MauDiagnostic> diags)
        {
            for (int i = 0; i < diags.Count; i++)
            {
                MauDiagnostic d = diags[i];
                Console.WriteLine(path + ":" + d.Line + ": " + d.Code + ": " + d.Message);
            }
        }

        /// <summary>
        /// 从文件路径推导流程名——file_convert.mau → FileConvert
        /// </summary>
        /// <param name="path">文件路径</param>
        /// <returns>PascalCase 流程名</returns>
        private static string FlowNameFromPath(string path)
        {
            string baseName = Path.GetFileNameWithoutExtension(path);
            string[] parts = baseName.Split('_');
            StringBuilder sb = new StringBuilder();
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
