using System;
using System.IO;
using System.Text;

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

            // [2] 全部程序集——Mau.* + Roslyn + 第三方依赖（全量拷贝，发布目录与宿主目录等价）
            // 完整性优先：发布目录必须与宿主目录等价，TPA 引用链不缺环
            string[] allDlls = Directory.GetFiles(baseDir, "*.dll", SearchOption.TopDirectoryOnly);
            Array.Sort(allDlls, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < allDlls.Length; i = i + 1)
            {
                copied = copied + CopyFile(allDlls[i], outDir, ref failed);
            }
            // [3] 运行时资产——runtimes/ 递归拷贝（Roslyn/System.Windows.Extensions 等 RID 特定 dll；TopDirectoryOnly 全量拷贝会漏）
            string runtimesDir = Path.Combine(baseDir, "runtimes");
            if (Directory.Exists(runtimesDir))
            {
                copied = copied + CopyDirectory(runtimesDir, Path.Combine(outDir, "runtimes"), ref failed);
            }
            // [4] 知识资产——Bricks（积木文本库：翻译器构筑期 BrickIndex 必需）+ Mau.Corpus + 工程文档
            string? root = CliSupport.FindWorkspaceRoot();
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
/// 探测文件是否被进程占用——以写方式独占打开（成功=未锁；IOException=被锁）
/// </summary>
/// <param name = "paths">待探测路径</param>
/// <returns>第一个被锁路径；全部未锁返回空串</returns>
private static string FindLockedFile(string[] paths)
{
    for (int i = 0; i < paths.Length; i = i + 1)
    {
        if (!File.Exists(paths[i]))
        {
            continue;
        }

        try
        {
            using (FileStream fs = new FileStream(paths[i], FileMode.Open, FileAccess.Write, FileShare.None))
            {
            // 探测成功——未锁
            }
        }
        catch (IOException)
        {
            return paths[i];
        }
        catch (UnauthorizedAccessException)
        {
            return paths[i];
        }
    }

    return "";
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
            catch (IOException)
            {
                // GAP.5——目标被进程占用（运行实例锁定）
                Console.Error.WriteLine("拷贝失败（文件被占用——先停止运行实例）: " + source);
                failed = failed + 1;
                return 0;
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
/// 发布目录冒烟——Mau.exe check --selftest（Bricks 索引随行自检）+ 最小语料 build 闭环
/// </summary>
///

///
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
                // [1] check --selftest——发布包随行资源自检（不要求 Mau.sln；D24 check-source/selftest 分级）
                System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo();
                psi.FileName = mauExe;
                psi.Arguments = "check --selftest";
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
                    Console.Error.WriteLine("冒烟: check --selftest 失败（退出码 " + p.ExitCode + "）");
                    Console.Error.WriteLine(stdout);
                    Console.Error.WriteLine(stderr);
                    return false;
                }

                // [2] 最小构筑闭环——内置最小语料（file.read，随行 Bricks 必然存在），不依赖 Mau.Corpus 内容质量
                string smokeDir = Path.Combine(publishDir, "CatTemp", "smoke");
                Directory.CreateDirectory(smokeDir);
                string smokeMau = Path.Combine(smokeDir, "Smoke.mau");
                string smokeSource = "Mau 0.1\n基座: Mau.Runtime/v0.1\n\n命题:\n  P_Go 信号\n  P_Done 事实\n  P_Failed 事实\n\n变迁 T_Smoke:\n  前置: P_Go\n  动作: file.read\n  参数: path\n  时限: 60帧\n  后置: P_Done / P_Failed\n";
                File.WriteAllText(smokeMau, smokeSource, Encoding.UTF8);
                string smokeOut = Path.Combine(smokeDir, "out");
                System.Diagnostics.ProcessStartInfo buildPsi = new System.Diagnostics.ProcessStartInfo();
                buildPsi.FileName = mauExe;
                buildPsi.Arguments = "build \"" + smokeMau + "\" -o \"" + smokeOut + "\"";
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
                    if (Directory.Exists(smokeDir))
                    {
                        Directory.Delete(smokeDir, true);
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
        }}
}
