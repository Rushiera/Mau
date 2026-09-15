// ═══════════════════════════════════════════════════
// 积木: config.bridge
// ID:   BRIK-PACK-004
// 类别: PACK
// 作用: 配置工具桥接口积木——配置自改能力声明（PACK 类，config.* 积木统一经本桥访问）
// 依赖: 无
// 包: 无
// 引用: Mau.Runtime（ConfigStore/ConfigSchema/DataBox）
// 原理: DataBox.TryResolve<ConfigStore>（全局 store——与配置面 HTTP 同源，§4.4）+ DataBox.TryResolve<ConfigSchema> → method 分派
//       读面走 ConfigEffective 单一出口（source/空值语义/未声明标注——§4.3/§4.5）
//       写入唯一实现 = ConfigStore.SetChecked/ResetToDefault（schema 白名单 + 值域校验 + 原子写回滚）
// 方法: list → （无参）
//        get → key
//        set → key,value
//        reset → key（空=全群）
// 常用: config.* 四工具的调度底座（P8.5d——配置自改最小闭环；llm.* 私密环境变量只读）
// ═══════════════════════════════════════════════════
using System;
using System.Text;
using System.Text.Json;
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// PACK 接口积木——config.bridge（调度 ConfigStore/ConfigSchema）
    /// </summary>
    public static class ConfigBridgeBrick
    {
        /// <summary>
        /// 配置工具桥单方法调度——method 白名单 + argsJson 展平参数（list/get/set/reset）
        /// </summary>
        /// <param name="method">操作名（list/get/set/reset）</param>
        /// <param name="argsJson">展平参数 JSON</param>
        /// <param name="result">结果文本</param>
        /// <returns>true=调用成功</returns>
        public static bool Invoke(string method, string argsJson, out string result)
        {
            result = "";
            // [参数面] 声明面口径零容忍——未知 / 缺值一律 ERR|BAD_ARGS（catId 保留键放行）
            string allowedKeys;
            string requiredKeys;
            if (method == "list")
            {
                allowedKeys = "";
                requiredKeys = "";
            }
            else if (method == "get")
            {
                allowedKeys = "key";
                requiredKeys = "key";
            }
            else if (method == "set")
            {
                allowedKeys = "key value";
                requiredKeys = "key value";
            }
            else if (method == "reset")
            {
                allowedKeys = "key";
                requiredKeys = "";
            }
            else
            {
                result = "ERR|UNKNOWN_METHOD|config." + method;
                return false;
            }
            string badArgs = ValidateArgs(argsJson, allowedKeys, requiredKeys, "", "");
            if (badArgs.Length > 0)
            {
                result = badArgs;
                return false;
            }
            // 全局 store 单一真相——config-* 与配置面（HTTP /api/v1/config）同源（§4.4：per-cat 路由已退役）
            ConfigStore cfg = null!;
            bool cfgBound = DataBox.TryResolve<ConfigStore>(out cfg);
            if (!cfgBound || cfg == null)
            {
                result = "ERR|CONFIG_NO_STORE|宿主未注入 ConfigStore";
                return false;
            }
            ConfigSchema? schema;
            bool schemaBound = DataBox.TryResolve<ConfigSchema>(out schema);
            try
            {
                if (method == "list")
                {
                    result = ListAll(cfg, schema, schemaBound);
                    return true;
                }
                if (method == "get")
                {
                    result = GetOne(cfg, schema, schemaBound, ExtractArg(argsJson, "key"));
                    return true;
                }
                if (method == "set")
                {
                    result = SetOne(cfg, schema, schemaBound, ExtractArg(argsJson, "key"), ExtractArg(argsJson, "value"));
                    return true;
                }
                if (method == "reset")
                {
                    result = ResetOne(cfg, schema, schemaBound, ExtractArg(argsJson, "key"));
                    return true;
                }
                result = "ERR|UNKNOWN_METHOD|config." + method;
                return false;
            }
            catch (Exception ex)
            {
                result = "ERR|" + ex.GetType().Name + "|" + ex.Message;
                return false;
            }
        }

        /// <summary>
        /// list——配置全览（读面单一出口 ConfigEffective：schema 声明全项 + 未声明落盘键；敏感键掩码）
        /// 输出：key=值 | 来源=file·env·default | 默认=… | 可写·只读 | 空值语义 | 类型=[min,max] | 描述
        /// </summary>
        /// <param name="cfg">配置存储</param>
        /// <param name="schema">配置 schema</param>
        /// <param name="schemaBound">schema 是否绑定</param>
        /// <returns>清单文本</returns>
        private static string ListAll(ConfigStore cfg, ConfigSchema schema, bool schemaBound)
        {
            ConfigSchema? readSchema = null;
            if (schemaBound)
            {
                readSchema = schema;
            }
            ConfigEffective.Entry[] items = ConfigEffective.BuildAll(cfg, readSchema);
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < items.Length; i = i + 1)
            {
                ConfigEffective.Entry entry = items[i];
                string value = entry.Value;
                if (entry.Sensitive && value.Length > 0)
                {
                    value = "****";
                }
                sb.Append(entry.Key);
                sb.Append('=');
                sb.Append(value);
                sb.Append(" | 来源=");
                sb.Append(entry.Source);
                if (entry.Declared)
                {
                    sb.Append(" | 默认=");
                    sb.Append(entry.Default);
                    sb.Append(" | ");
                    if (entry.Writable)
                    {
                        sb.Append("可写");
                    }
                    else
                    {
                        sb.Append("只读");
                    }
                    if (entry.EmptyDesc.Length > 0)
                    {
                        sb.Append(" | ");
                        sb.Append(entry.EmptyDesc);
                    }
                    if (entry.Type.Length > 0)
                    {
                        sb.Append(" | 类型=");
                        sb.Append(entry.Type);
                        if (entry.Min.Length > 0)
                        {
                            sb.Append("[" + entry.Min + "," + entry.Max + "]");
                        }
                    }
                }
                sb.Append(" | ");
                sb.AppendLine(entry.Desc);
            }
            return TrimResult(sb.ToString(), 20000);
        }

        /// <summary>
        /// get——单项查询（读面单一出口 ConfigEffective——含 source 与未声明标注）
        /// 未声明且未落盘 → ERR|NOT_FOUND；未声明但落盘 → 输出 + 「未在 schema 声明（只读…）」
        /// </summary>
        /// <param name="cfg">配置存储</param>
        /// <param name="schema">配置 schema</param>
        /// <param name="schemaBound">schema 是否绑定</param>
        /// <param name="key">配置键</param>
        /// <returns>单项文本</returns>
        private static string GetOne(ConfigStore cfg, ConfigSchema schema, bool schemaBound, string key)
        {
            if (key == null || key.Length == 0)
            {
                return "ERR|BAD_ARGS|缺少参数 key";
            }
            ConfigSchema? readSchema = null;
            if (schemaBound)
            {
                readSchema = schema;
            }
            ConfigEffective.Entry entry = ConfigEffective.Resolve(cfg, readSchema, key);
            if (!entry.Declared && entry.Source.Length == 0)
            {
                return "ERR|NOT_FOUND|配置键不存在: " + key;
            }
            string value = entry.Value;
            if (entry.Sensitive && value.Length > 0)
            {
                value = "****";
            }
            StringBuilder sb = new StringBuilder();
            sb.Append(key);
            sb.Append('=');
            sb.Append(value);
            sb.Append(" | 来源=");
            sb.Append(entry.Source);
            if (entry.Declared)
            {
                sb.Append(" | 默认=");
                sb.Append(entry.Default);
                sb.Append(" | ");
                if (entry.Writable)
                {
                    sb.Append("可写");
                }
                else
                {
                    sb.Append("只读");
                }
                if (entry.EmptyDesc.Length > 0)
                {
                    sb.Append(" | ");
                    sb.Append(entry.EmptyDesc);
                }
            }
            sb.Append(" | ");
            sb.Append(entry.Desc);
            return sb.ToString();
        }

        /// <summary>
        /// set——白名单写（唯一实现 ConfigStore.SetChecked）
        /// </summary>
        /// <param name="cfg">配置存储</param>
        /// <param name="schema">配置 schema</param>
        /// <param name="schemaBound">schema 是否绑定</param>
        /// <param name="key">配置键</param>
        /// <param name="value">新值</param>
        /// <returns>确认文本</returns>
        private static string SetOne(ConfigStore cfg, ConfigSchema schema, bool schemaBound, string key, string value)
        {
            if (key == null || key.Length == 0)
            {
                return "ERR|BAD_ARGS|缺少参数 key";
            }
            string error;
            if (!cfg.SetChecked(key, value, schemaBound ? schema : null, out error))
            {
                return "ERR|CONFIG_REJECT|" + error;
            }
            bool sensitive = schemaBound && schema != null && schema.IsSensitive(key);
            return "OK 配置已更新: " + key + "=" + (sensitive ? "****" : value);
        }

        /// <summary>
        /// reset——还原默认（key 空=全群 writable；写 schema default 落盘）
        /// </summary>
        /// <param name="cfg">配置存储</param>
        /// <param name="schema">配置 schema</param>
        /// <param name="schemaBound">schema 是否绑定</param>
        /// <param name="key">配置键（空=全群）</param>
        /// <returns>确认文本</returns>
        private static string ResetOne(ConfigStore cfg, ConfigSchema schema, bool schemaBound, string key)
        {
            string error;
            if (!cfg.ResetToDefault(key, schemaBound ? schema : null, out error))
            {
                return "ERR|CONFIG_REJECT|" + error;
            }
            if (key == null || key.Length == 0)
            {
                return "OK 已还原默认: 全部可写配置项";
            }
            return "OK 已还原默认: " + key;
        }

        /// <summary>
        /// 展平参数提取——argsJson 中取字符串值（不存在返回空串）
        /// </summary>
        /// <param name="argumentsJson">参数 JSON</param>
        /// <param name="key">参数名</param>
        /// <returns>参数值</returns>
        /// <summary>
        /// 参数面校验——声明面口径零容忍：未知参数 / 必填缺值 / 非法枚举值一律 ERR|BAD_ARGS（宿主注入保留键 catId 放行）。
        /// </summary>
        /// <param name="argsJson">工具参数 JSON</param>
        /// <param name="allowed">允许键（空格分隔）</param>
        /// <param name="required">必填键（空格分隔；空=无必填）</param>
        /// <param name="enumName">枚举参数名（空=无）</param>
        /// <param name="enumValues">枚举合法值（| 分隔）</param>
        /// <returns>错误文本（空=通过）</returns>
        private static string ValidateArgs(string argsJson, string allowed, string required, string enumName, string enumValues)
        {
            if (argsJson == null || argsJson.Length == 0)
            {
                return "ERR|BAD_ARGS|工具参数 JSON 解析失败（LLM 生成参数可能被截断——超长内容请分段写入）";
            }
            try
            {
                JsonDocument doc = JsonDocument.Parse(argsJson);
                try
                {
                    JsonElement root = doc.RootElement;
                    if (root.ValueKind != JsonValueKind.Object)
                    {
                        return "ERR|BAD_ARGS|参数必须是 JSON 对象";
                    }
                    foreach (JsonProperty property in root.EnumerateObject())
                    {
                        if (property.Name == "catId")
                        {
                            continue;
                        }
                        if (allowed.Length > 0 && (" " + allowed + " ").IndexOf(" " + property.Name + " ", StringComparison.Ordinal) < 0)
                        {
                            return "ERR|BAD_ARGS|未知参数: " + property.Name + "（支持 " + allowed + "）";
                        }
                        if (allowed.Length == 0)
                        {
                            return "ERR|BAD_ARGS|未知参数: " + property.Name + "（本操作无参数）";
                        }
                    }
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
                finally
                {
                    doc.Dispose();
                }
            }
            catch (Exception ex)
            {
                return "ERR|BAD_ARGS|参数 JSON 解析失败: " + ex.Message;
            }
        }

        private static string ExtractArg(string argumentsJson, string key)
        {
            try
            {
                JsonDocument doc = JsonDocument.Parse(argumentsJson);
                try
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
                finally
                {
                    doc.Dispose();
                }
            }
            catch (Exception)
            {
            }
            return "";
        }

        /// <summary>
        /// 结果截断——超长文本保留头部 + 截断提示（上下文防爆）
        /// </summary>
        /// <param name="text">原文</param>
        /// <param name="max">上限字符数</param>
        /// <returns>截断文本</returns>
        private static string TrimResult(string text, int max)
        {
            if (text == null || text.Length <= max)
            {
                return text ?? "";
            }
            return text.Substring(0, max) + "\n…[截断: 共 " + text.Length.ToString() + " 字符，仅保留前 " + max.ToString() + "]";
        }
    }
}
// #MAU_CHECKSUM:SHA256:ACF54AC0172B06B272FE395D72F4F7B63C5E45A10E3D83039C0EF504DD2F1ABD
