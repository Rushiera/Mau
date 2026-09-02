using System;
using System.IO;

namespace SetUp
{
    /// <summary>
    /// SetUp deploy 模式——产出正式运行实例到外部目标目录。
    /// 目标：把 public/app 可运行版本复制到目标目录（无 Mau.sln → 运行走 AppData Data 三级锚定）。
    /// 规范：design-ch4-release.md §二/§三/§五——目标目录禁止在仓库内；运行中先停进程（模块锁）。
    /// </summary>
    public static partial class Program
    {
        /// <summary>
        /// deploy 模式入口——复制 public/app 到目标目录 + 版本落盘。
        /// </summary>
        /// <param name="repoRoot">仓库根</param>
        /// <param name="targetDir">目标目录（外部运行实例）</param>
        /// <returns>退出码（0=成功，非0=失败）</returns>
        private static int Deploy(string repoRoot, string targetDir)
        {
            string sourceDir = Path.Combine(repoRoot, "public", "app");
            Console.WriteLine("[SetUp] deploy —— 部署正式运行实例");
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

            // [段3] 复制 public\app 平铺内容到目标（含 Flows/ html/）
            // 运行中 publish 纪律：目标实例运行中 → 复制会撞模块锁——先提示停进程
            Console.WriteLine("[SetUp] 复制中——若目标实例正在运行，请先停止（模块锁会拒绝覆盖）。");
            if (!CopyDirectory(sourceDir, targetFull))
            {
                return Fail("目录复制失败——目标目录可能被占用或不可写。");
            }

            // [段3b] 配置模板复制——仓库 config/ 模板 → 目标 config/（规范 §二：配置模板进部署包；Data 自举在首次运行完成）
            string configTpl = Path.Combine(repoRoot, "config");
            if (Directory.Exists(configTpl))
            {
                CopyDirectory(configTpl, Path.Combine(targetFull, "config"));
            }

            // [段4] 版本落盘——version.txt（读取 CatHome4.csproj <Version>）
            string version = ReadVersion(repoRoot);
            try
            {
                File.WriteAllText(Path.Combine(targetFull, "version.txt"), version);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[SetUp] 警告：version.txt 写入失败——" + ex.Message);
            }
            Console.WriteLine("[SetUp] 版本: " + version);

            // [段4b] 实例 Data 初始化——自包含（design-ch4-release §三 语义扩展：部署实例 = version.txt 标记自锚定）
            // 生成 Data 骨架 + 实例级 workspace.json（roots 仅 runtime=实例 Data——不读机器旧 AppData 配置）
            string dataDir = Path.Combine(targetFull, "Data");
            Directory.CreateDirectory(Path.Combine(dataDir, "config"));
            Directory.CreateDirectory(Path.Combine(dataDir, "secrets"));
            Directory.CreateDirectory(Path.Combine(dataDir, "sessions"));
            Directory.CreateDirectory(Path.Combine(dataDir, "runs"));
            string wsPath = Path.Combine(dataDir, "config", "workspace.json");
            if (!File.Exists(wsPath))
            {
                string runtimeRoot = targetFull.Replace('\\', '/') + "/Data";
                string wsJson = "{\n" +
                    "  \"_说明\": \"实例工作区配置——SetUp deploy 自动生成；roots 仅实例 Data（自包含）。可自行追加 ccbp 等只读知识根\",\n" +
                    "  \"roots\": [\n" +
                    "    { \"id\": \"runtime\", \"path\": \"" + runtimeRoot + "\", \"writable\": true }\n" +
                    "  ],\n" +
                    "  \"inject\": []\n" +
                    "}";
                File.WriteAllText(wsPath, wsJson);
                Console.WriteLine("[SetUp] 实例 Data 已初始化: " + dataDir);
            }
            else
            {
                Console.WriteLine("[SetUp] 实例 workspace.json 已存在——保留现有配置: " + wsPath);
            }
            // 启动脚本——显式 CH4_DATA_ROOT 逃生口（GBK 编码；%~dp0 = 实例目录 = Data 的父级；存在即保留）
            string scriptPath = Path.Combine(targetFull, "启动CatHome4.cmd");
            if (!File.Exists(scriptPath))
            {
                string script = "@echo off\r\n" +
                    "rem CatHome4 启动脚本——CH4_DATA_ROOT 指向实例目录（Data 的父级），自包含运行\r\n" +
                    "set CH4_DATA_ROOT=%~dp0\r\n" +
                    "\"%~dp0CatHome4.exe\"\r\n" +
                    "pause\r\n";
                File.WriteAllText(scriptPath, script, System.Text.Encoding.GetEncoding(936));
                Console.WriteLine("[SetUp] 启动脚本已生成: " + scriptPath);
            }

            // [段5] 完成提示——实例 Data 自包含（version.txt 标记自锚定，不落机器 AppData）；AppData 仅无标记兜底
            Console.WriteLine("[SetUp] deploy 完成。");
            Console.WriteLine("  实例 Data 落位: " + Path.Combine(targetFull, "Data"));
            Console.WriteLine("  启动: " + Path.Combine(targetFull, "CatHome4.exe") + "（或启动CatHome4.cmd——显式 CH4_DATA_ROOT）");
            return 0;
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
        /// 读取 CatHome4.csproj 版本号——<Version> 标签（唯一事实源）。
        /// </summary>
        /// <param name="repoRoot">仓库根</param>
        /// <returns>版本号（未找到返回 unknown）</returns>
        private static string ReadVersion(string repoRoot)
        {
            string csproj = Path.Combine(repoRoot, "CatHome4", "CatHome4.csproj");
            try
            {
                if (!File.Exists(csproj))
                {
                    return "unknown";
                }
                string text = File.ReadAllText(csproj);
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
