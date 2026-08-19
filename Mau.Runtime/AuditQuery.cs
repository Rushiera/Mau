using System;
using System.Collections.Generic;
using System.Text;

namespace Mau.Runtime
{
    /// <summary>
    /// 审计统计条目——形态一全量统计的聚合单元（类别×来源×计数）
    /// </summary>
    public sealed class AuditStat
    {
        /// <summary>
        /// 事件类别（cmd.set/oa.post/trace.*/...）
        /// </summary>
        public string Category { get; }

        /// <summary>
        /// 事件来源（CommandBus/OA/FlowRunner/...）
        /// </summary>
        public string Source { get; }

        /// <summary>
        /// 计数
        /// </summary>
        public long Count { get; }

        /// <summary>
        /// 构造统计条目
        /// </summary>
        /// <param name="category">类别</param>
        /// <param name="source">来源</param>
        /// <param name="count">计数</param>
        public AuditStat(string category, string source, long count)
        {
            Category = category;
            Source = source;
            Count = count;
        }
    }

    /// <summary>
    /// 审计查询——读取三形态（design-mau-audit.md §三）：全量统计 / 段读取 / 精确搜索。
    /// 数据源 = AuditStore 环形缓冲（内存全量）；输出统一 MD 键值格式；无结果（无匹配）。
    /// 注册：宿主 DataBox.Bind&lt;AuditQuery&gt;(query)——audit 积木族经 DataBox 寻路访问（功能一体）。
    /// </summary>
    public sealed class AuditQuery
    {
        /// <summary>审计存储——环形缓冲数据源</summary>
        private readonly AuditStore _store;

        /// <summary>
        /// 构造审计查询——绑定审计存储（环形缓冲为数据源）
        /// </summary>
        /// <param name="store">审计存储</param>
        public AuditQuery(AuditStore store)
        {
            if (store == null)
            {
                throw new ArgumentNullException("store");
            }
            _store = store;
        }

        /// <summary>
        /// 形态一 全量统计——类别×来源×计数（可过滤：类别/来源/帧范围；空 = 全量）
        /// </summary>
        /// <param name="category">类别过滤（空 = 全部）</param>
        /// <param name="source">来源过滤（空 = 全部）</param>
        /// <param name="frameFrom">起始帧（含）</param>
        /// <param name="frameTo">结束帧（含）</param>
        /// <returns>统计条目数组——类别×来源聚合</returns>
        public AuditStat[] Stats(string? category, string? source, long frameFrom, long frameTo)
        {
            AuditEvent[] snap = _store.Snapshot();
            Dictionary<string, long> counts = new Dictionary<string, long>(StringComparer.Ordinal);
            List<string> order = new List<string>();
            for (int i = 0; i < snap.Length; i = i + 1)
            {
                AuditEvent e = snap[i];
                if (e.Frame < frameFrom || e.Frame > frameTo)
                {
                    continue;
                }
                if (category != null && category.Length > 0 && e.Category != category)
                {
                    continue;
                }
                if (source != null && source.Length > 0 && e.Source != source)
                {
                    continue;
                }
                string key = e.Category + "\u0001" + e.Source;
                if (counts.ContainsKey(key))
                {
                    counts[key] = counts[key] + 1;
                }
                else
                {
                    counts[key] = 1;
                    order.Add(key);
                }
            }
            AuditStat[] result = new AuditStat[order.Count];
            for (int i = 0; i < order.Count; i = i + 1)
            {
                string key = order[i];
                int sep = key.IndexOf('\u0001');
                string cat = key.Substring(0, sep);
                string src = key.Substring(sep + 1);
                result[i] = new AuditStat(cat, src, counts[key]);
            }
            return result;
        }

        /// <summary>
        /// 形态二 段读取——帧范围/类别/来源过滤的事件序列（时间序，载荷为摘要级）
        /// </summary>
        /// <param name="frameFrom">起始帧（含）</param>
        /// <param name="frameTo">结束帧（含）</param>
        /// <param name="category">类别过滤（空 = 全部）</param>
        /// <param name="source">来源过滤（空 = 全部）</param>
        /// <returns>事件数组——无匹配返回空数组</returns>
        public AuditEvent[] Segment(long frameFrom, long frameTo, string? category, string? source)
        {
            AuditEvent[] snap = _store.Snapshot();
            List<AuditEvent> result = new List<AuditEvent>();
            for (int i = 0; i < snap.Length; i = i + 1)
            {
                AuditEvent e = snap[i];
                if (e.Frame < frameFrom || e.Frame > frameTo)
                {
                    continue;
                }
                if (category != null && category.Length > 0 && e.Category != category)
                {
                    continue;
                }
                if (source != null && source.Length > 0 && e.Source != source)
                {
                    continue;
                }
                result.Add(e);
            }
            return result.ToArray();
        }

        /// <summary>
        /// 形态三 精确搜索——按全局事件序号定位单事件
        /// </summary>
        /// <param name="seq">事件序号</param>
        /// <returns>事件；不存在返回 null</returns>
        public AuditEvent? FindBySeq(long seq)
        {
            AuditEvent[] snap = _store.Snapshot();
            for (int i = snap.Length - 1; i >= 0; i = i - 1)
            {
                if (snap[i].Seq == seq)
                {
                    return snap[i];
                }
            }
            return null;
        }

        /// <summary>
        /// 形态三 精确搜索——按属性键值精确匹配（如 key=chat_x_msg）
        /// </summary>
        /// <param name="propKey">属性键</param>
        /// <param name="propValue">属性值</param>
        /// <returns>匹配事件数组——无匹配返回空数组</returns>
        public AuditEvent[] FindByProp(string propKey, string propValue)
        {
            AuditEvent[] snap = _store.Snapshot();
            List<AuditEvent> result = new List<AuditEvent>();
            for (int i = snap.Length - 1; i >= 0; i = i - 1)
            {
                AuditEvent e = snap[i];
                for (int j = 0; j < e.Props.Length; j = j + 1)
                {
                    if (e.Props[j].Key == propKey && e.Props[j].Value == propValue)
                    {
                        result.Add(e);
                        break;
                    }
                }
            }
            return result.ToArray();
        }

        /// <summary>
        /// 统计结果 MD 格式化——键值块 + 类别×来源×计数行
        /// </summary>
        /// <param name="stats">统计条目</param>
        /// <param name="frameFrom">起始帧（含）</param>
        /// <param name="frameTo">结束帧（含）</param>
        /// <returns>MD 文本</returns>
        public string FormatStats(AuditStat[] stats, long frameFrom, long frameTo)
{
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("## AUDIT STAT");
            // GAP.4 段来源标注——数据源 = Log 真源（O2 并入）
            sb.AppendLine("- 数据源: Log 真源（audit.* 过滤；Data/runs/<会话>/log.all 落盘）");
            sb.AppendLine("- frame: " + frameFrom + "-" + frameTo);
            long total = 0;
            for (int i = 0; i < stats.Length; i = i + 1)
            {
                total = total + stats[i].Count;
            }
            sb.AppendLine("- total: " + total);
            for (int i = 0; i < stats.Length; i = i + 1)
            {
                sb.AppendLine("- " + stats[i].Category + " | " + stats[i].Source + " | " + stats[i].Count);
            }
            return sb.ToString();
        }
        /// <summary>
        /// 事件序列 MD 格式化——每事件标题行 + 属性行；空数组输出（无匹配）
        /// </summary>
        /// <param name="events">事件数组</param>
        /// <returns>MD 文本</returns>
        public string FormatEvents(AuditEvent[] events)
{
            // GAP.4 段来源标注——查询数据源 = 环形缓冲（内存）；落盘文件为 MD 留痕不参与查询
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("- 数据源: Log 真源（audit.* 过滤；Data/runs/<会话>/log.all 落盘）");
            if (events.Length == 0)
            {
                sb.AppendLine("（无匹配）");
                return sb.ToString();
            }
            for (int i = 0; i < events.Length; i = i + 1)
            {
                sb.Append(AuditStore.FormatEvent(events[i]));
            }
            return sb.ToString();
        }    }
}
