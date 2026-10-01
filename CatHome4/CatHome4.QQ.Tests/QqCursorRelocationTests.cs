using CatHome4.Contracts;
using CatHome4.QQ;
using Xunit;

namespace CatHome4.QQ.Tests
{
    /// <summary>
    /// QQ 转发游标校正测试（A111）——ComputeCursor 三级判据（区间平移 / 锚点定位 / 保守前看）+ 边界夹取。
    /// 覆盖：区间完全在游标之前（移除+新增 / 纯插入 / 上界夹取）· 区间不跨游标时的锚点定位 ·
    /// 区间跨越游标且锚点搬家 · 锚点未命中 · 锚点缺失（旧快照回落块数比对）· 空哈希锚点。
    /// </summary>
    public sealed class QqCursorRelocationTests
    {
        /// <summary>构造视图项数组——哈希与内容同串（便于阅读失败输出）</summary>
        private static QqViewItem[] Items(string[] hashes)
        {
            QqViewItem[] items = new QqViewItem[hashes.Length];
            for (int i = 0; i < hashes.Length; i = i + 1)
            {
                QqViewItem it = new QqViewItem();
                it.RenderType = "text";
                it.Content = hashes[i];
                it.Done = "";
                it.Hash = hashes[i];
                items[i] = it;
            }
            return items;
        }

        /// <summary>构造锚点——内容哈希 + 记录位置</summary>
        private static QqAnchor Anchor(string hash, int pos)
        {
            QqAnchor a = new QqAnchor();
            a.Hash = hash;
            a.Position = pos;
            return a;
        }

        /// <summary>构造块序变更区间——起点 / 移除数 / 新增数</summary>
        private static ViewOrderChange Change(int from, int removed, int added)
        {
            ViewOrderChange c = new ViewOrderChange();
            c.From = from;
            c.RemovedCount = removed;
            c.AddedCount = added;
            return c;
        }

        /// <summary>序列哈希——h0..hN（长度 N+1）</summary>
        private static string[] Series(int count)
        {
            string[] a = new string[count];
            for (int i = 0; i < count; i = i + 1)
            {
                a[i] = "h" + i.ToString();
            }
            return a;
        }

        /// <summary>区间完全在游标之前——游标按「移除 / 新增」平移（timeback 回收主路径）</summary>
        [Fact]
        public void RangeBeforeCursor_ShiftsByDelta()
        {
            string how = "";
            int got = QQBotService.ComputeCursor(10, Change(2, 3, 1), null, Items(Series(12)), out how);
            Assert.Equal(8, got);
            Assert.Contains("区间平移", how);
        }

        /// <summary>区间完全在游标之前——纯插入（移除 0）时游标后移</summary>
        [Fact]
        public void RangeBeforeCursor_PureInsertion()
        {
            string how = "";
            int got = QQBotService.ComputeCursor(5, Change(2, 0, 2), null, Items(Series(10)), out how);
            Assert.Equal(7, got);
        }

        /// <summary>区间平移结果越界——夹取到块数上界</summary>
        [Fact]
        public void RangeBeforeCursor_ClampedToCount()
        {
            string how = "";
            int got = QQBotService.ComputeCursor(3, Change(0, 0, 5), null, Items(Series(4)), out how);
            Assert.Equal(4, got);
        }

        /// <summary>区间在游标之后——游标不动，按锚点定位（命中位置 + 1）</summary>
        [Fact]
        public void RangeAfterCursor_AnchorHit()
        {
            string how = "";
            int got = QQBotService.ComputeCursor(5, Change(7, 2, 0), Anchor("h4", 4), Items(Series(10)), out how);
            Assert.Equal(5, got);
            Assert.Contains("锚点定位", how);
        }

        /// <summary>区间跨越游标且锚点块搬家——按哈希定位到新位置（精确续接，不重放后缀）</summary>
        [Fact]
        public void RangeAcrossCursor_AnchorMoved()
        {
            string[] hashes = new string[] { "h0", "h1", "h4", "h5", "h6", "h7", "h8", "h9" };
            string how = "";
            int got = QQBotService.ComputeCursor(5, Change(3, 4, 1), Anchor("h4", 4), Items(hashes), out how);
            Assert.Equal(3, got);
            Assert.Contains("锚点定位", how);
        }

        /// <summary>锚点未命中（块已被移除）——保守前看（游标 = 当前块数，不追发历史）</summary>
        [Fact]
        public void AnchorMissing_ConservativeLookAhead()
        {
            string how = "";
            int got = QQBotService.ComputeCursor(5, Change(3, 4, 1), Anchor("zz", 2), Items(Series(9)), out how);
            Assert.Equal(9, got);
            Assert.Contains("保守前看", how);
        }

        /// <summary>旧快照无锚点——块数 == 游标时等价直接续接（游标保持）</summary>
        [Fact]
        public void StaleSnapshot_NoAnchor_CountEqualKeepsCursor()
        {
            string how = "";
            int got = QQBotService.ComputeCursor(7, null, null, Items(Series(7)), out how);
            Assert.Equal(7, got);
            Assert.Contains("保守前看", how);
        }

        /// <summary>旧快照无锚点——块数不等时保守前看（游标 = 当前块数）</summary>
        [Fact]
        public void StaleSnapshot_NoAnchor_CountDiffConservative()
        {
            string how = "";
            int got = QQBotService.ComputeCursor(5, null, null, Items(Series(7)), out how);
            Assert.Equal(7, got);
        }

        /// <summary>空哈希锚点——不可作定位依据，回落保守前看</summary>
        [Fact]
        public void EmptyAnchorHash_ConservativeLookAhead()
        {
            string how = "";
            int got = QQBotService.ComputeCursor(3, Change(3, 1, 0), Anchor("", 2), Items(Series(5)), out how);
            Assert.Equal(5, got);
            Assert.Contains("保守前看", how);
        }
    }
}
