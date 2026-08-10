// ═══════════════════════════════════════════════════
// 积木: audit.stat
// ID:   BRIK-AUDIT-001
// 类别: AUDIT
// 作用: 审计全量统计（形态一）——类别×来源×计数；category 空=全量
// 依赖: 无
// 引用: Mau.Runtime
// 原理: DataBox 解析 AuditQuery → Stats 聚合 → FormatStats MD 输出
// 常用: 宿主自查"今天发生了什么"（审计体系第四根支柱——查询侧）
// 包: 无
// ═══════════════════════════════════════════════════
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// AUDIT 积木——audit.stat 审计全量统计（读取三形态·形态一）
    /// </summary>
    public static class AuditStatBrick
    {
        /// <summary>
        /// 全量统计——类别×来源×计数（category 空 = 全部类别）
        /// </summary>
        /// <param name="category">类别过滤（空 = 全部）</param>
        /// <param name="stat">MD 统计输出</param>
        /// <returns>true=查询服务可用</returns>
        public static bool Stat(string category, out string stat)
        {
            stat = "";
            AuditQuery? query;
            if (!DataBox.TryResolve<AuditQuery>(out query))
            {
                return false;
            }
            string? cat = string.IsNullOrEmpty(category) ? null : category;
            AuditStat[] stats = query.Stats(cat, null, 0, long.MaxValue);
            stat = query.FormatStats(stats, 0, long.MaxValue);
            return true;
        }
    }
}
// #MAU_CHECKSUM:SHA256:9A79A38376EB2B46121AC00BEB982EDB5319F63BC2503B5D84CAE861FD489097
