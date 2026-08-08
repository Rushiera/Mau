// ═══════════════════════════════════════════════════
// 积木: system.env
// ID:   BRIK-SYSTEM-003
// 类别: SYSTEM
// 作用: 本地环境——机器名/用户/OS/架构/处理器数/环境变量快照
// 依赖: 无
// 引用: System · System.Runtime.InteropServices
// 原理: Environment + RuntimeInformation 直接拉取
// 常用: SystemCat 工具 Cat——本地环境查询（M2c 六+一域 System 域）
// ═══════════════════════════════════════════════════
using System;
using System.Text;

namespace Mau.Bricks
{
    /// <summary>
    /// 系统积木——system.env 本地环境（纯函数无状态）
    /// </summary>
    public static class SystemEnvBrick
    {
        /// <summary>
        /// 本地环境——机器名/用户/OS/架构/处理器数/进程数/系统目录
        /// </summary>
        /// <param name="env">环境文本</param>
        /// <returns>true=成功</returns>
        public static bool Env(out string env)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("机器名: " + Environment.MachineName);
            sb.AppendLine("用户: " + Environment.UserName);
            sb.AppendLine("用户域: " + Environment.UserDomainName);
            sb.AppendLine("OS: " + Environment.OSVersion.VersionString);
            sb.AppendLine("架构: " + System.Runtime.InteropServices.RuntimeInformation.OSArchitecture);
            sb.AppendLine("框架: " + System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription);
            sb.AppendLine("处理器数: " + Environment.ProcessorCount);
            sb.AppendLine("进程数: " + System.Diagnostics.Process.GetProcesses().Length);
            sb.AppendLine("系统目录: " + Environment.SystemDirectory);
            sb.AppendLine("当前目录: " + Environment.CurrentDirectory);
            env = sb.ToString().TrimEnd('\n');
            return true;
        }
    }
}
// #MAU_CHECKSUM:SHA256:04A7FE4C655F20EC41FDF41ABD5BEFA572182701E36C42DBACF4F6589824A50B
