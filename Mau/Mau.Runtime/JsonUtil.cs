using System;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Mau.Runtime
{
    /// <summary>
    /// 统一 JSON 序列化入口——中文直显（UnsafeRelaxedJsonEscaping：非 ASCII 不转义；HTML 敏感字符亦不转义——前端渲染走 textContent/escapeHtml，无注入面）。
    /// 全局替换 JsonSerializer.Serialize 裸调用（2026-09-08：cfg/json/SSE 中文 \uXXXX 转义问题——Q2 全局治本）。
    /// 特例：需要自定义 options 的调用（IncludeFields 等）保持 JsonSerializer 原样。
    /// </summary>
    public static class JsonUtil
    {
        /// <summary>共享序列化选项——中文直显（其余默认）</summary>
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        /// <summary>
        /// 序列化为 JSON 字符串——中文直显
        /// </summary>
        /// <param name="value">待序列化对象</param>
        /// <returns>JSON 文本</returns>
        public static string Serialize<T>(T value)
        {
            return JsonSerializer.Serialize(value, Options);
        }
        /// <summary>
        /// JSON 字符串字面量——转义 + 双引号包裹（值面唯一入口：手写拼装一律引用此处，不自行转义）。
        /// </summary>
        /// <param name="value">原值，可为 null（→ 空串字面量）</param>
        /// <returns>带引号的 JSON 字符串字面量</returns>
        public static string Str(string? value)
        {
            return "\"" + TextUtil.JsonEscape(value) + "\"";
        }
        /// <summary>
        /// JSON 单值——按运行时类型分派：string → Str；char → 单字符字面量；bool / 数值 → 直出（数值走 InvariantCulture）；null → null；其余 → Serialize。
        /// </summary>
        /// <param name="value">值对象</param>
        /// <returns>JSON 片段</returns>
        public static string Scalar(object? value)
        {
            if (value == null)
            {
                return "null";
            }
            JsonFragment? fragment = value as JsonFragment;
            if (fragment != null)
            {
                return fragment.Json;
            }
            string? text = value as string;
            if (text != null)
            {
                return Str(text);
            }
            if (value is char)
            {
                return Str(value.ToString());
            }
            if (value is bool)
            {
                if ((bool)value)
                {
                    return "true";
                }
                return "false";
            }
            if (IsNumber(value))
            {
                string? num = System.Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
                if (num == null)
                {
                    return "null";
                }
                return num;
            }
            return Serialize(value);
        }
        /// <summary>
        /// 数值类型判定——数值直出 JSON（其余走序列化，避免 DateTime 等被当数值文本输出）。
        /// </summary>
        /// <param name="value">值对象</param>
        /// <returns>true=数值类型</returns>
        private static bool IsNumber(object value)
        {
            return value is int || value is long || value is short || value is byte || value is sbyte
                || value is uint || value is ulong || value is ushort || value is float || value is double || value is decimal;
        }
        /// <summary>
        /// JSON 对象字面量——键恒走 Str 转义，值走 Scalar 分派。
        /// 边界：值须是标量或可序列化对象；Object / Array 产出的片段要嵌入为值时不走此路（会被当字符串转义）——直接手写该段骨架并用 Str 填值。
        /// </summary>
        /// <param name="fields">字段序（键 + 值）</param>
        /// <returns>JSON 对象文本</returns>
        public static string Object(params (string Key, object? Value)[] fields)
        {
            if (fields == null || fields.Length == 0)
            {
                return "{}";
            }
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.Append("{");
            for (int i = 0; i < fields.Length; i = i + 1)
            {
                if (i > 0)
                {
                    sb.Append(",");
                }
                sb.Append(Str(fields[i].Key));
                sb.Append(":");
                sb.Append(Scalar(fields[i].Value));
            }
            sb.Append("}");
            return sb.ToString();
        }
        /// <summary>
        /// JSON 数组字面量——元素走 Scalar 分派（边界同 Object）。
        /// </summary>
        /// <param name="items">元素序</param>
        /// <returns>JSON 数组文本</returns>
        public static string Array(params object?[] items)
        {
            if (items == null || items.Length == 0)
            {
                return "[]";
            }
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.Append("[");
            for (int i = 0; i < items.Length; i = i + 1)
            {
                if (i > 0)
                {
                    sb.Append(",");
                }
                sb.Append(Scalar(items[i]));
            }
            sb.Append("]");
            return sb.ToString();
        }
        /// <summary>
        /// 原始 JSON 片段包装——值位要嵌入「已合法 JSON」时使用（Scalar 识别后直出，不再转义）。
        /// 边界：文本必须是合法 JSON；要表达字符串请走 Str / Scalar（本方法不做校验——类型本身就是承诺）。
        /// </summary>
        /// <param name="json">JSON 片段（对象 / 数组 / 字面量）</param>
        /// <returns>片段包装对象（供 Object / Array 的值位使用）</returns>
        public static JsonFragment Raw(string json)
        {
            return new JsonFragment(json);
        }
        /// <summary>
        /// JSON 解析统一入口——失败返回 null 并出声（L2），不抛异常、不静默回落。
        /// </summary>
        /// <param name="text">JSON 文本（空 → null，不出声）</param>
        /// <param name="source">来源标签（日志出声用；空=通用标签）</param>
        /// <param name="error">失败原因（成功=空串）</param>
        /// <returns>JsonDocument（调用方负责 Dispose）；失败 null</returns>
        public static JsonDocument? Parse(string? text, string source, out string error)
        {
            error = "";
            if (text == null || text.Length == 0)
            {
                return null;
            }
            try
            {
                return JsonDocument.Parse(text);
            }
            catch (Exception ex)
            {
                error = ex.Message;
                string tag = source.Length > 0 ? source : "JSON";
                LogStore.Add("Mau", 2, "JSON 解析失败: " + tag + " | " + ex.Message, "JSON");
                return null;
            }
        }
        /// <summary>
        /// JSON 解析统一入口（丢弃失败原因）——失败返回 null 并出声（L2）。
        /// </summary>
        /// <param name="text">JSON 文本（空 → null，不出声）</param>
        /// <param name="source">来源标签（日志出声用；空=通用标签）</param>
        /// <returns>JsonDocument（调用方负责 Dispose）；失败 null</returns>
        public static JsonDocument? Parse(string? text, string source)
        {
            string error;
            return Parse(text, source, out error);
        }
        /// <summary>
        /// 取字符串属性——字符串返回值；null 属性 / 缺失返回 fallback；其余类型返回其 JSON 原文。
        /// </summary>
        /// <param name="obj">JSON 对象</param>
        /// <param name="name">属性名</param>
        /// <param name="fallback">缺失兜底值（读面默认值，不进落盘）</param>
        /// <returns>属性值</returns>
        public static string GetStr(JsonElement obj, string name, string fallback)
        {
            JsonElement value;
            if (obj.ValueKind != JsonValueKind.Object || !obj.TryGetProperty(name, out value))
            {
                return fallback;
            }
            if (value.ValueKind == JsonValueKind.String)
            {
                return value.GetString() ?? "";
            }
            if (value.ValueKind == JsonValueKind.Null)
            {
                return fallback;
            }
            return value.GetRawText();
        }
        /// <summary>
        /// 取整型属性——数值返回值；缺失 / 非数值返回 fallback。
        /// </summary>
        /// <param name="obj">JSON 对象</param>
        /// <param name="name">属性名</param>
        /// <param name="fallback">缺失兜底值</param>
        /// <returns>属性值</returns>
        public static int GetInt(JsonElement obj, string name, int fallback)
        {
            JsonElement value;
            if (obj.ValueKind != JsonValueKind.Object || !obj.TryGetProperty(name, out value))
            {
                return fallback;
            }
            int parsed;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out parsed))
            {
                return parsed;
            }
            return fallback;
        }
        /// <summary>
        /// 取布尔属性——布尔返回值；缺失 / 非布尔返回 fallback。
        /// </summary>
        /// <param name="obj">JSON 对象</param>
        /// <param name="name">属性名</param>
        /// <param name="fallback">缺失兜底值</param>
        /// <returns>属性值</returns>
        public static bool GetBool(JsonElement obj, string name, bool fallback)
        {
            JsonElement value;
            if (obj.ValueKind != JsonValueKind.Object || !obj.TryGetProperty(name, out value))
            {
                return fallback;
            }
            if (value.ValueKind == JsonValueKind.True)
            {
                return true;
            }
            if (value.ValueKind == JsonValueKind.False)
            {
                return false;
            }
            return fallback;
        }
        /// <summary>
        /// 片段数组——把一组「已是合法 JSON 的片段」拼成数组（逐元素直出，不转义）。
        /// 用途：循环聚合列表（每项由 Object 构造）后整体嵌入父结构。
        /// </summary>
        /// <param name="fragments">片段序列（各元素须为合法 JSON）</param>
        /// <returns>数组片段（供值位使用）</returns>
        public static JsonFragment RawArray(params string[] fragments)
        {
            if (fragments == null || fragments.Length == 0)
            {
                return new JsonFragment("[]");
            }
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.Append("[");
            for (int i = 0; i < fragments.Length; i = i + 1)
            {
                if (i > 0)
                {
                    sb.Append(",");
                }
                sb.Append(fragments[i]);
            }
            sb.Append("]");
            return new JsonFragment(sb.ToString());
        }
        /// <summary>
        /// JSON 解析（严格）——失败抛 InvalidDataException 并出声（L2）。
        /// 用途：调用方已有 try / catch 处置面（保持原控制流不变）——与 Parse 同一实装、同一出声口径。
        /// </summary>
        /// <param name="text">JSON 文本</param>
        /// <param name="source">来源标签（日志出声用；空=通用标签）</param>
        /// <returns>JsonDocument（调用方负责 Dispose）</returns>
        public static JsonDocument ParseStrict(string text, string source)
        {
            string error;
            JsonDocument? doc = Parse(text, source, out error);
            if (doc == null)
            {
                throw new System.IO.InvalidDataException("JSON 解析失败: " + error);
            }
            return doc;
        }
        /// <summary>
        /// JSON 解析（严格·通用来源标签）——失败抛 InvalidDataException 并出声（L2）。
        /// </summary>
        /// <param name="text">JSON 文本</param>
        /// <returns>JsonDocument（调用方负责 Dispose）</returns>
        public static JsonDocument ParseStrict(string text)
        {
            return ParseStrict(text, "");
        }
    }
}
