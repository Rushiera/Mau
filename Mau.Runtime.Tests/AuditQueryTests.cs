using Mau.Runtime;
using Xunit;

namespace Mau.Runtime.Tests
{
    /// <summary>
    /// 审计查询测试——A.4 读取三形态（AuditQuery：全量统计/段读取/精确搜索）
    /// 隔离：AuditStore 内存模式（不 ConfigureAudit——不触碰 Default 静态）
    /// </summary>
    public sealed class AuditQueryTests
    {
        /// <summary>
        /// 构造带审计存储的查询——预置混合事件
        /// </summary>
        /// <returns>查询实例</returns>
        private static AuditQuery CreateQueryWithEvents()
        {
            AuditStore store = new AuditStore();
            store.Record("CommandBus", "cmd.set", 10, new AuditProp[] { new AuditProp("key", "chat_x_msg"), new AuditProp("result", "accepted") });
            store.Record("OA", "oa.post", 11, new AuditProp[] { new AuditProp("officeId", "7") });
            store.Record("CommandBus", "cmd.set", 12, new AuditProp[] { new AuditProp("key", "talk_x_stop"), new AuditProp("result", "accepted") });
            store.Record("CommandBus", "cmd.consume", 13, new AuditProp[] { new AuditProp("owner", "1") });
            return new AuditQuery(store);
        }

        /// <summary>
        /// 形态一 全量统计——类别×来源×计数聚合
        /// </summary>
        [Fact]
        public void Stats_AggregatesByCategoryAndSource()
        {
            AuditQuery query = CreateQueryWithEvents();
            AuditStat[] stats = query.Stats(null, null, 0, long.MaxValue);
            Assert.Equal(3, stats.Length);
            // cmd.set × CommandBus = 2；oa.post × OA = 1；cmd.consume × CommandBus = 1
            Assert.Equal("cmd.set", stats[0].Category);
            Assert.Equal("CommandBus", stats[0].Source);
            Assert.Equal(2L, stats[0].Count);
            Assert.Equal("oa.post", stats[1].Category);
            Assert.Equal(1L, stats[1].Count);
            Assert.Equal("cmd.consume", stats[2].Category);
            Assert.Equal(1L, stats[2].Count);
        }

        /// <summary>
        /// 形态一 全量统计——类别过滤
        /// </summary>
        [Fact]
        public void Stats_FiltersByCategory()
        {
            AuditQuery query = CreateQueryWithEvents();
            AuditStat[] stats = query.Stats("cmd.set", null, 0, long.MaxValue);
            Assert.Single(stats);
            Assert.Equal("cmd.set", stats[0].Category);
            Assert.Equal(2L, stats[0].Count);
        }

        /// <summary>
        /// 形态二 段读取——帧范围过滤事件序列（时间序）
        /// </summary>
        [Fact]
        public void Segment_FiltersByFrameRange()
        {
            AuditQuery query = CreateQueryWithEvents();
            AuditEvent[] events = query.Segment(11, 12, null, null);
            Assert.Equal(2, events.Length);
            Assert.Equal(11L, events[0].Frame);
            Assert.Equal("oa.post", events[0].Category);
            Assert.Equal(12L, events[1].Frame);
            // 类别过滤
            AuditEvent[] byCat = query.Segment(0, long.MaxValue, "cmd.set", null);
            Assert.Equal(2, byCat.Length);
            // 来源过滤
            AuditEvent[] bySrc = query.Segment(0, long.MaxValue, null, "OA");
            Assert.Single(bySrc);
            // 无匹配
            AuditEvent[] none = query.Segment(99, 100, null, null);
            Assert.Empty(none);
        }

        /// <summary>
        /// 形态三 精确搜索——按序号定位 + 按属性键值匹配
        /// </summary>
        [Fact]
        public void Find_BySeqAndByProp()
        {
            AuditQuery query = CreateQueryWithEvents();
            AuditEvent? bySeq = query.FindBySeq(2);
            Assert.NotNull(bySeq);
            Assert.Equal("oa.post", bySeq.Category);
            Assert.Null(query.FindBySeq(999));
            AuditEvent[] byProp = query.FindByProp("key", "chat_x_msg");
            Assert.Single(byProp);
            Assert.Equal("cmd.set", byProp[0].Category);
            AuditEvent[] none = query.FindByProp("key", "nope");
            Assert.Empty(none);
        }

        /// <summary>
        /// 格式化——空事件输出（无匹配）；事件输出 MD 标题+属性
        /// </summary>
        [Fact]
        public void Format_EmptyAndEvents()
        {
            AuditQuery query = CreateQueryWithEvents();
            Assert.Equal("（无匹配）", query.FormatEvents(new AuditEvent[0]));
            AuditEvent[] events = query.Segment(10, 10, null, null);
            string text = query.FormatEvents(events);
            Assert.Contains("## E0000001", text);
            Assert.Contains("- key: chat_x_msg", text);
            // 统计格式化
            string statText = query.FormatStats(query.Stats(null, null, 0, long.MaxValue), 0, long.MaxValue);
            Assert.Contains("## AUDIT STAT", statText);
            Assert.Contains("- total: 4", statText);
            Assert.Contains("- cmd.set | CommandBus | 2", statText);
        }
    }
}
