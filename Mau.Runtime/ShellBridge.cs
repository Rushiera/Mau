using System;

namespace Mau.Runtime
{
    /// <summary>
    /// Shell 桥（程序级）——shell.exec 固定工作目录的配置/读取入口（宿主启动时配置，积木执行时读取）
    /// </summary>
    public static class ShellBridge
    {
        /// <summary>
        /// 固定工作目录——默认进程当前目录；宿主显式配置（仓库根/Data 根）
        /// </summary>
        private static string _workingDirectory = Environment.CurrentDirectory;

        /// <summary>
        /// 当前固定工作目录——shell.exec 积木执行时读取（任意线程只读）
        /// </summary>
        public static string WorkingDirectory
        {
            get { return _workingDirectory; }
        }

        /// <summary>
        /// 配置固定工作目录——宿主启动时调用（绝对路径规范化）
        /// </summary>
        /// <param name="workingDirectory">工作目录</param>
        public static void ConfigureWorkingDirectory(string workingDirectory)
        {
            if (string.IsNullOrWhiteSpace(workingDirectory))
            {
                throw new ArgumentException("Working directory is empty.", "workingDirectory");
            }
            _workingDirectory = System.IO.Path.GetFullPath(workingDirectory);
        }
/// <summary>
/// 重置——恢复默认工作目录（进程当前目录）（D26 统一 Reset 契约；宿主切换/测试隔离调用）
/// </summary>
public static void Reset()
{
    _workingDirectory = Environment.CurrentDirectory;
}    }
}
