using System;
using System.Collections.Generic;
using System.IO;
using CatHome4.Contracts;
using Mau.Runtime;
using Xunit;

namespace CatHome4.Core.Tests
{
    /// <summary>
    /// 视图块序变更通知测试（A111）——四处变更点（清除 / 重建 / 轮统计清理 / 区间转废弃）统一出声；
    /// 区间口径 = 「公共前缀 + 公共后缀之外」的最小差异（转发面据此平移或重定位游标）。
    /// </summary>
    public sealed class ViewOrderNotifyTests : IDisposable
    {
        /// <summary>临时目录——Guid 防并发撞车</summary>
        private readonly string _dir;

        /// <summary>被测视图存储</summary>
        private readonly CH4.SessionViewStore _store;

        /// <summary>收到的变更通知——按发出序累积</summary>
        private readonly List<ViewOrderChange> _changes = new List<ViewOrderChange>();

        /// <summary>建立夹具——临时目录 + 视图实例 + 通知接线</summary>
        public ViewOrderNotifyTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "cat4vieworder_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _store = new CH4.SessionViewStore(Path.Combine(_dir, "view.json"));
            _store.OnBlocksReordered = delegate (ViewOrderChange c)
            {
                _changes.Add(c);
            };
        }

        /// <summary>释放夹具——尽力删除临时目录（清理失败不影响断言结论）</summary>
        public void Dispose()
        {
            try
            {
                Directory.Delete(_dir, true);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[测试清理] 视图临时目录删除失败: " + ex.Message);
            }
        }

        /// <summary>构造用户消息</summary>
        private static LlmMessage User(string text, long ts)
        {
            LlmMessage m = new LlmMessage();
            m.Role = LlmRole.User;
            m.Content = text;
            m.CreatedAt = ts;
            return m;
        }

        /// <summary>构造 assistant 纯文本消息</summary>
        private static LlmMessage Assistant(string text, long ts)
        {
            LlmMessage m = new LlmMessage();
            m.Role = LlmRole.Assistant;
            m.Content = text;
            m.CreatedAt = ts;
            return m;
        }

        /// <summary>灌入两轮往返——块序：user(0) / text(1) / user(2) / text(3)</summary>
        private void SeedTwoRounds()
        {
            _store.OnUserMessage(User("问一", 100L), 0);
            _store.OnAssistantText(Assistant("答一", 110L), 1);
            _store.OnUserMessage(User("问二", 120L), 2);
            _store.OnAssistantText(Assistant("答二", 130L), 3);
        }

        /// <summary>未接线（无消费方）时变更点零动作且不抛异常</summary>
        [Fact]
        public void NoHandler_MutationsDoNotThrow()
        {
            _store.OnBlocksReordered = null;
            SeedTwoRounds();
            _store.ClearRoundSums();
            _store.Clear();
            Assert.Empty(_changes);
            Assert.Empty(_store.GetBlocks());
        }

        /// <summary>清空——全量移除（起点 0：转发面游标随之归位）</summary>
        [Fact]
        public void Clear_NotifiesWholeRange()
        {
            SeedTwoRounds();
            _changes.Clear();
            _store.Clear();
            Assert.Single(_changes);
            Assert.Equal(0, _changes[0].From);
            Assert.Equal(4, _changes[0].RemovedCount);
            Assert.Equal(0, _changes[0].AddedCount);
        }

        /// <summary>清轮末统计——只报尾部 roundsum 段（消息块前缀不动）</summary>
        [Fact]
        public void ClearRoundSums_NotifiesTailRange()
        {
            SeedTwoRounds();
            _store.AppendRoundSummary("{\"type\":\"roundsum\"}", 200L);
            _store.AppendRoundSummary("{\"type\":\"roundsum\"}", 210L);
            _changes.Clear();
            _store.ClearRoundSums();
            Assert.Single(_changes);
            Assert.Equal(4, _changes[0].From);
            Assert.Equal(2, _changes[0].RemovedCount);
            Assert.Equal(0, _changes[0].AddedCount);
        }

        /// <summary>截断——切点之后的尾部块移除并通知（前缀一致区不动；A156：回滚走显式截断，不再重建）</summary>
        [Fact]
        public void TruncateFrom_NotifiesRemovedTail()
        {
            SeedTwoRounds();
            _changes.Clear();
            int removed = _store.TruncateFrom(2);
            Assert.Equal(2, removed);
            Assert.Single(_changes);
            Assert.Equal(2, _changes[0].From);
            Assert.Equal(2, _changes[0].RemovedCount);
            Assert.Equal(0, _changes[0].AddedCount);
            Assert.Equal(2, _store.GetBlocks().Length);
        }

        /// <summary>区间转废弃——移除 N 块 + 原位插入 1 个归档块（timeback 回收口径）</summary>
        [Fact]
        public void ConvertRangeToVoid_NotifiesMergedRange()
        {
            SeedTwoRounds();
            _changes.Clear();
            int moved = _store.ConvertRangeToVoid(1, 2, "");
            Assert.Equal(2, moved);
            Assert.Single(_changes);
            Assert.Equal(1, _changes[0].From);
            Assert.Equal(2, _changes[0].RemovedCount);
            Assert.Equal(1, _changes[0].AddedCount);
        }

        /// <summary>区间无命中——零动作且不发通知</summary>
        [Fact]
        public void ConvertRangeToVoid_NoHit_NoNotify()
        {
            SeedTwoRounds();
            _changes.Clear();
            int moved = _store.ConvertRangeToVoid(90, 99, "");
            Assert.Equal(0, moved);
            Assert.Empty(_changes);
        }

        /// <summary>区间无变更——块序未变（差异为零）不发通知</summary>
        [Fact]
        public void UnchangedMutation_NoNotify()
        {
            SeedTwoRounds();
            _changes.Clear();
            _store.ClearRoundSums();
            Assert.Empty(_changes);
        }
    }
}
