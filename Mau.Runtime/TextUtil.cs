namespace Mau.Runtime
{
    /// <summary>
    /// 文本工具——字符串处理统一实现（JSON 转义等）。
    /// 收敛历史：CommandBricks/CommandMauProj/CommandRun/HttpTransport 原四份 JsonEscape 独立实现，
    /// 下沉 Runtime（Cli/Observer 均引用）统一（2026-08-11 审查修复轮）。
    /// </summary>
    public static class TextUtil
    {
        /// <summary>
        /// JSON 字符串转义——统一实现
        /// </summary>
        /// <param name="s">原始字符串，可为 null</param>
        /// <returns>转义后字符串</returns>
        public static string JsonEscape(string? s)
        {
            if (s == null)
            {
                return "";
            }
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
        }
    }
}
