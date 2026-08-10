// ═══════════════════════════════════════════════════
// 积木: audit.find
// ID:   BRIK-AUDIT-003
// 类别: AUDIT
// 作用: 审计精确搜索（形态三）——按序号 seq:{n} 或属性键值 key=value 定位完整事件
// 依赖: 无
// 引用: Mau.Runtime
// 原理: DataBox 解析 AuditQuery → FindBySeq / FindByProp → FormatEvents MD 输出
// 常用: "查 seq 42 是什么事件" / "查所有 key=chat_x_msg 的事件"
// 包: 无
// ═══════════════════════════════════════════════════
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// AUDIT 积木——audit.find 审计精确搜索（读取三形态·形态三）
    /// </summary>
    public static class AuditFindBrick
    {
        /// <summary>
        /// 精确搜索——flat："seq:{n}" 按序号 / "key=value" 按属性键值
        /// </summary>
        /// <param name="flat">搜索条件</param>
        /// <param name="text">MD 事件输出</param>
        /// <returns>true=命中或查询服务可用（未命中输出（无匹配）返回 false）</returns>
        public static bool Find(string flat, out string text)
        {
            text = "";
            AuditQuery? query;
            if (!DataBox.TryResolve<AuditQuery>(out query))
            {
                return false;
            }
            if (flat == null || flat.Length == 0)
            {
                text = "（无匹配）";
                return false;
            }
            if (flat.StartsWith("seq:", System.StringComparison.Ordinal))
            {
                long seq;
                if (long.TryParse(flat.Substring(4), out seq))
                {
                    AuditEvent ev = query.FindBySeq(seq);
                    if (ev != null)
                    {
                        text = query.FormatEvents(new AuditEvent[] { ev });
                        return true;
                    }
                }
                text = "（无匹配）";
                return false;
            }
            int eq = flat.IndexOf('=');
            if (eq > 0)
            {
                string key = flat.Substring(0, eq);
                string value = flat.Substring(eq + 1);
                AuditEvent[] events = query.FindByProp(key, value);
                text = query.FormatEvents(events);
                return events.Length > 0;
            }
            text = "（无匹配）";
            return false;
        }
    }
}
// #MAU_CHECKSUM:SHA256:343286514A9E9310E4FA29D4F5226CE44639A273340F745FB454253045F01F3C
