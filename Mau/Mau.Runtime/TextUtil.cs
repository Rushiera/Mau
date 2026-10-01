using System.Text;

namespace Mau.Runtime
{
    /// <summary>
    /// 文本工具——字符串处理统一实现（JSON 转义、实体解码等）。
    /// 收敛历史：CommandBricks/CommandMauProj/CommandRun/HttpTransport 原四份 JsonEscape 独立实现，
    /// 下沉 Runtime（Cli/Observer 均引用）统一（2026-08-11 审查修复轮）。
    /// 实体解码（A133，2026-10-02）：模型侧误转义的字符形态还原——统一实现，宿主参数面与 ps 输出面共用。
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

        /// <summary>
        /// HTML/XML 实体解码——把误转义的字符形态还原（四字符序列 amp+lt+分号 → 单个尖括号；本文档内一律写作转义形态以免自伤）。
        /// 支持命名实体（lt / gt / amp / quot / apos）与数字实体（#NN 十进制 / #xHH 十六进制）；**只解一层**（不递归）。
        /// 逃生口：要表达字面量实体文本，写双写形态（amp + amp + lt + 分号 —— 一次解码得单写实体文本）。
        /// </summary>
        /// <param name="s">原文（可为 null）</param>
        /// <returns>解码文本（无实体时原样返回同一引用）</returns>
        public static string DecodeEntities(string? s)
        {
            if (s == null || s.Length == 0 || s.IndexOf('&') < 0)
            {
                return s ?? "";
            }
            StringBuilder sb = new StringBuilder(s.Length);
            int i = 0;
            while (i < s.Length)
            {
                char c = s[i];
                if (c != '&')
                {
                    sb.Append(c);
                    i = i + 1;
                    continue;
                }
                int semi = s.IndexOf(';', i + 1);
                if (semi < 0)
                {
                    sb.Append(c);
                    i = i + 1;
                    continue;
                }
                int span = semi - i;
                if (span < 3 || span > 9)
                {
                    sb.Append(c);
                    i = i + 1;
                    continue;
                }
                string body = s.Substring(i + 1, span - 1);
                string? decoded = DecodeEntityBody(body);
                if (decoded == null)
                {
                    sb.Append(c);
                    i = i + 1;
                    continue;
                }
                sb.Append(decoded);
                i = semi + 1;
            }
            return sb.ToString();
        }

        /// <summary>
        /// 单个实体体解码——命名实体（amp / lt / gt / quot / apos）+ 数字实体（十进制 / 十六进制）；未知名返回 null（调用方原样保留）。
        /// </summary>
        /// <param name="body">&amp; 与 ; 之间的正文（不含界定符）</param>
        /// <returns>解码后的字符文本（null=未知实体）</returns>
        private static string? DecodeEntityBody(string body)
        {
            if (body == "amp")
            {
                return "&";
            }
            if (body == "lt")
            {
                return "<";
            }
            if (body == "gt")
            {
                return ">";
            }
            if (body == "quot")
            {
                return "\"";
            }
            if (body == "apos")
            {
                return "'";
            }
            if (body.Length > 1 && body[0] == '#')
            {
                bool hex = body.Length > 2 && (body[1] == 'x' || body[1] == 'X');
                string digits = hex ? body.Substring(2) : body.Substring(1);
                if (digits.Length == 0 || digits.Length > 6)
                {
                    return null;
                }
                int value = 0;
                if (hex)
                {
                    int parsedHex;
                    if (!int.TryParse(digits, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out parsedHex))
                    {
                        return null;
                    }
                    value = parsedHex;
                }
                else
                {
                    int parsedDec;
                    if (!int.TryParse(digits, out parsedDec))
                    {
                        return null;
                    }
                    value = parsedDec;
                }
                if (value <= 0 || value > 0xFFFF)
                {
                    return null;
                }
                return ((char)value).ToString();
            }
            return null;
        }
    }
}
