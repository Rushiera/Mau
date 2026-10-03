using System.Collections.Generic;
using Mau.Runtime;

namespace CH4
{
    /// <summary>
    /// 视图卡载荷构造内核——同类视图卡的唯一拼装处。
    /// 部位：工具卡（toolcard）四处构造点收口——实时先行卡 / 中断终态补推 / A128 逐条回填 / 前文派生（事件直落）。
    /// 动机：同一语义两处实现必漏字段——判例 2026-10-02（前文派生路径漏 order → 前端执行序徽标消失）；
    /// 故「该写哪些字段」只允许一处说了算。纯函数、无宿主依赖——可直测。
    /// 扩展：其余同族载荷（user / text / reason 各两处）按同一模式逐步收口。
    /// </summary>
    internal static class ViewCardPayload
    {
        /// <summary>
        /// 工具卡载荷——name / arguments / result / toolIndex / toolTotal / order 六字段的唯一拼装处。
        /// result 传 null = 先行「进行中」卡（不写 result 字段，前端按处理中渲染）；
        /// 传空串 = 合法空结果（写字段）——两者语义不同，不可合并。
        /// </summary>
        /// <param name="name">工具名</param>
        /// <param name="arguments">参数 JSON（原样；先行卡侧已按需注入 catId）</param>
        /// <param name="result">结果文本（null = 先行卡）</param>
        /// <param name="index">并发序号（1-based）</param>
        /// <param name="total">并发总数</param>
        /// <returns>工具卡载荷字典</returns>
        public static Dictionary<string, object> BuildToolCard(string name, string arguments, string result, int index, int total)
        {
            Dictionary<string, object> payload = new Dictionary<string, object>();
            payload["name"] = name;
            payload["arguments"] = arguments;
            if (result != null)
            {
                payload["result"] = result;
            }
            payload["toolIndex"] = index;
            payload["toolTotal"] = total;
            payload["order"] = ToolOrderTable.OrderText(name);
            return payload;
        }
    }
}
