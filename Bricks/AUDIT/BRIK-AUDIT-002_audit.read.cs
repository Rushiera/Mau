// ═══════════════════════════════════════════════════
// 积木: audit.read
// ID:   BRIK-AUDIT-002
// 类别: AUDIT
// 作用: 审计段读取（形态二）——帧范围/类别/来源过滤事件序列；flat 载荷 from|to|category|source
// 依赖: 无
// 引用: Mau.Runtime
// 原理: DataBox 解析 AuditQuery → Segment 过滤 → FormatEvents MD 输出（空=（无匹配））
// 常用: "读取第 n 到 m 帧的 cmd.set 事件"——排错时序复盘
// 包: 无
// ═══════════════════════════════════════════════════
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// AUDIT 积木——audit.read 审计段读取（读取三形态·形态二）
    /// </summary>
    public static class AuditReadBrick
    {
        /// <summary>
        /// 段读取——flat 载荷 "from|to|category|source"（空段 = 不过滤）
        /// </summary>
        /// <param name="flat">四段式载荷（| 分隔）</param>
        /// <param name="text">MD 事件序列输出</param>
        /// <returns>true=查询服务可用</returns>
        public static bool Read(string flat, out string text)
        {
            text = "";
            AuditQuery? query;
            if (!DataBox.TryResolve<AuditQuery>(out query))
            {
                return false;
            }
            string[] parts = flat == null ? new string[0] : flat.Split('|');
            long from = 0;
            long to = long.MaxValue;
            if (parts.Length > 0 && parts[0].Length > 0)
            {
                if (!long.TryParse(parts[0], out from))
                {
                    return false;
                }
            }
            if (parts.Length > 1 && parts[1].Length > 0)
            {
                if (!long.TryParse(parts[1], out to))
                {
                    return false;
                }
            }
            string? category = parts.Length > 2 && parts[2].Length > 0 ? parts[2] : null;
            string? source = parts.Length > 3 && parts[3].Length > 0 ? parts[3] : null;
            AuditEvent[] events = query.Segment(from, to, category, source);
            text = query.FormatEvents(events);
            return true;
        }
    }
}
// #MAU_CHECKSUM:SHA256:B2BDC36FBC6150FDBB5AB27BBFBBFF88F04656CACEF823AAE7818C0234DB629E
