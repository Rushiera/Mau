using System.Text.Json;

namespace Mau.Runtime
{
    /// <summary>
    /// 工具载荷辅助——tool_calls 参数展平与 JSON 读取（tool.* 积木共享，程序级）
    /// </summary>
    public static class ToolPayload
    {
        /// <summary>
        /// 展平 arguments 对象为 args.&lt;名&gt; Key 序列
        /// </summary>
        /// <param name="oa">OA 实例</param>
        /// <param name="officeId">Office ID</param>
        /// <param name="dogId">所有者 LongId</param>
        /// <param name="argObj">arguments JSON 对象</param>
        public static void FlattenArgs(IOA oa, long officeId, long dogId,
            JsonElement argObj)
        {
            if (argObj.ValueKind != JsonValueKind.Object)
            {
                return;
            }
            foreach (JsonProperty prop in argObj.EnumerateObject())
            {
                string key = "args." + prop.Name;
                if (prop.Value.ValueKind == JsonValueKind.String)
                {
                    oa.SetStr(officeId, dogId, key, prop.Value.GetString() ?? "");
                }
                else
                {
                    oa.SetStr(officeId, dogId, key, prop.Value.GetRawText());
                }
            }
        }

        /// <summary>
        /// 读取对象内的可选字符串属性
        /// </summary>
        /// <param name="element">JSON 对象</param>
        /// <param name="name">属性名</param>
        /// <returns>字符串或空串</returns>
        public static string ReadString(JsonElement element, string name)
        {
            JsonElement value;
            if (element.TryGetProperty(name, out value)
                && value.ValueKind == JsonValueKind.String)
            {
                return BrickText.SafeText(value.GetString());
            }
            return "";
        }
    }
}
