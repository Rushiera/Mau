using System;
using System.Text;

namespace Mau.Providers
{
    /// <summary>
    /// LLM 请求体 JSON 文本辅助——跨服务共享（R4-P3-07：Vision / WebSearch 两处私有 EscapeJson 重复实现收拢）。
    /// </summary>
    internal static class LlmJson
    {
        /// <summary>
        /// JSON 字符串转义——双引号 / 反斜杠 / 控制字符（请求体组装安全；不含外层引号）。
        /// </summary>
        /// <param name="value">原始字符串（null 视为空串）</param>
        /// <returns>转义后字符串</returns>
        public static string Escape(string value)
        {
            if (value == null)
            {
                return "";
            }
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c == '"' || c == '\\')
                {
                    sb.Append('\\');
                    sb.Append(c);
                }
                else if (c == '\n')
                {
                    sb.Append("\\n");
                }
                else if (c == '\r')
                {
                    sb.Append("\\r");
                }
                else if (c == '\t')
                {
                    sb.Append("\\t");
                }
                else if (c < 0x20)
                {
                    sb.Append("\\u");
                    sb.Append(((int)c).ToString("x4"));
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }
    }
}
