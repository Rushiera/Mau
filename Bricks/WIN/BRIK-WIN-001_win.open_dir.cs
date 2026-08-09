// ═══════════════════════════════════════════════════
// 积木: win.open_dir
// ID:   BRIK-WIN-001
// 类别: WIN
// 作用: 打开目录（explorer）——target: config=AppData 配置目录 / workspace=宿主目录 / 猫名=Data/Cats/{cat}
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
            if (t == "config")
            {
                return AppDataConfig.RootDir;
            }
            if (t == "workspace")
            {
                return System.AppContext.BaseDirectory;
            }
            return System.IO.Path.Combine(System.AppContext.BaseDirectory, "Data", "Cats", t);
        }
    }
}
// #MAU_CHECKSUM:SHA256:297FCBDC2DB761D5B67F71687494B039C57D91C820AA8233BE7F7CCF9249B9DD
