using System.Collections.Generic;
using Mau.Runtime;

namespace CH4
{
    /// <summary>
    /// 工具结构化返回头（统一口径·A214）——首行单行 JSON 元数据（ok / tool + 主来源 target + 主计数 items + 调用方字段；键序稳定 = 插入序）。
    /// 约定（Project/CH4/design-ch4-tools.md 附录）：返回体 = 首行 JSON 头 + 正文摘要行
    /// （正文不塞进 JSON——避免转义膨胀撞截断面）。target 空串 = 省略；items 负值 = 省略。
    /// </summary>
    internal static class ToolMetaHead
    {
        /// <summary>
        /// 构建单行 JSON 元数据头——ok / tool 恒定 + target / items 主字段 + 调用方字段（按插入序）。
        /// </summary>
        /// <param name="tool">工具名（如 Note / time / host-flows）</param>
        /// <param name="ok">成败</param>
        /// <param name="target">主来源（空串 = 省略）</param>
        /// <param name="items">主计数（负值 = 省略）</param>
        /// <param name="fields">附加字段（可空）</param>
        /// <returns>单行 JSON</returns>
        public static string Build(string tool, bool ok, string target, int items, Dictionary<string, object> fields)
        {
            Dictionary<string, object> head = new Dictionary<string, object>();
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
            if (fields != null)
            {
                foreach (KeyValuePair<string, object> kv in fields)
                {
                    head[kv.Key] = kv.Value;
                }
            }
            return JsonUtil.Serialize(head);
        }

        /// <summary>
        /// 头 + 正文拼接——正文为空时只返回头（不产出尾随空行）。
        /// </summary>
        /// <param name="tool">工具名</param>
        /// <param name="ok">成败</param>
        /// <param name="target">主来源（空串 = 省略）</param>
        /// <param name="items">主计数（负值 = 省略）</param>
        /// <param name="fields">附加字段（可空）</param>
        /// <param name="body">正文（可空 / 空串）</param>
        /// <returns>结构化返回体</returns>
        public static string With(string tool, bool ok, string target, int items, Dictionary<string, object> fields, string body)
        {
            string head = Build(tool, ok, target, items, fields);
            if (body == null || body.Length == 0)
            {
                return head;
            }
            return head + "\n" + body;
        }
    }
}
