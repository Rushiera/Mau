using System;
using System.Text.Json;

namespace Mau.Runtime
{
    /// <summary>
    /// 工具参数面统一入口（A138）——参数校验与取值的唯一实装。
    /// 收敛来源：20 件工具积木各自的 ValidateArgs / ExtractArg 样板（字符级重复，差异仅四个参数）。
    /// 口径与既有样板逐字一致——错误文本即工具返回面契约，不改写。
    /// </summary>
    public static class JsonArgs
    {
        /// <summary>
        /// 参数校验——未知键 / 必填缺值 / 枚举非法一律报错（参数面零容忍）；宿主注入保留键 catId 放行。
        /// </summary>
        /// <param name="argsJson">参数 JSON（空=无参工具）</param>
        /// <param name="allowed">允许键（空格分隔；空=无参数）</param>
        /// <param name="required">必填键（空格分隔；空=无必填）</param>
        /// <param name="enumName">枚举参数名（空=无）</param>
        /// <param name="enumValues">枚举合法值（| 分隔）</param>
        /// <returns>错误文本（空=通过）</returns>
        public static string Validate(string argsJson, string allowed, string required, string enumName, string enumValues)
        {
            if (argsJson == null || argsJson.Length == 0)
            {
                return "ERR|BAD_ARGS|工具参数 JSON 解析失败（LLM 生成参数可能被截断——超长内容请分段写入）";
            }
            string parseError;
            JsonDocument? doc = JsonUtil.Parse(argsJson, "工具参数", out parseError);
            if (doc == null)
            {
                return "ERR|BAD_ARGS|参数 JSON 解析失败: " + parseError;
            }
            using (doc)
            {
                JsonElement root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    return "ERR|BAD_ARGS|参数必须是 JSON 对象";
                }
                // [段1] 未知键拦截——catId 为宿主注入保留键
                foreach (JsonProperty property in root.EnumerateObject())
                {
                    if (property.Name == "catId")
                    {
                        continue;
                    }
                    if ((" " + allowed + " ").IndexOf(" " + property.Name + " ", StringComparison.Ordinal) < 0)
                    {
                        return "ERR|BAD_ARGS|未知参数: " + property.Name + "（支持 " + allowed + "）";
                    }
                }
                // [段2] 必填校验——字符串空值视同缺失；required 空 = 无必填（跳过：原样板对空 required 会 Split 出空键并误报「缺参数 」）
                if (required.Length > 0)
                {
                    string[] must = required.Split(' ');
                    for (int i = 0; i < must.Length; i = i + 1)
                    {
                        JsonElement mustValue;
                        if (!root.TryGetProperty(must[i], out mustValue) ||
                            (mustValue.ValueKind == JsonValueKind.String && (mustValue.GetString() ?? "").Length == 0))
                        {
                            return "ERR|BAD_ARGS|缺参数 " + must[i] + "（必填：" + required + "）";
                        }
                    }
                }
                // [段3] 枚举校验——空值放行（缺省语义）
                if (enumName.Length > 0)
                {
                    JsonElement enumValue;
                    if (root.TryGetProperty(enumName, out enumValue) && enumValue.ValueKind == JsonValueKind.String)
                    {
                        string value = enumValue.GetString() ?? "";
                        if (value.Length > 0 && ("|" + enumValues + "|").IndexOf("|" + value + "|", StringComparison.Ordinal) < 0)
                        {
                            return "ERR|BAD_ARGS|" + enumName + " 非法值: " + value + "（" + enumValues + "）";
                        }
                    }
                }
                return "";
            }
        }

        /// <summary>
        /// 展平参数提取——取字符串值（不存在返回空串）；非字符串返回其 JSON 原文；解析失败返回 §PARSE_FAIL§。
        /// </summary>
        /// <param name="argsJson">参数 JSON</param>
        /// <param name="key">参数名</param>
        /// <returns>参数值</returns>
        public static string Get(string argsJson, string key)
        {
            if (argsJson == null || argsJson.Length == 0)
            {
                return "§PARSE_FAIL§";
            }
            string parseError;
            JsonDocument? doc = JsonUtil.Parse(argsJson, "工具参数", out parseError);
            if (doc == null)
            {
                return "§PARSE_FAIL§";
            }
            using (doc)
            {
                if (doc.RootElement.TryGetProperty(key, out JsonElement el))
                {
                    if (el.ValueKind == JsonValueKind.String)
                    {
                        return el.GetString() ?? "";
                    }
                    return el.GetRawText();
                }
            }
            return "";
        }
    }
}
