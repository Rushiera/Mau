using System;
using System.IO;

namespace SetUp
{
    /// <summary>
    /// SetUp deploy 模式——产出正式运行实例到外部目标目录。
    /// 目标：把 public/app 可运行版本复制到目标目录（无 Mau.sln → 运行走 AppData Data 三级锚定）。
    /// 规范：design-ch4-release.md §二/§三/§五——目标目录禁止在仓库内；运行中先停进程（模块锁）。
    /// </summary>
    /// <summary>
    /// Program 分部——deploy 模式：产物发布到目标运行目录（原子切换 + 报告）。
    /// </summary>
    public static partial class Program
    {
        /// <summary>deploy 模式入口——public/app 落新目录后原子切换上架（现行更名 _old 留作回退源）+ 版本落盘。</summary>
        /// <param name="repoRoot">仓库根</param>
        /// <param name="targetDir">目标目录（外部运行实例）</param>
        /// <returns>退出码（0=成功，非0=失败）</returns>
        private static int Deploy(string repoRoot, string targetDir)
        {
            string sourceDir = Path.Combine(repoRoot, "public", "app");
            Console.WriteLine("[SetUp] deploy —— 部署正式运行实例（原子切换）");
            Console.WriteLine("  源: " + sourceDir);
            Console.WriteLine("  目标: " + targetDir);

            // [段1] 目标目录校验——禁止部署到仓库内（FindRepoRoot 误判 → Data 挂仓库根，污染纯净源）
            // 边界判定：比较时仓库根追加路径分隔符——防 "mau" 前缀误伤 "mau-test-deploy" 这类兄弟目录
            string targetFull = Path.GetFullPath(targetDir);
            string repoFull = Path.GetFullPath(repoRoot);
            string repoPrefix = repoFull.TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            bool insideRepo = string.Equals(targetFull, repoFull, StringComparison.OrdinalIgnoreCase) ||
                              targetFull.StartsWith(repoPrefix, StringComparison.OrdinalIgnoreCase);
            if (insideRepo)
            {
                return Fail("目标目录位于仓库内——禁止部署（Data 会误挂仓库根）。目标: " + targetFull);
            }

            // [段2] 前置校验——public\app 已就绪（prepare 过）
            string hostExe = Path.Combine(sourceDir, "CatHome4.exe");
            if (!File.Exists(hostExe))
            {
                return Fail("public\\app\\CatHome4.exe 不存在——请先运行 SetUp.exe prepare 重建发布链。");
            }

            // [段3] 复制 public\app 平铺内容到 <target>_new（含 Flows/ html/）——先落新目录，全程不触碰现行实例
            string targetNew = targetFull + "_new";
            string targetOld = targetFull + "_old";
            Console.WriteLine("[SetUp] 复制中——落新目录: " + targetNew);
            long copyMs = Environment.TickCount64;
            bool copyOk = CopyDirectory(sourceDir, targetNew);
            _steps.Add(new StepReport() { Step = 1, Name = "复制 public/app -> <target>_new", Ok = copyOk, Ms = Environment.TickCount64 - copyMs });
            if (!copyOk)
            {
                return Fail("目录复制失败——新目录可能被占用或不可写。");
            }

            // [段3b] 配置模板复制——仓库 config/ 模板 → 新目录 config/（规范 §二：配置模板进部署包；Data 自举在首次运行完成）
            string configTpl = Path.Combine(repoRoot, "config");
            if (Directory.Exists(configTpl))
            {
                CopyDirectory(configTpl, Path.Combine(targetNew, "config"));
            }

            // [段4] 版本落盘——version.txt（读取 CatHome4.csproj <Version>；写新目录）
            string version = ReadVersion(repoRoot);
            long verMs = Environment.TickCount64;
            bool verOk = true;
            try
            {
                File.WriteAllText(Path.Combine(targetNew, "version.txt"), version);
            }
            catch (Exception ex)
            {
                verOk = false;
                Console.WriteLine("[SetUp] 警告：version.txt 写入失败——" + ex.Message);
            }
            _steps.Add(new StepReport() { Step = 2, Name = "版本落盘 version.txt", Ok = verOk, Ms = Environment.TickCount64 - verMs });
            Console.WriteLine("[SetUp] 版本: " + version);

            // [段4b] 原子切换——现行更名 _old（留作回退源）→ 新目录更名上架（同卷改名 = 原子）
            long switchMs = Environment.TickCount64;
            string switchErr = SwitchDirectory(targetFull, targetNew, targetOld);
            _steps.Add(new StepReport() { Step = 3, Name = "原子切换运行区", Ok = switchErr.Length == 0, Ms = Environment.TickCount64 - switchMs });
            if (switchErr.Length > 0)
            {
                return Fail("原子切换失败——" + switchErr);
            }

            // [段5] 完成提示——Data 走 AppData（本机 %LOCALAPPDATA%/CatHome4/Data 三级锚定回退）
            Console.WriteLine("[SetUp] deploy 完成。");
            Console.WriteLine("  正式实例 Data 落位: " + Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CatHome4", "Data"));
            Console.WriteLine("  启动: " + Path.Combine(targetFull, "CatHome4.exe"));
            Console.WriteLine("  回退源: " + targetOld);
            return 0;
        }
        /// <summary>
        /// 原子切换运行区——现行目录更名 _old（先清旧回退源）→ 新目录更名上架；任一步失败即回滚。
        /// 前提：运行区（MauOut）无持久信息——持久数据在 Data 三级锚定区，整目录替换安全。
        /// </summary>
        /// <param name="target">现行运行区绝对路径</param>
        /// <param name="targetNew">新目录绝对路径（内容已就绪）</param>
        /// <param name="targetOld">回退源绝对路径</param>
        /// <returns>空串=切换成功；非空=错误描述（已尽力回滚）</returns>
        private static string SwitchDirectory(string target, string targetNew, string targetOld)
        {
            // [段1] 清旧回退源——上一次切换的残留（旧宿主已退出，模块锁已释放）
            if (Directory.Exists(targetOld))
            {
                try
                {
                    Directory.Delete(targetOld, true);
                }
                catch (Exception ex)
                {
                    return "回退源清理失败（" + targetOld + "）：" + ex.Message;
                }
            }
            // [段2] 现行 → 回退源
            bool movedOld = false;
            if (Directory.Exists(target))
            {
                try
                {
                    Directory.Move(target, targetOld);
                    movedOld = true;
                }
                catch (Exception ex)
                {
                    return "现行目录更名失败（模块锁？）：" + ex.Message;
                }
            }
            // [段3] 新目录上架——失败回滚（回退源改回现行）
            try
            {
                Directory.Move(targetNew, target);
            }
            catch (Exception ex)
            {
                if (movedOld)
                {
                    try
                    {
                        Directory.Move(targetOld, target);
                    }
                    catch (Exception)
                    {
                    }
                }
                return "新目录上架失败（已回滚）：" + ex.Message;
            }
            return "";
        }

        /// <summary>
        /// 递归目录复制——文件覆盖复制（已存在直接覆盖），失败即中止。
        /// </summary>
        /// <param name="sourceDir">源目录</param>
        /// <param name="targetDir">目标目录</param>
        /// <returns>true=复制成功</returns>
        private static bool CopyDirectory(string sourceDir, string targetDir)
        {
            try
            {
                if (!Directory.Exists(targetDir))
                {
                    Directory.CreateDirectory(targetDir);
                }
                string[] dirs = Directory.GetDirectories(sourceDir, "*", SearchOption.AllDirectories);
                for (int i = 0; i < dirs.Length; i = i + 1)
                {
                    string rel = dirs[i].Substring(sourceDir.Length).TrimStart('\\', '/');
                    Directory.CreateDirectory(Path.Combine(targetDir, rel));
                }
                string[] files = Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories);
                for (int i = 0; i < files.Length; i = i + 1)
                {
                    string rel = files[i].Substring(sourceDir.Length).TrimStart('\\', '/');
                    File.Copy(files[i], Path.Combine(targetDir, rel), true);
                }
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine("[SetUp] 复制异常: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// 读取仓库单点版本号——Directory.Build.props 的 Version 标签（A53 唯一事实源：CH4 + Mau 全跟随，
        /// SetUp 独立）；消费路径 = deploy 落盘 version.txt / relaunch 回执。
        /// </summary>
        /// <param name="repoRoot">仓库根</param>
        /// <returns>版本号（未找到返回 unknown）</returns>
        private static string ReadVersion(string repoRoot)
        {
            string props = Path.Combine(repoRoot, "Directory.Build.props");
            try
            {
                if (!File.Exists(props))
                {
                    return "unknown";
                }
                string text = File.ReadAllText(props);
                string marker = "<Version>";
                int start = text.IndexOf(marker, StringComparison.Ordinal);
                if (start < 0)
                {
                    return "unknown";
                }
                start = start + marker.Length;
                int end = text.IndexOf("</Version>", start, StringComparison.Ordinal);
                if (end < 0)
                {
                    return "unknown";
                }
                return text.Substring(start, end - start).Trim();
            }
            catch (Exception)
            {
                return "unknown";
            }
        }
    }
}
