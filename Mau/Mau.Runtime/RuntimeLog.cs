using System;

namespace Mau.Runtime
{
    /// <summary>
    /// 运行时日志通道——可注入（零控制台依赖审查项 P1-6：基座库禁止直接 Console）
    /// 宿主启动时注入 Error（如 CH4 面板/文件），未注入=静默
    /// </summary>
    public static class RuntimeLog
    {
        /// <summary>
        /// 错误日志通道——宿主注入
        /// </summary>
        public static Action<string>? Error;

        /// <summary>
        /// 输出错误——通道注入时转发，未注入静默
        /// </summary>
        /// <param name="message">消息</param>
        public static void ErrorOut(string message)
        {
            if (Error != null)
            {
                Error(message);
            }
            // Log 并入审计——错误通道同步写审计（category=log.error；程序级 Default 未配置时不记录）
            AuditStore? audit = AuditStore.Default;
            if (audit != null)
            {
                audit.Record("RuntimeLog", "log.error", -1, new AuditProp[] {
                    new AuditProp("message", AuditStore.Summarize(message))
                });
            }
        }
    }
}
