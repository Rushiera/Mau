using System;
using System.IO;
using System.Text;
using Mau.Translator;
using Mau.Development;
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
            Mau.Bricks.OaBrickRegistration.RegisterAll();
            Mau.Bricks.ToolBrickRegistration.RegisterAll();

            // [段2] 命令路由
            if (args.Length == 0)
            {
                Console.WriteLine("Mau Translator v0.1");
                Console.WriteLine("用法: mau verify <file.mau> | mau gen <file.mau> -o <dir> | mau build <file.mau> -o <dir> [--sdk] | mau publish -o <dir> | mau test | mau checksum --update | mau run <file.mau> [--fire Method key=val ...] [--ticks N] [--sdk] | mau serve <file.mau> [--port N] | mau serve spawn|stop|status|call ... | mau serve-work <项目> <管道> <pocket>");
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
            if (command == "publish")
            {
                string[] publishArgs = new string[args.Length - 1];
                for (int i = 0; i < publishArgs.Length; i = i + 1)
                {
                    publishArgs[i] = args[i + 1];
                }
                return CommandPublish.Execute(publishArgs);
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
                if (serveArgs.Length > 0 && IsServeSubCommand(serveArgs[0]))
                {
                    return CommandServeManager.Execute(serveArgs);
                }
                return CommandServe.Execute(serveArgs);
            }
            if (command == "serve-work")
            {
                string[] workArgs = new string[args.Length - 1];
                for (int i = 0; i < workArgs.Length; i = i + 1)
                {
                    workArgs[i] = args[i + 1];
                }
                return CommandServeWork.Execute(workArgs);
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
        /// build 命令——验证 + 生成 + 编译（默认 Roslyn Emit 无 SDK；--sdk 走环境 dotnet build）
        /// </summary>
        /// <param name="args">命令行参数</param>
        /// <returns>退出码</returns>
        private static int CommandBuild(string[] args)
        {
            string? mauFile = null;
            string outDir = ".";
            bool useSdk = false;

            for (int i = 1; i < args.Length; i = i + 1)
            {
                if (args[i] == "-o" && i + 1 < args.Length)
                {
                    outDir = args[i + 1];
                    i = i + 1;
                }
                else if (args[i] == "--sdk")
                {
                    useSdk = true;
                }
                else if (mauFile == null)
                {
                    mauFile = args[i];
                }
            }
            if (mauFile == null)
            {
                Console.WriteLine("用法: mau build <file.mau> -o <dir> [--sdk]");
                Console.WriteLine("  默认: Roslyn 内存编译（无需 .NET SDK）");
                Console.WriteLine("  --sdk: 走环境 dotnet build（开发调试用）");
                return 1;
            }
            if (!File.Exists(mauFile))
            {
                Console.WriteLine("文件不存在: " + mauFile);
                return 1;
            }

            // [1] 解析 + 验证
            string source = File.ReadAllText(mauFile);
            string flowName = FlowNameFromPath(mauFile);
            CompileResult result = MauCompiler.Compile(source, flowName);
            PrintDiagnostics(mauFile, result.Diagnostics);
            if (!result.Success)
            {
                Console.WriteLine("构建失败: 验证未通过——" + flowName);
                return 1;
            }

            // [2] 输出目录
            if (!Directory.Exists(outDir))
            {
                Directory.CreateDirectory(outDir);
            }
            string className = "FL_" + flowName;
            string dllPath = Path.Combine(outDir, className + ".dll");

            // [3] 编译
            if (useSdk)
            {
                string tempDir = Path.Combine(Path.GetTempPath(), "mau_build_sdk_" + Guid.NewGuid().ToString("N").Substring(0, 8));
                TempProjectBuilder.BuildResult buildResult = TempProjectBuilder.Build(result.GeneratedCode, className, tempDir);
                if (!buildResult.Success)
                {
                    Console.WriteLine("构建失败: C# 编译错误");
                    if (buildResult.BuildOutput != null)
                    {
                        Console.WriteLine(buildResult.BuildOutput);
                    }
                    return 2;
                }
                File.Copy(buildResult.DllPath!, dllPath, true);
                try
                {
                    if (Directory.Exists(tempDir))
                    {
                        Directory.Delete(tempDir, true);
                    }
                }
                catch
                {
                    // 临时目录清理失败不影响结果
                }
            }
            else
            {
                string pocketRoot = Path.Combine(Path.GetTempPath(), "mau_build_pocket_" + Guid.NewGuid().ToString("N").Substring(0, 8));
                MauPocketCompiler compiler = new MauPocketCompiler(pocketRoot);
                MauPocketCompileResult compileResult = compiler.Compile(result.GeneratedCode, className);
                if (!compileResult.Success)
                {
                    Console.WriteLine("构建失败: Roslyn 编译错误");
                    for (int i = 0; i < compileResult.Diagnostics.Length; i = i + 1)
                    {
                        Console.WriteLine("  " + compileResult.Diagnostics[i]);
                    }
                    return 2;
                }
                File.Copy(compileResult.AssemblyPath, dllPath, true);
                try
                {
                    if (Directory.Exists(pocketRoot))
                    {
                        Directory.Delete(pocketRoot, true);
                    }
                }
                catch
                {
                    // 临时目录清理失败不影响结果
                }
            }

            Console.WriteLine("构建成功: " + dllPath + "（" + (useSdk ? "dotnet build" : "Roslyn Emit") + "）");
            return 0;
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
}

    /// <summary>
    /// 判断 serve 子命令——spawn/stop/status/call 走服务管理，其余走 HTTP 面板
    /// </summary>
    /// <param name="first">第一个参数</param>
    /// <returns>是否服务管理子命令</returns>
    private static bool IsServeSubCommand(string first)
    {
        return first == "spawn" || first == "stop" || first == "status" || first == "call";
    }
}
}
