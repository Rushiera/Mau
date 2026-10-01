using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

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
            StringBuilder sb = new StringBuilder(s.Length + 8);
            for (int i = 0; i < s.Length; i = i + 1)
            {
                char c = s[i];
                if (c == '"')
                {
                    sb.Append("\\\"");
                }
                else if (c == '\\')
                {
                    sb.Append("\\\\");
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
                else if (c == '\b')
                {
                    sb.Append("\\b");
                }
                else if (c == '\f')
                {
                    sb.Append("\\f");
                }
                else if (c < ' ')
                {
                    // [段1] 控制字符兜底——其余 U+0000-U+001F 走 \u00xx（合法 JSON，不得裸出）
                    sb.Append("\\u");
                    sb.Append(((int)c).ToString("x4", System.Globalization.CultureInfo.InvariantCulture));
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }
        /// <summary>
        /// JSON 字符串还原——JsonEscape 的对称实现（标准八种转义 + uXXXX；未识别序列原样保留，不吞反斜杠）。
        /// 消费面：截断 / 损坏载荷的兜底还原；前端 chat-cmd.js cmdUnescapeJson 为同规则镜像（C# 与 JS 双实现须同步）。
        /// </summary>
        /// <param name="s">转义文本，可为 null</param>
        /// <returns>还原文本</returns>
        public static string JsonUnescape(string? s)
        {
            if (s == null || s.Length == 0 || s.IndexOf('\\') < 0)
            {
                return s ?? "";
            }
            StringBuilder sb = new StringBuilder(s.Length);
            int i = 0;
            while (i < s.Length)
            {
                char c = s[i];
                if (c != '\\' || i + 1 >= s.Length)
                {
                    sb.Append(c);
                    i = i + 1;
                    continue;
                }
                char n = s[i + 1];
                if (n == '"')
                {
                    sb.Append('"');
                    i = i + 2;
                }
                else if (n == '\\')
                {
                    sb.Append('\\');
                    i = i + 2;
                }
                else if (n == '/')
                {
                    sb.Append('/');
                    i = i + 2;
                }
                else if (n == 'b')
                {
                    sb.Append('\b');
                    i = i + 2;
                }
                else if (n == 'f')
                {
                    sb.Append('\f');
                    i = i + 2;
                }
                else if (n == 'n')
                {
                    sb.Append('\n');
                    i = i + 2;
                }
                else if (n == 'r')
                {
                    sb.Append('\r');
                    i = i + 2;
                }
                else if (n == 't')
                {
                    sb.Append('\t');
                    i = i + 2;
                }
                else if (n == 'u' && i + 5 < s.Length)
                {
                    int code;
                    if (int.TryParse(s.Substring(i + 2, 4), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out code))
                    {
                        sb.Append((char)code);
                        i = i + 6;
                    }
                    else
                    {
                        sb.Append(c);
                        i = i + 1;
                    }
                }
                else
                {
                    // [段1] 未识别序列——原样保留（与前端镜像口径一致）
                    sb.Append(c);
                    i = i + 1;
                }
            }
            return sb.ToString();
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
        /// <summary>实体解码的内容面豁免名单——这些参数承载「写什么存什么」的正文语义，解码会损坏内容（A133）</summary>
        private static readonly string[] EntityDecodeExemptArgs = new string[]
        {
                    "content", "body", "code", "codes", "value", "new", "findings", "cmd", "command",
                    "expression", "question", "text", "purpose", "push", "persona", "description", "note"
        };
        /// <summary>
        /// 是否内容面豁免参数——名单内不解码（一参数一判定，不按前缀打包）。
        /// </summary>
        /// <param name="name">参数名</param>
        /// <returns>true=豁免（原样保留）</returns>
        private static bool IsEntityDecodeExempt(string name)
        {
            if (name == null || name.Length == 0)
            {
                return false;
            }
            for (int i = 0; i < EntityDecodeExemptArgs.Length; i = i + 1)
            {
                if (name == EntityDecodeExemptArgs[i])
                {
                    return true;
                }
            }
            return false;
        }
        /// <summary>
        /// 工具参数实体解码（A133）——把模型侧误转义的 HTML/XML 实体还原为字符形态（JSON 对象级，逐属性处理）。
        /// 落点 = 会话参数面统一入口（全工具面一次覆盖——内置与 OA 工具同源）；豁免内容面参数（正文写什么存什么）。
        /// 逃生口：要表达字面量实体文本，写双写形态（一次解码得单写实体文本，详见 DecodeEntities）。
        /// 解码发生时出声（L2）——静默改写模型输入不可观测。
        /// </summary>
        /// <param name="toolName">工具名（日志用）</param>
        /// <param name="argsJson">参数 JSON（整包）</param>
        /// <returns>解码后的参数 JSON（无改动时原样返回同一引用）</returns>
        public static string DecodeArgEntities(string toolName, string argsJson)
        {
            if (argsJson == null || argsJson.Length == 0 || argsJson.IndexOf('&') < 0)
            {
                return argsJson ?? "";
            }
            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(argsJson);
            }
            catch (Exception)
            {
                // 参数 JSON 非法——原样透传（下游按 BAD_ARGS 出声，本入口不抢报）
                return argsJson;
            }
            string result = argsJson;
            using (doc)
            {
                JsonElement root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Object)
                {
                    // [段1] 判定变更属性——只收值面改动的字段
                    List<KeyValuePair<string, string>> changed = new List<KeyValuePair<string, string>>();
                    foreach (JsonProperty property in root.EnumerateObject())
                    {
                        if (property.Value.ValueKind != JsonValueKind.String)
                        {
                            continue;
                        }
                        if (IsEntityDecodeExempt(property.Name))
                        {
                            continue;
                        }
                        string original = property.Value.GetString() ?? "";
                        string decoded = DecodeEntities(original);
                        if (!string.Equals(original, decoded, StringComparison.Ordinal))
                        {
                            changed.Add(new KeyValuePair<string, string>(property.Name, decoded));
                        }
                    }
                    // [段2] 重建参数 JSON——未变更属性取原文本，保持字节级等价
                    if (changed.Count > 0)
                    {
                        List<(string Key, object? Value)> items = new List<(string Key, object? Value)>();
                        string names = "";
                        foreach (JsonProperty property in root.EnumerateObject())
                        {
                            string? replacement = null;
                            for (int i = 0; i < changed.Count; i = i + 1)
                            {
                                if (changed[i].Key == property.Name)
                                {
                                    replacement = changed[i].Value;
                                }
                            }
                            if (replacement != null)
                            {
                                items.Add((property.Name, replacement));
                            }
                            else
                            {
                                items.Add((property.Name, JsonUtil.Raw(property.Value.GetRawText())));
                            }
                        }
                        for (int i = 0; i < changed.Count; i = i + 1)
                        {
                            if (names.Length > 0)
                            {
                                names = names + ",";
                            }
                            names = names + changed[i].Key;
                        }
                        LogStore.Add("CatHome4", 2, "工具参数实体解码: " + toolName + " · 字段 " + names, "TOOL");
                        result = JsonUtil.Object(items.ToArray());
                    }
                }
            }
            return result;
        }
    }
}
