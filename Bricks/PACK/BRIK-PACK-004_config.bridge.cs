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
using System.IO;
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
                allowedKeys = "key value declare desc";
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
            string badArgs = JsonArgs.Validate(argsJson, allowedKeys, requiredKeys, "", "");
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
                    result = GetOne(cfg, schema, schemaBound, JsonArgs.Get(argsJson, "key"));
                    return true;
                }
                if (method == "set")
                {
                    string declareArg = JsonArgs.Get(argsJson, "declare");
                    bool declare = string.Equals(declareArg, "true", StringComparison.OrdinalIgnoreCase);
                    result = SetOne(cfg, schema, schemaBound, JsonArgs.Get(argsJson, "key"), JsonArgs.Get(argsJson, "value"), declare, JsonArgs.Get(argsJson, "desc"));
                    return true;
                }
                if (method == "reset")
                {
                    result = ResetOne(cfg, schema, schemaBound, JsonArgs.Get(argsJson, "key"));
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
            int writable = 0;
            int sensitive = 0;
            for (int i = 0; i < items.Length; i = i + 1)
            {
                if (items[i].Declared && items[i].Writable)
                {
                    writable = writable + 1;
                }
                if (items[i].Sensitive)
                {
                    sensitive = sensitive + 1;
                }
            }
            // 结构化返回体（design-ch4-tools 附录）——首行 JSON 元数据头 + 正文定界
            System.Collections.Generic.Dictionary<string, object> fields = new System.Collections.Generic.Dictionary<string, object>();
            fields["count"] = items.Length;
            fields["writable"] = writable;
            fields["sensitive"] = sensitive;
            // A152——段清单（schema 声明前缀 ∪ 落盘键前缀：派生段亦可发现）
            fields["sections"] = string.Join(" / ", ListSegments(cfg, readSchema).ToArray());
            return MetaHead("config-list", fields) + "\n" + TrimResult(sb.ToString(), 20000);
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
                // A152——缺键说明区分「段不存在」/「键不存在」，并给出段清单 / 段内键清单（段面发现入口）
                return "ERR|NOT_FOUND|" + DescribeMissing(cfg, readSchema, key);
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
            // 结构化返回体（design-ch4-tools 附录）——首行 JSON 元数据头 + 正文定界
            System.Collections.Generic.Dictionary<string, object> fields = new System.Collections.Generic.Dictionary<string, object>();
            fields["key"] = key;
            fields["source"] = entry.Source;
            fields["declared"] = entry.Declared;
            fields["writable"] = entry.Writable;
            fields["sensitive"] = entry.Sensitive;
            return MetaHead("config-get", fields) + "\n" + sb.ToString();
        }

        /// <summary>
        /// set——白名单写（唯一实现 ConfigStore.SetChecked）
        /// </summary>
        /// <param name="cfg">配置存储</param>
        /// <param name="schema">配置 schema</param>
        /// <param name="schemaBound">schema 是否绑定</param>
        /// <param name="key">配置键</param>
        /// <param name="value">新值</param>
        /// <param name="declare">是否声明新键（true 且键未声明时先入 schema 再写入）</param>
        /// <param name="desc">声明描述（declare=true 时生效，可空）</param>
        /// <returns>确认文本</returns>
        private static string SetOne(ConfigStore cfg, ConfigSchema schema, bool schemaBound, string key, string value, bool declare, string desc)
        {
            if (key == null || key.Length == 0)
            {
                return "ERR|BAD_ARGS|缺少参数 key";
            }
            bool declared = schemaBound && schema != null && schema.Find(key) != null;
            string declareNote = "";
            if (!declared)
            {
                if (!declare)
                {
                    return "ERR|CONFIG_REJECT|配置键未在 schema 声明: " + key + "——新建请加 declare=true（可配 desc=说明）";
                }
                if (schema == null)
                {
                    return "ERR|CONFIG_DECLARE|schema 未绑定";
                }
                string fileName;
                bool newSegment;
                string declareError;
                if (!schema.Declare(key, desc, out fileName, out newSegment, out declareError))
                {
                    return "ERR|CONFIG_DECLARE|" + declareError;
                }
                declareNote = " · 已声明（段文件 " + fileName + "）";
                if (newSegment)
                {
                    int dot = key.IndexOf('.');
                    string segment = key.Substring(0, dot);
                    string dir = Path.GetDirectoryName(schema.SourcePath);
                    if (dir != null && dir.Length > 0)
                    {
                        cfg.AddFile(segment, Path.Combine(dir, fileName));
                        declareNote = declareNote + " · 新段已即时注册";
                    }
                    else
                    {
                        declareNote = declareNote + " · 新段目录未知——重启后自动注册";
                    }
                }
            }
            else if (declare)
            {
                declareNote = " · 键已声明，declare 忽略";
            }
            string error;
            if (!cfg.SetChecked(key, value, schemaBound ? schema : null, out error))
            {
                return "ERR|CONFIG_REJECT|" + error;
            }
            bool sensitive = schemaBound && schema != null && schema.IsSensitive(key);
            // 结构化返回体（design-ch4-tools 附录）——首行 JSON 元数据头 + 正文定界
            System.Collections.Generic.Dictionary<string, object> fields = new System.Collections.Generic.Dictionary<string, object>();
            fields["key"] = key;
            fields["sensitive"] = sensitive;
            fields["declaredNow"] = !declared;
            return MetaHead("config-set", fields) + "\n" + "配置已更新: " + key + "=" + (sensitive ? "****" : value) + declareNote;
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
            // 结构化返回体（design-ch4-tools 附录）——首行 JSON 元数据头 + 正文定界
            string keyText = "";
            if (key != null)
            {
                keyText = key;
            }
            bool all = (keyText.Length == 0);
            System.Collections.Generic.Dictionary<string, object> fields = new System.Collections.Generic.Dictionary<string, object>();
            fields["key"] = keyText;
            fields["scope"] = all ? "all" : "single";
            fields["count"] = all ? CountWritable(schema, schemaBound) : 1;
            if (all)
            {
                return MetaHead("config-reset", fields) + "\n" + "已还原默认: 全部可写配置项";
            }
            return MetaHead("config-reset", fields) + "\n" + "已还原默认: " + keyText;
        }

        /// <summary>
        /// 展平参数提取——argsJson 中取字符串值（不存在返回空串）
        /// </summary>
        /// <param name="argumentsJson">参数 JSON</param>
        /// <param name="key">参数名</param>
        /// <summary>
        /// 段名提取（A152）——键首点号前缀（`ui.font_scale` → `ui`）；无点号或点号在首位返回空串
        /// </summary>
        /// <param name="key">配置键</param>
        /// <returns>段名（空=无段）</returns>
        private static string SegmentOf(string key)
        {
            if (key == null)
            {
                return "";
            }
            int dot = key.IndexOf('.');
            if (dot <= 0)
            {
                return "";
            }
            return key.Substring(0, dot);
        }

        /// <summary>
        /// 段清单（A152）——schema 声明键前缀 ∪ 落盘键前缀（去重保序；派生段 / 未声明段一并可见）
        /// </summary>
        /// <param name="cfg">配置存储</param>
        /// <param name="schema">配置 schema（可 null）</param>
        /// <returns>段名列表</returns>
        private static System.Collections.Generic.List<string> ListSegments(ConfigStore cfg, ConfigSchema? schema)
        {
            System.Collections.Generic.List<string> segments = new System.Collections.Generic.List<string>();
            if (schema != null)
            {
                ConfigSchema.Item[] items = schema.All();
                for (int i = 0; i < items.Length; i = i + 1)
                {
                    AddSegment(segments, SegmentOf(items[i].Key));
                }
            }
            if (cfg != null)
            {
                System.Collections.Generic.KeyValuePair<string, string>[] all = cfg.All();
                for (int i = 0; i < all.Length; i = i + 1)
                {
                    AddSegment(segments, SegmentOf(all[i].Key));
                }
            }
            return segments;
        }

        /// <summary>
        /// 段名去重追加——已存在（忽略大小写）不重复加入
        /// </summary>
        /// <param name="segments">段清单</param>
        /// <param name="segment">候选段名（空跳过）</param>
        private static void AddSegment(System.Collections.Generic.List<string> segments, string segment)
        {
            if (segment.Length == 0)
            {
                return;
            }
            for (int i = 0; i < segments.Count; i = i + 1)
            {
                if (string.Equals(segments[i], segment, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }
            segments.Add(segment);
        }

        /// <summary>
        /// 缺键说明（A152）——分「段不存在」/「键不存在」：段不存在时列可用段；段存在时列段内键（上限 8 条）
        /// </summary>
        /// <param name="cfg">配置存储</param>
        /// <param name="schema">配置 schema（可 null）</param>
        /// <param name="key">未命中的配置键</param>
        /// <returns>说明文本（替代原「配置键不存在」一句话）</returns>
        private static string DescribeMissing(ConfigStore cfg, ConfigSchema? schema, string key)
        {
            System.Collections.Generic.List<string> segments = ListSegments(cfg, schema);
            string segment = SegmentOf(key);
            if (segment.Length == 0)
            {
                return "键缺少段前缀（形如 ui.font_scale）——可用段: " + string.Join(" / ", segments.ToArray());
            }
            bool known = false;
            for (int i = 0; i < segments.Count; i = i + 1)
            {
                if (string.Equals(segments[i], segment, StringComparison.OrdinalIgnoreCase))
                {
                    known = true;
                    break;
                }
            }
            if (!known)
            {
                return "段不存在: " + segment + "——可用段: " + string.Join(" / ", segments.ToArray());
            }
            System.Collections.Generic.List<string> keys = new System.Collections.Generic.List<string>();
            if (schema != null)
            {
                ConfigSchema.Item[] items = schema.All();
                for (int i = 0; i < items.Length; i = i + 1)
                {
                    if (string.Equals(SegmentOf(items[i].Key), segment, StringComparison.OrdinalIgnoreCase))
                    {
                        keys.Add(items[i].Key);
                    }
                }
            }
            if (cfg != null)
            {
                System.Collections.Generic.KeyValuePair<string, string>[] all = cfg.All();
                for (int i = 0; i < all.Length; i = i + 1)
                {
                    if (!string.Equals(SegmentOf(all[i].Key), segment, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    bool dup = false;
                    for (int k = 0; k < keys.Count; k = k + 1)
                    {
                        if (string.Equals(keys[k], all[i].Key, StringComparison.Ordinal))
                        {
                            dup = true;
                            break;
                        }
                    }
                    if (!dup)
                    {
                        keys.Add(all[i].Key);
                    }
                }
            }
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.Append("键不存在: ");
            sb.Append(key);
            sb.Append("——段 ");
            sb.Append(segment);
            sb.Append(" 现有键 ");
            sb.Append(keys.Count.ToString());
            sb.Append(" 个");
            if (keys.Count > 0)
            {
                int show = keys.Count;
                if (show > 8)
                {
                    show = 8;
                }
                sb.Append(": ");
                for (int i = 0; i < show; i = i + 1)
                {
                    if (i > 0)
                    {
                        sb.Append(", ");
                    }
                    sb.Append(keys[i]);
                }
                if (keys.Count > show)
                {
                    sb.Append(" …");
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// 结构化元数据头——首行单行 JSON（ok/tool + 调用方字段；键序稳定 = 插入序）
        /// 约定（design-ch4-tools 附录）：返回体 = 首行 JSON 头 + 正文定界行（正文不塞进 JSON——避免转义膨胀）
        /// </summary>
        /// <param name="tool">工具名（config-list / config-get / config-set / config-reset）</param>
        /// <param name="fields">附加字段（按插入序输出）</param>
        /// <returns>单行 JSON</returns>
        private static string MetaHead(string tool, System.Collections.Generic.Dictionary<string, object> fields)
        {
            System.Collections.Generic.Dictionary<string, object> head = new System.Collections.Generic.Dictionary<string, object>();
            head["ok"] = true;
            head["tool"] = tool;
            foreach (System.Collections.Generic.KeyValuePair<string, object> kv in fields)
            {
                head[kv.Key] = kv.Value;
            }
            return JsonSerializer.Serialize(head);
        }

        /// <summary>
        /// 可写项计数——全群还原的规模量（schema 未绑定时 0）
        /// </summary>
        /// <param name="schema">配置 schema</param>
        /// <param name="schemaBound">schema 是否绑定</param>
        /// <returns>writable 项数</returns>
        private static int CountWritable(ConfigSchema schema, bool schemaBound)
        {
            if (!schemaBound || schema == null)
            {
                return 0;
            }
            int n = 0;
            ConfigSchema.Item[] all = schema.All();
            for (int i = 0; i < all.Length; i = i + 1)
            {
                if (all[i].Writable)
                {
                    n = n + 1;
                }
            }
            return n;
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
// #MAU_CHECKSUM:SHA256:53350F53F69D9689BE5482D07A508B510533547617243BDEA6FD5DB04988C6BD
