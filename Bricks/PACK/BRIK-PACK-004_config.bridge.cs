// ═══════════════════════════════════════════════════
// 积木: config.bridge
// ID:   BRIK-PACK-004
// 类别: PACK
// 作用: 配置工具桥接口积木——配置自改能力声明（PACK 类，config.* 积木统一经本桥访问）
// 依赖: 无
// 包: 无
// 引用: Mau.Runtime（ConfigStore/ConfigSchema/DataBox）
// 原理: DataBox.TryResolve<ConfigStore> + <ConfigSchema> → method 分派
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
            ConfigStore? cfg;
            DataBox.TryResolve<ConfigStore>(out cfg);
            if (cfg == null)
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
        /// list——配置全览（schema 全部条目 + 当前值 + 元信息；敏感键掩码）
        /// </summary>
        /// <param name="cfg">配置存储</param>
        /// <param name="schema">配置 schema</param>
        /// <param name="schemaBound">schema 是否绑定</param>
        /// <returns>清单文本</returns>
        private static string ListAll(ConfigStore cfg, ConfigSchema schema, bool schemaBound)
        {
            StringBuilder sb = new StringBuilder();
            if (!schemaBound || schema == null)
            {
                System.Collections.Generic.KeyValuePair<string, string>[] all = cfg.All();
                for (int i = 0; i < all.Length; i = i + 1)
                {
                    sb.Append(all[i].Key);
                    sb.Append('=');
                    sb.AppendLine(all[i].Value);
                }
                return TrimResult(sb.ToString(), 20000);
            }
            ConfigSchema.Item[] items = schema.All();
            for (int i = 0; i < items.Length; i = i + 1)
            {
                ConfigSchema.Item item = items[i];
                string value = cfg.Get(item.Key, item.Default);
                if (schema.IsSensitive(item.Key) && value.Length > 0)
                {
                    value = "****";
                }
                sb.Append(item.Key);
                sb.Append('=');
                sb.Append(value);
                sb.Append(" | 默认=");
                sb.Append(item.Default);
                sb.Append(" | ");
                if (item.Writable)
                {
                    sb.Append("可写");
                }
                else
                {
                    sb.Append("只读");
                }
                if (item.Type.Length > 0)
                {
                    sb.Append(" | 类型=");
                    sb.Append(item.Type);
                    if (item.Min.Length > 0)
                    {
                        sb.Append("[" + item.Min + "," + item.Max + "]");
                    }
                }
                sb.Append(" | ");
                sb.AppendLine(item.Desc);
            }
            return TrimResult(sb.ToString(), 20000);
        }

        /// <summary>
        /// get——单项查询
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
            if (schemaBound && schema != null)
            {
                ConfigSchema.Item? item = schema.Find(key);
                if (item != null)
                {
                    string value = cfg.Get(item.Key, item.Default);
                    if (schema.IsSensitive(item.Key) && value.Length > 0)
                    {
                        value = "****";
                    }
                    return key + "=" + value + " | 默认=" + item.Default + " | " + (item.Writable ? "可写" : "只读") + " | " + item.Desc;
                }
            }
            string direct;
            if (cfg.TryGet(key, out direct))
            {
                return key + "=" + direct;
            }
            return "ERR|NOT_FOUND|配置键不存在: " + key;
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
// #MAU_CHECKSUM:SHA256:1A0E531059D55FD86134D2CFBB030CFA50EFFD7066BE23B893DBBC1FB2399764
