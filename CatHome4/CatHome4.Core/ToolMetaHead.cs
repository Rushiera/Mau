using System.Collections.Generic;
using Mau.Runtime;

namespace CH4
{
    /// <summary>
    /// 工具结构化返回头——首行单行 JSON 元数据（ok / tool + 调用方字段；键序稳定 = 插入序）。
    /// 约定（Project/CH4/design-ch4-tools.md 附录）：返回体 = 首行 JSON 头 + 正文定界行
    /// （正文不塞进 JSON——避免转义膨胀撞截断面）。
    /// 消费面：前端 chatMetaHead 拆分（headline / 计数徽标由确定字段驱动）——Z8 起宿主侧摘要退役，剥头唯一消费方在前端。
    /// </summary>
    internal static class ToolMetaHead
    {
        /// <summary>
        /// 构建单行 JSON 元数据头——ok / tool 恒定字段 + 调用方字段（按插入序）。
        /// </summary>
        /// <param name="tool">工具名（如 Note / time / host-flows）</param>
        /// <param name="ok">成败</param>
        /// <param name="fields">附加字段（可空）</param>
        /// <returns>单行 JSON</returns>
        public static string Build(string tool, bool ok, Dictionary<string, object> fields)
        {
            Dictionary<string, object> head = new Dictionary<string, object>();
            head["ok"] = ok;
            head["tool"] = tool;
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
        /// <param name="fields">附加字段（可空）</param>
        /// <param name="body">正文（可空 / 空串）</param>
        /// <returns>结构化返回体</returns>
        public static string With(string tool, bool ok, Dictionary<string, object> fields, string body)
        {
            string head = Build(tool, ok, fields);
            if (body == null || body.Length == 0)
            {
                return head;
            }
            return head + "\n" + body;
        }
    }
}
