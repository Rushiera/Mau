// ═══════════════════════════════════════════════════
// 积木: win.notify
// ID:   BRIK-WIN-003
// 类别: WIN
// 作用: 系统通知气泡——title|text flat 载荷（Shell_NotifyIcon NIF_INFO）
// 依赖: 无
// 引用: Mau.Runtime · System.Runtime.InteropServices
// 原理: Shell_NotifyIcon NIM_ADD + NIF_INFO——瞬态图标（气泡显示后 NIM_DELETE，无托盘残留）
// 常用: UiPet 事件通知（任务完成/审批结果）
// 包: 无
// ═══════════════════════════════════════════════════
using System.Runtime.InteropServices;

using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// WIN 积木——win.notify 系统通知气泡（Shell_NotifyIcon）
    /// </summary>
    public static class WinNotifyBrick
    {
        /// <summary>
        /// 通知数据——NOTIFYICONDATA 结构
        /// </summary>
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NotifyIconData
        {
            public int cbSize;
            public System.IntPtr hWnd;
            public int uID;
            public int uFlags;
            public int uCallbackMessage;
            public System.IntPtr hIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string szTip;
            public int dwState;
            public int dwStateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string szInfo;
            public int uVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            public string szInfoTitle;
            public int dwInfoFlags;
        }

        /// <summary>
        /// shell32 Shell_NotifyIcon
        /// </summary>
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern bool Shell_NotifyIcon(int dwMessage, ref NotifyIconData lpData);

        /// <summary>
        /// 弹系统通知气泡——flat 载荷 "title|text"；瞬态图标（NIM_ADD → NIM_DELETE）
        /// </summary>
        /// <param name="flat">title|text</param>
        /// <returns>true=已发起通知</returns>
        public static bool Notify(string flat)
        {
            if (flat == null)
            {
                return false;
            }
            string title = flat;
            string text = "";
            int bar = flat.IndexOf('|');
            if (bar >= 0)
            {
                title = flat.Substring(0, bar);
                text = flat.Substring(bar + 1);
            }
            if (title.Length == 0)
            {
                title = "CH4";
            }
            try
            {
                System.IntPtr h = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
                if (h == System.IntPtr.Zero)
                {
                    return false;
                }
                NotifyIconData data = new NotifyIconData();
                data.cbSize = Marshal.SizeOf(typeof(NotifyIconData));
                data.hWnd = h;
                data.uID = 1001;
                data.uFlags = 0x00000010; // NIF_INFO
                data.szInfo = text.Length > 0 ? text : title;
                data.szInfoTitle = title;
                data.dwInfoFlags = 1; // NIIF_INFO
                Shell_NotifyIcon(1, ref data); // NIM_ADD
                Shell_NotifyIcon(2, ref data); // NIM_DELETE——瞬态，气泡仍显示
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
// #MAU_CHECKSUM:SHA256:E04EB1CF1E6EA92BD0EE9FB51219FDE9ACE332232F7498C455EFF818BF5C80C9
