// ═══════════════════════════════════════════════════
// 积木: system.info
// ID:   BRIK-SYSTEM-001
// 类别: SYSTEM
// 作用: 运行环境信息——工作目录/LLM 端点/时间/DataBox 服务/机器名（对标 CH2 CatInfo）
// 依赖: 无
// 包: 无
// 引用: Mau.Runtime · System
// 原理: Environment + LlmBridge + DataBox 聚合（运行时信息经 DataBox scope 读取）
// 常用: SystemCat 工具 Cat——环境查询（M2c 六+一域 System 域）
// ═══════════════════════════════════════════════════
using System;
using System.Text;

namespace Mau.Bricks
{
    /// <summary>
    /// 系统积木——system.info 运行环境信息（纯函数无状态）
    /// </summary>
    public static class SystemInfoBrick
    {
        /// <summary>
        /// 运行环境信息——工作目录/LLM 端点/时间/DataBox 服务/机器名
        /// </summary>
        /// <param name="info">信息文本</param>
        /// <returns>true=成功</returns>
        public static bool Info(out string info)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("工作目录: " + Environment.CurrentDirectory);
            sb.AppendLine("LLM 端点: " + Mau.Runtime.LlmBridge.Endpoint);
            sb.AppendLine("LLM Key: " + (Mau.Runtime.LlmBridge.ApiKey.Length > 0 ? "已配置" : "未配置"));
            sb.AppendLine("系统时间: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("机器名: " + Environment.MachineName);
            sb.AppendLine("用户: " + Environment.UserName);
            sb.AppendLine("OS: " + Environment.OSVersion.VersionString);
            sb.AppendLine("架构: " + System.Runtime.InteropServices.RuntimeInformation.OSArchitecture);
            sb.AppendLine("运行时长: " + Environment.TickCount64 + "ms");
            // DataBox 服务清单（运行时白名单视图）
            Mau.Runtime.DataBoxSnapshot snapshot = Mau.Runtime.DataBox.Capture();
            sb.Append("DataBox 服务: ");
            if (snapshot.Services.Length == 0)
            {
                sb.AppendLine("(空)");
            }
            else
            {
                for (int i = 0; i < snapshot.Services.Length; i = i + 1)
                {
                    if (i > 0)
                    {
                        sb.Append(", ");
                    }
                    string full = snapshot.Services[i].TypeName;
                    int dot = full.LastIndexOf('.');
                    sb.Append(dot >= 0 ? full.Substring(dot + 1) : full);
                }
                sb.AppendLine();
            }
            info = sb.ToString().TrimEnd('\n');
            return true;
        }
    }
}
// #MAU_CHECKSUM:SHA256:301228758FDBA4F54009A3252E23036A5D2DE2BE648FAF79515E9BC201E815CB
