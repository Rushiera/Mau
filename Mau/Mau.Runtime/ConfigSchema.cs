using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Mau.Runtime
{
    /// <summary>
    /// 配置群 schema——schema.json 元声明装载（P8.5 配置群规范 design-ch4-workspace §四）。
    /// 每项声明：键 / 文件归属 / 默认值 / 敏感 / 可写 / 描述——配置面唯一契约。
    /// 当前消费：/api/v1/config GET 输出面（default/writable/desc 补全——LLM 可读）；P8.5d config.* 写通道按此声明放权。
    /// </summary>
    public sealed class ConfigSchema
    {
        /// <summary>
        /// schema 条目——配置项元声明
        /// </summary>
        public sealed class Item
        {
            /// <summary>
            /// 配置键（带文件前缀寻址：llm.api_key / app.frame_ms）
            /// </summary>
            public string Key = "";

            /// <summary>
            /// 文件归属——llm.cfg / app.json / workspace.json
            /// </summary>
            public string File = "";

            /// <summary>
            /// 默认值——缺省空串
            /// </summary>
            public string Default = "";

            /// <summary>
            /// 敏感标志——true 时所有输出面掩码（api_key/secret/token 系）
            /// </summary>
            public bool Sensitive;

            /// <summary>
            /// 可写标志——false 时写通道拒绝（roots 等结构项只读）
            /// </summary>
            public bool Writable;

            /// <summary>
            /// 描述——一行 ≤40 字
            /// </summary>
            public string Desc = "";
            /// <summary>
            /// 值类型——"string"（缺省）/ "int" / "bool"；P8.5d 值域校验
            /// </summary>
            public string Type = "";
            /// <summary>
            /// 数值下限（type=int 时生效）
            /// </summary>
            public string Min = "";
            /// <summary>
            /// 数值上限（type=int 时生效）
            /// </summary>
            public string Max = "";

            /// <summary>
            /// 枚举值域——type=string/空 且非空时生效（写通道只接受其中之一；空=不限制）
            /// </summary>
            public string[] Values = new string[0];
        }

        /// <summary>
        /// schema 条目表——按键查询（Ordinal）
        /// </summary>
        private readonly Dictionary<string, Item> _items;

        /// <summary>
        /// 建立空 schema
        /// </summary>
        public ConfigSchema()
        {
            _items = new Dictionary<string, Item>(StringComparer.Ordinal);
        }

        // [段1] 装载
        /// <summary>
        /// 从 schema.json 装载——文件缺失返回空 schema（兼容无配置启动）；损坏抛异常（配置了不静默）
        /// </summary>
        /// <param name="path">schema.json 路径</param>
        /// <returns>配置 schema</returns>
        public static ConfigSchema Load(string path)
        {
            ConfigSchema schema = new ConfigSchema();
            if (path == null || path.Length == 0 || !File.Exists(path))
            {
                return schema;
            }
            using (JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path)))
            {
                JsonElement root = doc.RootElement;
                JsonElement itemsEl;
                if (!root.TryGetProperty("items", out itemsEl) || itemsEl.ValueKind != JsonValueKind.Array)
                {
                    return schema;
                }
                for (int i = 0; i < itemsEl.GetArrayLength(); i++)
                {
                    JsonElement el = itemsEl[i];
                    string key = GetProp(el, "key");
                    if (key.Length == 0)
                    {
                        continue;
                    }
                    Item item = new Item();
                    item.Key = key;
                    item.File = GetProp(el, "file");
                    item.Default = GetProp(el, "default");
                    item.Desc = GetProp(el, "desc");
                    item.Sensitive = GetBoolProp(el, "sensitive", false);
                    item.Writable = GetBoolProp(el, "writable", true);
                    item.Type = GetProp(el, "type");
                    item.Min = GetProp(el, "min");
                    item.Max = GetProp(el, "max");
                    JsonElement valuesEl;
                    if (el.TryGetProperty("values", out valuesEl) && valuesEl.ValueKind == JsonValueKind.Array)
                    {
                        List<string> values = new List<string>();
                        for (int v = 0; v < valuesEl.GetArrayLength(); v = v + 1)
                        {
                            JsonElement valueEl = valuesEl[v];
                            if (valueEl.ValueKind == JsonValueKind.String)
                            {
                                string? got = valueEl.GetString();
                                if (got != null && got.Length > 0)
                                {
                                    values.Add(got);
                                }
                            }
                        }
                        item.Values = values.ToArray();
                    }
                    schema._items[key] = item;
                }
            }
            return schema;
        }

        // [段2] 查询
        /// <summary>
        /// 全部条目快照——按装载顺序（键组序）
        /// </summary>
        /// <returns>条目数组</returns>
        public Item[] All()
        {
            Item[] copy = new Item[_items.Count];
            int i = 0;
            foreach (KeyValuePair<string, Item> pair in _items)
            {
                copy[i] = pair.Value;
                i = i + 1;
            }
            return copy;
        }

        /// <summary>
        /// 按键查条目——不存在返回 null
        /// </summary>
        /// <param name="key">配置键</param>
        /// <returns>条目或 null</returns>
        public Item? Find(string key)
        {
            Item? found;
            if (_items.TryGetValue(key, out found))
            {
                return found;
            }
            return null;
        }

        /// <summary>
        /// 敏感判定——schema 声明优先；未声明时键名子串兜底（api_key/secret/token 系）
        /// </summary>
        /// <param name="key">配置键</param>
        /// <returns>是否敏感</returns>
        public bool IsSensitive(string key)
        {
            Item? item = Find(key);
            if (item != null)
            {
                return item.Sensitive;
            }
            return key.IndexOf("api_key", StringComparison.OrdinalIgnoreCase) >= 0
                || key.IndexOf("secret", StringComparison.OrdinalIgnoreCase) >= 0
                || key.IndexOf("token", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // [段3] 辅助
        /// <summary>
        /// 读取字符串属性——缺字段返回空串（防御式）
        /// </summary>
        /// <param name="obj">JSON 元素</param>
        /// <param name="prop">属性名</param>
        /// <returns>属性值</returns>
        private static string GetProp(JsonElement obj, string prop)
        {
            JsonElement value;
            if (obj.TryGetProperty(prop, out value) && value.ValueKind == JsonValueKind.String)
            {
                string? got = value.GetString();
                if (got != null)
                {
                    return got;
                }
            }
            return "";
        }

        /// <summary>
        /// 读取布尔属性——缺字段返回默认值（防御式）
        /// </summary>
        /// <param name="obj">JSON 元素</param>
        /// <param name="prop">属性名</param>
        /// <param name="defaultValue">缺省值</param>
        /// <returns>布尔值</returns>
        private static bool GetBoolProp(JsonElement obj, string prop, bool defaultValue)
        {
            JsonElement value;
            if (obj.TryGetProperty(prop, out value) && (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False))
            {
                return value.GetBoolean();
            }
            return defaultValue;
        }
        /// <summary>
        /// 枚举值域命中判定——忽略大小写（声明 values 与候选值逐项比较）
        /// </summary>
        /// <param name="values">声明值域</param>
        /// <param name="value">候选值</param>
        /// <returns>是否命中</returns>
        private static bool ContainsValue(string[] values, string value)
        {
            for (int i = 0; i < values.Length; i = i + 1)
            {
                if (string.Equals(values[i], value, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 值域校验——P8.5d 写入通道（SetChecked）前置校验。
        /// 规则：值长度上限 1024；type=int → 整数解析 + min/max 范围；type=bool → true/false/1/0；type=string/空 → 放行。
        /// </summary>
        /// <param name="key">配置键</param>
        /// <param name="value">候选值</param>
        /// <param name="error">失败原因（成功为空串）</param>
        /// <returns>是否通过</returns>
        public bool Validate(string key, string value, out string error)
        {
            error = "";
            Item? item = Find(key);
            if (item == null)
            {
                error = "配置键未在 schema 声明: " + key;
                return false;
            }

            if (value.Length > 1024)
            {
                error = "配置值过长（>1024）";
                return false;
            }

            string type = item.Type;
            if (type.Length == 0 || type == "string")
            {
                // 枚举值域——声明非空时只接受其中之一（忽略大小写；值原样落盘，消费端归一）
                if (item.Values.Length > 0 && !ContainsValue(item.Values, value))
                {
                    error = "配置项 " + key + " 只接受以下值之一: " + string.Join(" / ", item.Values);
                    return false;
                }

                return true;
            }

            if (type == "int")
            {
                long parsed;
                if (!long.TryParse(value, out parsed))
                {
                    error = "配置项 " + key + " 需要整数: " + value;
                    return false;
                }

                if (item.Min.Length > 0)
                {
                    long min;
                    if (long.TryParse(item.Min, out min) && parsed < min)
                    {
                        error = "配置项 " + key + " 低于下限 " + item.Min;
                        return false;
                    }
                }

                if (item.Max.Length > 0)
                {
                    long max;
                    if (long.TryParse(item.Max, out max) && parsed > max)
                    {
                        error = "配置项 " + key + " 超出上限 " + item.Max;
                        return false;
                    }
                }

                return true;
            }

            if (type == "bool")
            {
                if (value != "true" && value != "false" && value != "1" && value != "0")
                {
                    error = "配置项 " + key + " 需要布尔值: " + value;
                    return false;
                }

                return true;
            }

            error = "未知值类型: " + type;
            return false;
        }
    }
}