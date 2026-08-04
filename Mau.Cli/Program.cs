using System;
using System.IO;
using System.Text;
using Mau.Translator;
using System.Security.Cryptography;

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
            Mau.Bricks.MathBrickRegistration.RegisterAll();
            Mau.Bricks.DataBrickRegistration.RegisterAll();
            Mau.Bricks.BoxBrickRegistration.RegisterAll();
            Mau.Bricks.TextBrickRegistration.RegisterAll();
            Mau.Bricks.ShellBrickRegistration.RegisterAll();
            Mau.Bricks.LlmBrickRegistration.RegisterAll();
            Mau.Bricks.ContextBrickRegistration.RegisterAll();
            Mau.Bricks.ApprovalBrickRegistration.RegisterAll();
            Mau.Bricks.ExcelBrickRegistration.RegisterAll();
            Mau.Bricks.DocxBrickRegistration.RegisterAll();
            Mau.Bricks.LogBrickRegistration.RegisterAll();

            // [段2] 命令路由
            if (args.Length == 0)
            {
                Console.WriteLine("Mau Translator v0.1");
                Console.WriteLine("用法: mau verify <file.mau> | mau gen <file.mau> -o <dir> | mau build <file.mau> -o <dir> | mau test | mau checksum --update | mau run <file.mau> [--fire Method key=val ...] [--ticks N] | mau serve <file.mau> [--port N]");
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
            if (command == "checksum")
            {
                return CommandChecksum(args);
            }
            if (command == "run")
            {
                string[] runArgs = new string[args.Length - 1];
                for (int i = 0; i < runArgs.Length; i = i + 1)
                {
                    runArgs[i] = args[i + 1];
                }
                return CommandRun.Execute(runArgs);
            }
            if (command == "serve")
            {
                string[] serveArgs = new string[args.Length - 1];
                for (int i = 0; i < serveArgs.Length; i = i + 1)
                {
                    serveArgs[i] = args[i + 1];
                }
                return CommandServe.Execute(serveArgs);
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
/// <summary>
/// 计算 SHA256 哈希——UTF-8 字节转 64 位十六进制大写
/// </summary>
/// <param name = "text">输入文本</param>
/// <returns>64 位十六进制哈希（大写）</returns>
private static string ComputeSha256(string text)
{
    byte[] bytes = Encoding.UTF8.GetBytes(text);
    byte[] hash = SHA256.HashData(bytes);
    StringBuilder hex = new StringBuilder();
    for (int i = 0; i < hash.Length; i++)
    {
        hex.Append(hash[i].ToString("X2"));
    }

    return hex.ToString();
}    /// <summary>
/// 查找 workspace 根——含 Mau.sln 的目录
/// </summary>
/// <returns>workspace 根或空</returns>
private static string? FindWorkspaceRoot()
{
    DirectoryInfo? dir = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (dir != null)
    {
        string sln = Path.Combine(dir.FullName, "Mau.sln");
        if (File.Exists(sln))
        {
            return dir.FullName;
        }

        dir = dir.Parent;
    }

    return null;
}/// <summary>
/// checksum 命令——更新黄金文件 SHA256 校验尾
/// </summary>
/// <param name = "args">命令行参数：checksum --update</param>
/// <returns>退出码</returns>
private static int CommandChecksum(string[] args)
{
    if (args.Length < 2 || args[1] != "--update")
    {
        Console.WriteLine("用法: mau checksum --update");
        Console.WriteLine("  遍历 Mau.Snapshots/expected/*.cs，计算 SHA256 并更新校验尾");
        return 1;
    }

    string? root = FindWorkspaceRoot();
    if (root == null)
    {
        Console.WriteLine("FAIL: 未找到 Mau.sln");
        return 1;
    }

    string expectedDir = Path.Combine(root, "Mau.Snapshots", "expected");
    if (!Directory.Exists(expectedDir))
    {
        Console.WriteLine("FAIL: 目录不存在——" + expectedDir);
        return 1;
    }

    string[] files = Directory.GetFiles(expectedDir, "*.cs");
    if (files.Length == 0)
    {
        Console.WriteLine("无黄金文件——" + expectedDir);
        return 0;
    }

    string checksumPrefix = "// #MAU_CHECKSUM:SHA256:";
    int updated = 0;
    for (int i = 0; i < files.Length; i++)
    {
        string file = files[i];
        string content;
        try
        {
            content = File.ReadAllText(file).Replace("\r\n", "\n");
        }
        catch
        {
            Console.WriteLine("SKIP: 不可读——" + Path.GetFileName(file));
            continue;
        }

        // 去掉末尾已有校验尾（可能多行 + 空行）
        string[] lines = content.Split('\n');
        int bodyEnd = lines.Length;
        while (bodyEnd > 0)
        {
            string last = lines[bodyEnd - 1].Trim();
            if (last.Length == 0 || last.StartsWith(checksumPrefix))
            {
                bodyEnd = bodyEnd - 1;
            }
            else
            {
                break;
            }
        }
        if (bodyEnd < lines.Length)
        {
            StringBuilder sb = new StringBuilder();
            for (int j = 0; j < bodyEnd; j++)
            {
                if (j > 0)
                {
                    sb.Append('\n');
                }
                sb.Append(lines[j]);
            }
            content = sb.ToString();
        }

        string hash = ComputeSha256(content);
        string newContent = content + "\n" + checksumPrefix + hash;
        File.WriteAllText(file, newContent);
        Console.WriteLine("UPDATED: " + Path.GetFileName(file) + " → " + hash);
        updated = updated + 1;
    }

    Console.WriteLine("完成: " + updated + " 份黄金文件校验尾已更新");
    return 0;
}}
}
