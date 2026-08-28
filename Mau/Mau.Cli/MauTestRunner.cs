using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Mau.Translator;

namespace Mau.Cli
{
    /// <summary>
    /// mau test（v3 最小门禁）——[1] 翻译器测试 + [2] 语法谱黄金哈希（TokenId 流哨兵）。
    /// 完整分层门禁（L4 积木谱/L6 E2E 等）随 P5 积木重生 + P6 工具链收拢重建。
    /// 黄金哨兵：golden-sha-v3.txt 每行 "文件名 哈希"——哈希对象 = TokenId 流（换外观零漂移，语义变更漂移）。
    /// </summary>
    public static class MauTestRunner
    {
        /// <summary>
        /// 执行门禁——成功输出 MAU_CHECKS_OK
        /// </summary>
        /// <param name="update">true=重建黄金哈希清单</param>
        /// <returns>退出码——0 成功</returns>
        public static int Run(bool update)
        {
            string? root = CliSupport.FindWorkspaceRoot();
            if (root == null)
            {
                Console.WriteLine("FAIL: 未找到 Mau.sln——请在 Mau workspace 下运行 mau test");
                return 1;
            }
            // [段1] 翻译器测试 + 运行时机制测试（L2——v3 语言链 + 数字电路机制）
            Console.WriteLine("[1/3] 翻译器 + 运行时机制测试（L2）");
            string testProj = Path.Combine(root, "Mau", "Mau.Translator.Tests", "Mau.Translator.Tests.csproj");
            if (!RunProcess("dotnet", "build \"" + testProj + "\" -v q --nologo"))
            {
                Console.WriteLine("FAIL: 翻译器测试项目构建失败");
                return 1;
            }
            string testExe = Path.Combine(root, "Mau", "Mau.Translator.Tests", "bin", "Debug", "net8.0", "Mau.Translator.Tests.exe");
            if (!RunProcess(testExe, ""))
            {
                Console.WriteLine("FAIL: 翻译器测试失败");
                return 1;
            }
            string runtimeTestProj = Path.Combine(root, "Mau", "Mau.Runtime.Tests", "Mau.Runtime.Tests.csproj");
            if (!RunProcess("dotnet", "build \"" + runtimeTestProj + "\" -v q --nologo"))
            {
                Console.WriteLine("FAIL: 运行时测试项目构建失败");
                return 1;
            }
            string runtimeExe = Path.Combine(root, "Mau", "Mau.Runtime.Tests", "bin", "Debug", "net8.0", "Mau.Runtime.Tests.exe");
            if (!RunProcess(runtimeExe, ""))
            {
                Console.WriteLine("FAIL: 运行时机制测试失败");
                return 1;
            }
            // [段2] 语法谱黄金哈希（L3——TokenId 流哨兵）
            Console.WriteLine("[2/3] 语法谱黄金哈希（L3）");
            Dictionary<string, string> golden = LoadGolden(root);
            string casesDir = Path.Combine(root, "Mau", "Mau.Snapshots", "cases");
            string[] caseFiles = Directory.GetFiles(casesDir, "*.mau");
            Array.Sort(caseFiles, StringComparer.Ordinal);
            for (int i = 0; i < caseFiles.Length; i++)
            {
                string caseName = Path.GetFileName(caseFiles[i]);
                string source = File.ReadAllText(caseFiles[i]);
                string hash = TokenFlowV3.Hash(source, new DefaultAppearance());
                if (hash.Length == 0)
                {
                    Console.WriteLine("FAIL: 黄金哈希源词法失败——" + caseName);
                    return 1;
                }
                if (update)
                {
                    golden[caseName] = hash;
                    Console.WriteLine("UPDATED: " + caseName + " → " + hash);
                    continue;
                }
                string? recorded;
                if (!golden.TryGetValue(caseName, out recorded) || recorded == null)
                {
                    Console.WriteLine("FAIL: 黄金哈希缺失——" + caseName + "（mau test --update 生成）");
                    return 1;
                }
                if (recorded != hash)
                {
                    Console.WriteLine("FAIL: 生成漂移——TokenId 流哈希不一致（" + caseName + "）");
                    Console.WriteLine("  清单: " + recorded);
                    Console.WriteLine("  实际: " + hash);
                    return 1;
                }
            }
            if (update)
            {
                SaveGolden(root, golden);
                Console.WriteLine("黄金哈希清单已更新: golden-sha-v3.txt（" + golden.Count + " 份）");
            }
            // [段3] 积木谱（L4——brickflow 跑测：语料→内嵌→Emit→ALC→断言）
            Console.WriteLine("[3/3] 积木谱（L4）");
            Mau.Development.BrickSpecRunResult spec = Mau.Development.BrickSpecRunner.Run(root);
            if (!spec.Success)
            {
                Console.WriteLine("FAIL: " + spec.Summary);
                if (spec.Details.Length > 0)
                {
                    Console.WriteLine("  " + spec.Details);
                }
                return 1;
            }
            CliSupport.Detail(spec.Summary);
            Console.WriteLine("MAU_CHECKS_OK");
            return 0;
        }

        /// <summary>
        /// 运行子进程——120s watchdog（RT.4/RT.5 判例：卡死测试不再无限挂起）
        /// </summary>
        /// <param name="fileName">可执行文件</param>
        /// <param name="arguments">参数</param>
        /// <returns>退出码为 0</returns>
        private static bool RunProcess(string fileName, string arguments)
{
    int timeoutMs = 120000;
    // 管道死锁根治——共享 ProcessRunner 双流并行读（原顺序 ReadToEnd 在输出 >64KB 时假超时）
    Mau.Development.ProcessRunResult pr = Mau.Development.ProcessRunner.RunAndCapture(fileName, arguments, null, timeoutMs);
    if (!pr.Started)
    {
        Console.WriteLine("FAIL: 无法启动进程——" + fileName);
        return false;
    }
    if (!pr.Exited)
    {
        Console.WriteLine("FAIL: 测试进程超时（120s）已强杀: " + fileName);
        Console.WriteLine(CliSupport.TailLines(pr.Stdout + "\n" + pr.Stderr, 20));
        return false;
    }
    if (pr.ExitCode == 0)
    {
        CliSupport.Detail(pr.Stdout);
        if (pr.Stderr.Trim().Length > 0)
        {
            CliSupport.Detail(pr.Stderr);
        }
        return true;
    }
    Console.WriteLine(CliSupport.TailLines(pr.Stdout + "\n" + pr.Stderr, 20));
    return false;
}
        /// <summary>
        /// 加载黄金哈希清单——golden-sha-v3.txt（每行：文件名 空格 哈希）
        /// </summary>
        /// <param name="root">仓库根</param>
        /// <returns>文件名 → 哈希字典</returns>
        private static Dictionary<string, string> LoadGolden(string root)
        {
            Dictionary<string, string> hashes = new Dictionary<string, string>(StringComparer.Ordinal);
            string path = Path.Combine(root, "Mau", "Mau.Snapshots", "golden-sha-v3.txt");
            if (!File.Exists(path))
            {
                return hashes;
            }
            string[] lines = File.ReadAllLines(path);
            for (int i = 0; i < lines.Length; i++)
            {
                string trimmed = lines[i].Trim();
                if (trimmed.Length == 0)
                {
                    continue;
                }
                int space = trimmed.IndexOf(' ');
                if (space < 0)
                {
                    continue;
                }
                hashes[trimmed.Substring(0, space)] = trimmed.Substring(space + 1);
            }
            return hashes;
        }

        /// <summary>
        /// 保存黄金哈希清单——按名排序，每份一行
        /// </summary>
        /// <param name="root">仓库根</param>
        /// <param name="hashes">文件名 → 哈希字典</param>
        private static void SaveGolden(string root, Dictionary<string, string> hashes)
        {
            List<string> names = new List<string>(hashes.Keys);
            names.Sort(StringComparer.Ordinal);
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            for (int i = 0; i < names.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append('\n');
                }
                sb.Append(names[i]);
                sb.Append(' ');
                sb.Append(hashes[names[i]]);
            }
            File.WriteAllText(Path.Combine(root, "Mau", "Mau.Snapshots", "golden-sha-v3.txt"), sb.ToString());
        }
    }
}
