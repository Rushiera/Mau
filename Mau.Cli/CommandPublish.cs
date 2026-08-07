using System;
using System.IO;

namespace Mau.Cli
{
    /// <summary>
    /// mau publish 命令——基座全量发布：Mau.exe + 全部 Mau.*.dll + Roslyn + 知识文档 → 指定目录
    /// 产物为 framework-dependent（依赖环境 .NET 8），随宿主目录整体部署
    /// </summary>
    public static class CommandPublish
    {
        /// <summary>
        /// 执行 mau publish
        /// </summary>
        /// <param name="args">命令行参数——不含 "publish" 本身</param>
        /// <returns>退出码</returns>
        public static int Execute(string[] args)
        {
            string outDir = ".";
            for (int i = 0; i < args.Length; i = i + 1)
            {
                if (args[i] == "-o" && i + 1 < args.Length)
                {
                    outDir = args[i + 1];
                    i = i + 1;
                }
            }

            string baseDir = AppContext.BaseDirectory;
            if (!Directory.Exists(baseDir))
            {
                Console.Error.WriteLine("FAIL: 宿主目录不存在——" + baseDir);
                return 1;
            }
            if (!Directory.Exists(outDir))
            {
                Directory.CreateDirectory(outDir);
            }

            int copied = 0;
            int failed = 0;

            // [1] 可执行文件 + 配置（framework-dependent 必需）
            copied = copied + CopyFile(Path.Combine(baseDir, "Mau.exe"), outDir, ref failed);
            copied = copied + CopyFile(Path.Combine(baseDir, "Mau.dll"), outDir, ref failed);
            copied = copied + CopyFile(Path.Combine(baseDir, "Mau.deps.json"), outDir, ref failed);
            copied = copied + CopyFile(Path.Combine(baseDir, "Mau.runtimeconfig.json"), outDir, ref failed);

            // [2] 全部程序集——Mau.* + Roslyn + 第三方依赖（ClosedXML/OpenXml 等 Office 积木包）
            // 完整性优先：发布目录必须与宿主目录等价，TPA 引用链不缺环
            string[] allDlls = Directory.GetFiles(baseDir, "*.dll", SearchOption.TopDirectoryOnly);
            Array.Sort(allDlls, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < allDlls.Length; i = i + 1)
            {
                copied = copied + CopyFile(allDlls[i], outDir, ref failed);
            }
            // [4] 知识资产——Bricks（积木文本库：翻译器构筑期 BrickIndex 必需）+ Mau.Corpus + 工程文档
            string? root = FindWorkspaceRoot();
            if (root != null)
            {
                copied = copied + CopyDirectory(Path.Combine(root, "Bricks"),
                    Path.Combine(outDir, "Bricks"), ref failed);
                copied = copied + CopyDirectory(Path.Combine(root, "Mau.Corpus"),
                    Path.Combine(outDir, "Mau.Corpus"), ref failed);
                copied = copied + CopyFile(Path.Combine(root, "README.md"), outDir, ref failed);
                copied = copied + CopyFile(Path.Combine(root, "LICENSE"), outDir, ref failed);
            }

            // [5] 发布后冒烟——发布目录自检（Bricks/ 随行 + 翻译器可用 + 最小构筑闭环）
            if (!SmokeTest(outDir))
            {
                Console.Error.WriteLine("FAIL: 发布目录冒烟验证失败——请检查发布完整性");
                return 1;
            }

            Console.WriteLine("发布完成: " + Path.GetFullPath(outDir));
            Console.WriteLine("  文件: " + copied + " 个" + (failed > 0 ? "，失败 " + failed + " 个" : ""));
            Console.WriteLine("  运行: " + Path.Combine(Path.GetFullPath(outDir), "Mau.exe") + " test");
            return failed > 0 ? 1 : 0;
        }

        /// <summary>
        /// 拷贝单文件——已存在则覆盖
        /// </summary>
        /// <param name="source">源文件</param>
        /// <param name="outDir">目标目录</param>
        /// <param name="failed">失败计数</param>
        /// <returns>成功拷贝数</returns>
        private static int CopyFile(string source, string outDir, ref int failed)
        {
            if (!File.Exists(source))
            {
                failed = failed + 1;
                return 0;
            }
            try
            {
                File.Copy(source, Path.Combine(outDir, Path.GetFileName(source)), true);
                return 1;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("拷贝失败: " + source + " — " + ex.Message);
                failed = failed + 1;
                return 0;
            }
        }

        /// <summary>
        /// 拷贝目录——递归
        /// </summary>
        /// <param name="sourceDir">源目录</param>
        /// <param name="destDir">目标目录</param>
        /// <param name="failed">失败计数</param>
        /// <returns>成功拷贝文件数</returns>
        private static int CopyDirectory(string sourceDir, string destDir, ref int failed)
        {
            if (!Directory.Exists(sourceDir))
            {
                return 0;
            }
            if (!Directory.Exists(destDir))
            {
                Directory.CreateDirectory(destDir);
            }
            int count = 0;
            string[] files = Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories);
            for (int i = 0; i < files.Length; i = i + 1)
            {
                string relative = Path.GetRelativePath(sourceDir, files[i]);
                string target = Path.Combine(destDir, relative);
                string? parent = Path.GetDirectoryName(target);
                if (parent != null && !Directory.Exists(parent))
                {
                    Directory.CreateDirectory(parent);
                }
                count = count + CopyFile(files[i], Path.GetDirectoryName(target)!, ref failed);
            }
            return count;
        }

        /// <summary>
        /// 查找 workspace 根——含 Mau.sln 的目录；当前目录优先，程序集位置兜底
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
                DirectoryInfo? parent = Directory.GetParent(dir.FullName);
                if (parent == null)
                {
                    break;
                }
                dir = parent;
            }

            DirectoryInfo? exeDir = new DirectoryInfo(AppContext.BaseDirectory);
            while (exeDir != null)
            {
                string sln = Path.Combine(exeDir.FullName, "Mau.sln");
                if (File.Exists(sln))
                {
                    return exeDir.FullName;
                }
                DirectoryInfo? exeParent = Directory.GetParent(exeDir.FullName);
                if (exeParent == null)
                {
                    break;
                }
                exeDir = exeParent;
            }
            return null;
        }
/// <summary>
/// 发布目录冒烟——Mau.exe check --syntax（Bricks 索引随行可用）+ 最小语料 build 闭环
/// </summary>
/// <param name = "publishDir">发布目录</param>
/// <returns>通过</returns>
private static bool SmokeTest(string publishDir)
{
    string mauExe = Path.Combine(publishDir, "Mau.exe");
    if (!File.Exists(mauExe))
    {
        Console.Error.WriteLine("冒烟: Mau.exe 缺失——" + mauExe);
        return false;
    }

    try
    {
        // [1] check --syntax——验证 Bricks/ 已随行且翻译器可用
        System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo();
        psi.FileName = mauExe;
        psi.Arguments = "check --syntax";
        psi.WorkingDirectory = publishDir;
        psi.UseShellExecute = false;
        psi.CreateNoWindow = true;
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        System.Diagnostics.Process? p = System.Diagnostics.Process.Start(psi);
        if (p == null)
        {
            Console.Error.WriteLine("冒烟: 无法启动 Mau.exe check");
            return false;
        }

        string stdout = p.StandardOutput.ReadToEnd() ?? "";
        string stderr = p.StandardError.ReadToEnd() ?? "";
        p.WaitForExit(120000);
        if (p.ExitCode != 0)
        {
            Console.Error.WriteLine("冒烟: check --syntax 失败（退出码 " + p.ExitCode + "）");
            Console.Error.WriteLine(stdout);
            Console.Error.WriteLine(stderr);
            return false;
        }

        // [2] 最小语料 build——Mau.Corpus 随行样例
        string sample = Path.Combine(publishDir, "Mau.Corpus", "hello_cat.mau");
        if (!File.Exists(sample))
        {
            Console.Error.WriteLine("冒烟: Mau.Corpus/hello_cat.mau 缺失——跳过 build 段");
            return true;
        }

        string smokeOut = Path.Combine(publishDir, "CatTemp", "smoke");
        System.Diagnostics.ProcessStartInfo buildPsi = new System.Diagnostics.ProcessStartInfo();
        buildPsi.FileName = mauExe;
        buildPsi.Arguments = "build \"" + sample + "\" -o \"" + smokeOut + "\"";
        buildPsi.WorkingDirectory = publishDir;
        buildPsi.UseShellExecute = false;
        buildPsi.CreateNoWindow = true;
        buildPsi.RedirectStandardOutput = true;
        buildPsi.RedirectStandardError = true;
        System.Diagnostics.Process? buildP = System.Diagnostics.Process.Start(buildPsi);
        if (buildP == null)
        {
            Console.Error.WriteLine("冒烟: 无法启动 Mau.exe build");
            return false;
        }

        string buildOut = buildP.StandardOutput.ReadToEnd() ?? "";
        string buildErr = buildP.StandardError.ReadToEnd() ?? "";
        buildP.WaitForExit(120000);
        if (buildP.ExitCode != 0)
        {
            Console.Error.WriteLine("冒烟: 最小 build 失败（退出码 " + buildP.ExitCode + "）");
            Console.Error.WriteLine(buildOut);
            Console.Error.WriteLine(buildErr);
            return false;
        }

        try
        {
            if (Directory.Exists(smokeOut))
            {
                Directory.Delete(smokeOut, true);
            }
        }
        catch
        {
        // 冒烟产物清理失败不影响结果
        }

        return true;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine("冒烟: 异常——" + ex.Message);
        return false;
    }
}    }
}
