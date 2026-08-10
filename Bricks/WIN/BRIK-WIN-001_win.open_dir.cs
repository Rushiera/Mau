// ═══════════════════════════════════════════════════
// 积木: win.open_dir
// ID:   BRIK-WIN-001
// 类别: WIN
// 作用: 打开目录（explorer）——target: config=AppData 配置目录 / workspace=宿主目录 / 猫名=Data/Cats/{cat}（仅已存在目录）
// 依赖: 无
// 引用: Mau.Runtime · System.Diagnostics
// 原理: Process.Start explorer——路径解析在积木内（AppDataConfig.RootDir / AppContext.BaseDirectory）
// 常用: UiPet Chat_UI_OpenDir 路由（配置页/工作空间跳转按钮）
// 包: 无
// ═══════════════════════════════════════════════════
using System.Diagnostics;

using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// WIN 积木——win.open_dir 打开目录（explorer）
    /// </summary>
    public static class WinOpenDirBrick
    {
        /// <summary>
        /// 打开目录——target: config=AppData 配置目录 / workspace=宿主目录 / 猫名=Data/Cats/{猫名}
        /// </summary>
        /// <param name="target">目录标识</param>
        /// <returns>true=已发起打开</returns>
        public static bool OpenDir(string target)
        {
            string path = ResolvePath(target);
            if (path.Length == 0)
            {
                return false;
            }
            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe", "\"" + path + "\"")
                {
                    UseShellExecute = true
                });
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 解析目录标识为绝对路径
        /// </summary>
        /// <param name="target">标识</param>
        /// <returns>绝对路径；非法返回空串</returns>
        private static string ResolvePath(string target)
        {
            if (target == null || target.Length == 0)
            {
                return "";
            }
            string t = target.Trim();
            string path = "";
            if (t == "config")
            {
                path = AppDataConfig.RootDir;
            }
            else if (t == "workspace")
            {
                path = System.AppContext.BaseDirectory;
            }
            else
            {
                path = System.IO.Path.Combine(System.AppContext.BaseDirectory, "Data", "Cats", t);
            }
            // 只打开已存在目录——非法/不存在路径不弹 explorer（跑测无副作用：随机临时路径 → false 不拉起窗口）
            if (System.IO.Directory.Exists(path))
            {
                return path;
            }
            return "";
        }
    }
}
// #MAU_CHECKSUM:SHA256:F09B2384E891D4B459AD5D16454AE4339CE40EA0FD2685A72A73BC3C6CB90B19
