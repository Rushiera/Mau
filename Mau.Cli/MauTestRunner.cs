using System;
using System.Diagnostics;
using System.IO;
using Mau.Translator;
using System.Security.Cryptography;
using System.Text;

namespace Mau.Cli
{
    /// <summary>
    /// 门禁执行器——纯 C# 实现的分层门禁（L2 翻译器测试 + L5 生成物真实运行 + L3 黄金文件对比）
    /// 纲领三：工具链全 C#——不用 ps1/py 脚本承载工程逻辑
    /// </summary>
    public static class MauTestRunner
    {
        /// <summary>
        /// 执行门禁——成功输出 MAU_CHECKS_OK
        /// </summary>
        /// <returns>退出码——0 成功</returns>
        public static int Run()
        {
            string? root = FindWorkspaceRoot();
            if (root == null)
            {
                Console.WriteLine("FAIL: 未找到 Mau.sln——请在 Mau workspace 下运行 mau test");
                return 1;
            }

            // [段1] 构建测试项目
            Console.WriteLine("[1/4] 构建测试项目");
            if (!RunProcess("dotnet", "build \"" + Path.Combine(root, "Mau.Translator.Tests", "Mau.Translator.Tests.csproj") + "\" -v q --nologo"))
            {
                Console.WriteLine("FAIL: 翻译器测试项目构建失败");
                return 1;
            }
            if (!RunProcess("dotnet", "build \"" + Path.Combine(root, "Mau.Runtime.Tests", "Mau.Runtime.Tests.csproj") + "\" -v q --nologo"))
            {
                Console.WriteLine("FAIL: 运行时测试项目构建失败");
                return 1;
            }

            // [段2] 翻译器测试（L2）
            Console.WriteLine("[2/4] 翻译器测试（L2）");
            string translatorExe = Path.Combine(root, "Mau.Translator.Tests", "bin", "Debug", "net8.0", "Mau.Translator.Tests.exe");
            if (!RunProcess(translatorExe, ""))
            {
                Console.WriteLine("FAIL: 翻译器测试失败");
                return 1;
            }

            // [段3] 生成物真实运行（L5）
            Console.WriteLine("[3/4] 生成物真实运行（L5）");
            string runtimeExe = Path.Combine(root, "Mau.Runtime.Tests", "bin", "Debug", "net8.0", "Mau.Runtime.Tests.exe");
            if (!RunProcess(runtimeExe, ""))
            {
                Console.WriteLine("FAIL: 运行时测试失败");
                return 1;
            }

            // [段4] 黄金文件对比（L3）——含 SHA256 校验尾验证
            Console.WriteLine("[4/4] 黄金文件对比（L3）");
            if (!VerifyGolden(root, "file_convert.mau", "FL_FileConvert.cs", "FileConvert"))
            {
                return 1;
            }
            if (!VerifyGolden(root, "worker_convert.mau", "FL_WorkerConvert.cs", "WorkerConvert"))
            {
                return 1;
            }
            if (!VerifyGolden(root, "sequence_flow.mau", "FL_SequenceFlow.cs", "SequenceFlow"))
            {
                return 1;
            }

            Console.WriteLine("MAU_CHECKS_OK");
            return 0;
        }

        /// <summary>
        /// 查找 workspace 根——含 Mau.sln 的目录；当前目录向上优先，程序集位置兜底
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

            DirectoryInfo? exeDir = new DirectoryInfo(AppContext.BaseDirectory);
            while (exeDir != null)
            {
                string sln = Path.Combine(exeDir.FullName, "Mau.sln");
                if (File.Exists(sln))
                {
                    return exeDir.FullName;
                }
                exeDir = exeDir.Parent;
            }
            return null;
        }

        /// <summary>
        /// 运行子进程并检查退出码——输出直接继承控制台
        /// </summary>
        /// <param name="fileName">可执行文件</param>
        /// <param name="arguments">参数</param>
        /// <returns>退出码为 0</returns>
        private static bool RunProcess(string fileName, string arguments)
        {
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = fileName;
            psi.Arguments = arguments;
            psi.UseShellExecute = false;
            Process? p;
            try
            {
                p = Process.Start(psi);
            }
            catch
            {
                Console.WriteLine("FAIL: 无法启动进程——" + fileName);
                return false;
            }
            if (p == null)
            {
                Console.WriteLine("FAIL: 进程启动失败——" + fileName);
                return false;
            }
            p.WaitForExit();
            return p.ExitCode == 0;
        }
/// <summary>
/// 计算字符串的 SHA256 哈希——UTF-8 字节转 64 位十六进制大写
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
/// 验证黄金文件校验尾——读文件，提取末行 MAU_CHECKSUM，验证 SHA256，返回去掉校验行的文件体
/// </summary>
/// <param name = "filePath">黄金文件路径</param>
/// <param name = "body">去掉校验行的文件体</param>
/// <returns>校验通过</returns>
private static bool VerifyChecksum(string filePath, out string body)
{
        body = "";
        string full;
        try
        {
            full = File.ReadAllText(filePath).Replace("\r\n", "\n");
        }
        catch
        {
            Console.WriteLine("FAIL: 黄金文件不可读——" + filePath);
            return false;
        }

        string[] lines = full.Split('\n');
        if (lines.Length == 0)
        {
            Console.WriteLine("FAIL: 黄金文件为空——" + filePath);
            return false;
        }

        // 从后往前找第一个非空行作为校验尾——容忍末尾多余换行
        string prefix = "// #MAU_CHECKSUM:SHA256:";
        int checksumIdx = -1;
        for (int i = lines.Length - 1; i >= 0; i--)
        {
            string trimmed = lines[i].Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }
            if (trimmed.StartsWith(prefix))
            {
                checksumIdx = i;
            }
            break;
        }

        if (checksumIdx < 0)
        {
            Console.WriteLine("FAIL: 黄金文件缺少校验尾——" + filePath);
            return false;
        }

        string lastLine = lines[checksumIdx].Trim();
        string claimedHash = lastLine.Substring(prefix.Length).Trim();
        if (claimedHash.Length != 64)
        {
            Console.WriteLine("FAIL: 校验尾哈希长度异常——" + filePath);
            return false;
        }

        // 去掉校验行及之后的所有行——重新拼接
        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < checksumIdx; i++)
        {
            if (i > 0)
            {
                sb.Append('\n');
            }
            sb.Append(lines[i]);
        }
        body = sb.ToString();

        // 计算 SHA256
        string computedHash = ComputeSha256(body);
        if (!string.Equals(computedHash, claimedHash, StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("FAIL: 黄金文件校验失败——" + filePath);
            Console.WriteLine("  期望: " + claimedHash);
            Console.WriteLine("  实际: " + computedHash);
            return false;
        }

        return true;
    }/// <summary>
/// 单份黄金文件验证——编译 Mau 源 → 黄金文件校验尾验证 → 逐字节对比
/// </summary>
/// <param name = "root">workspace 根</param>
/// <param name = "caseName">cases/ 下的 .mau 文件名</param>
/// <param name = "expectedName">expected/ 下的黄金文件名</param>
/// <param name = "flowName">流程名——PascalCase</param>
/// <returns>通过</returns>
private static bool VerifyGolden(string root, string caseName, string expectedName, string flowName)
{
    string caseFile = Path.Combine(root, "Mau.Snapshots", "cases", caseName);
    string expectedFile = Path.Combine(root, "Mau.Snapshots", "expected", expectedName);
    string source;
    try
    {
        source = File.ReadAllText(caseFile);
    }
    catch
    {
        Console.WriteLine("FAIL: 黄金文件源缺失——" + caseFile);
        return false;
    }

    CompileResult result = MauCompiler.Compile(source, flowName);
    if (!result.Success)
    {
        Console.WriteLine("FAIL: 黄金文件源编译失败——" + caseName);
        return false;
    }

    if (!VerifyChecksum(expectedFile, out string expectedBody))
    {
        return false;
    }

    string actual = result.GeneratedCode.Replace("\r\n", "\n").TrimEnd('\n');
    if (expectedBody.TrimEnd('\n') != actual)
    {
        Console.WriteLine("FAIL: 生成漂移——黄金文件不一致 (" + caseName + ")");
        return false;
    }

    return true;
}}
}
