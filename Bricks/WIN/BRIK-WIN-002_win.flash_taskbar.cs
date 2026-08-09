// ═══════════════════════════════════════════════════
// 积木: win.flash_taskbar
// ID:   BRIK-WIN-002
// 类别: WIN
// 作用: 任务栏闪烁——主窗口 FlashWindow（用户注意）
// 依赖: 无
// 引用: Mau.Runtime · System.Runtime.InteropServices
// 原理: FlashWindow P/Invoke——当前进程主窗口句柄
// 常用: UiPet 事件提醒（消息到达/审批挂起时）
// 包: 无
// ═══════════════════════════════════════════════════
using System.Runtime.InteropServices;

using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// WIN 积木——win.flash_taskbar 任务栏闪烁（依赖 user32）
    /// </summary>
    public static class WinFlashTaskbarBrick
    {
        /// <summary>
        /// 任务栏闪烁——主窗口未激活时闪烁一次（持续提醒需宿主循环调用）
        /// </summary>
        /// <returns>true=已闪烁</returns>
        public static bool FlashTaskbar()
        {
            try
            {
                System.IntPtr h = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
                if (h == System.IntPtr.Zero)
                {
                    return false;
                }
                FlashWindow(h, true);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// user32 FlashWindow——闪烁窗口标题栏
        /// </summary>
        [DllImport("user32.dll")]
        private static extern bool FlashWindow(System.IntPtr hWnd, bool bInvert);
    }
}
// #MAU_CHECKSUM:SHA256:02536BDEB75946F58A7C58268C613573E86B79942140C1396831AE4D8322E721
