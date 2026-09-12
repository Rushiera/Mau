using System;
using System.Runtime.InteropServices;

namespace SetUp
{
    /// <summary>
    /// Program 分部——Win32 控制台 P/Invoke 声明（R7-P3-8：与 WinForms UI 逻辑分离，UI 逻辑见 Program.Ui.cs）。
    /// </summary>
    public static partial class Program
    {
        /// <summary>
        /// 附加父控制台——CLI 模式（WinExe 无自有控制台；交互终端可见输出；自动化重定向场景跳过）。
        /// </summary>
        /// <param name="dwProcessId">父进程 ID，-1=父进程控制台</param>
        /// <returns>true=附加成功</returns>
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AttachConsole(int dwProcessId);

        /// <summary>
        /// 取标准句柄——AttachConsole 成功后 .NET Console 流需手动绑定控制台句柄（否则 WriteLine 仍丢失）
        /// </summary>
        /// <param name="nStdHandle">标准句柄类型（STD_OUTPUT_HANDLE/STD_ERROR_HANDLE）</param>
        /// <returns>句柄（失败返回 INVALID_HANDLE_VALUE）</returns>
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetStdHandle(int nStdHandle);

        /// <summary>
        /// 取控制台窗口句柄——无控制台（WinExe 未附加成功）返回 IntPtr.Zero——自动化检测的本质判据
        /// </summary>
        /// <returns>控制台窗口句柄</returns>
        [DllImport("kernel32.dll")]
        private static extern IntPtr GetConsoleWindow();
    }
}
