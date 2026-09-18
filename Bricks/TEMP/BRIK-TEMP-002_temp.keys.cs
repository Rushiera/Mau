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
        /// 结构化元数据头——首行单行 JSON（ok/tool/count/keys；键序稳定 = 插入序）
        /// 约定（design-ch4-tools 附录）：返回体 = 首行 JSON 头 + 正文定界行（正文不塞进 JSON——避免转义膨胀）
        /// </summary>
        /// <param name="list">逗号分隔 Key 组</param>
        /// <returns>单行 JSON</returns>
        private static string MetaHead(string list)
        {
            string[] items = (list.Length == 0) ? new string[0] : list.Split(',');
            System.Collections.Generic.List<object> keys = new System.Collections.Generic.List<object>();
            for (int i = 0; i < items.Length; i = i + 1)
            {
                keys.Add(items[i]);
            }
            System.Collections.Generic.Dictionary<string, object> head = new System.Collections.Generic.Dictionary<string, object>();
            head["ok"] = true;
            head["tool"] = "temp-info";
            head["count"] = items.Length;
            head["keys"] = keys;
            return System.Text.Json.JsonSerializer.Serialize(head);
        }

        public static bool Keys(out string keys)
        {
            string list = TempRegistry.ListKeys();
            // 结构化返回体（design-ch4-tools 附录）——首行 JSON 元数据头 + 正文定界
            keys = MetaHead(list) + ((list.Length == 0) ? "" : ("\n" + list));
            return true;
        }
    }
}
// #MAU_CHECKSUM:SHA256:32E0F4829A0BC9633D771FDB69E16ECDD8664999ED4C17A8D37D3FA4EB8205FB
