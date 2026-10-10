// ═══════════════════════════════════════════════════
// 积木: temp.keys
// ID:   BRIK-TEMP-002
// 类别: TEMP
// 作用: 临时工具 Key 组枚举——返回当前 TempRegistry 全部可用 Key（逗号分隔）；返回体 = 首行 JSON 头 + 正文
// 依赖: 无
// 引用: 无
// 原理: 读 TempRegistry.ListKeys()（临时工具注册表——LLM 可改区）→ out keys
// 常用: TempToolCat 认领线——'temp.keys'[] > @keys（temp-info 工具消费面）
// ═══════════════════════════════════════════════════
#nullable disable warnings
using System;

namespace Mau.Bricks
{
    /// <summary>
    /// 临时积木——temp.keys 枚举当前可用临时工具 Key 组（temp-info 消费面）
    /// </summary>
    public static class TempKeysBrick
    {
        /// <summary>
        /// Key 组枚举——逗号分隔
        /// </summary>
        /// <param name="keys">Key 组文本（逗号分隔；空 = 暂无临时工具）</param>
        /// <returns>true=已输出</returns>
        /// <summary>
        /// 结构化元数据头（统一口径·A214）——恒定 ok / tool + 主来源 target + 主计数 items（负值 = 省略）+ 专有字段（插入序）
        /// 约定（design-ch4-tools 附录）：返回体 = 首行 JSON 头 + 正文摘要行 + 载荷（正文不塞进 JSON——避免转义膨胀）
        /// </summary>
        /// <param name="tool">工具名</param>
        /// <param name="ok">成败</param>
        /// <param name="target">主来源（空串 = 省略）</param>
        /// <param name="items">主计数（负值 = 省略）</param>
        /// <param name="fields">附加字段（按插入序输出）</param>
        /// <returns>单行 JSON</returns>
        private static string MetaHead(string tool, bool ok, string target, int items, System.Collections.Generic.Dictionary<string, object> fields)
        {
            System.Collections.Generic.Dictionary<string, object> head = new System.Collections.Generic.Dictionary<string, object>();
            head["ok"] = ok;
            head["tool"] = tool;
            if (target.Length > 0)
            {
                head["target"] = target;
            }
            if (items >= 0)
            {
                head["items"] = items;
            }
            foreach (System.Collections.Generic.KeyValuePair<string, object> kv in fields)
            {
                head[kv.Key] = kv.Value;
            }
            return System.Text.Json.JsonSerializer.Serialize(head);
        }

        public static bool Keys(out string keys)
        {
            string list = TempRegistry.ListKeys();
            // 结构化返回体（design-ch4-tools 附录 · A214）——首行 JSON 头（items + 专有）+ 正文摘要行 + 载荷
            string[] parts = (list.Length == 0) ? new string[0] : list.Split(',');
            System.Collections.Generic.Dictionary<string, object> fields = new System.Collections.Generic.Dictionary<string, object>();
            System.Collections.Generic.List<object> keysList = new System.Collections.Generic.List<object>();
            for (int i = 0; i < parts.Length; i = i + 1)
            {
                keysList.Add(parts[i]);
            }
            fields["keys"] = keysList;
            keys = MetaHead("temp-info", true, "", parts.Length, fields) + ((list.Length == 0) ? "" : ("\n" + parts.Length.ToString() + " 个 Key\n" + list));
            return true;
        }
    }
}
// #MAU_CHECKSUM:SHA256:256B6D6829EBD213266B03CFE495D29DEC105E8B7EA3F2C95132ED2E1071DD63
