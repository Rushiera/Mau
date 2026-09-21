using System;
using CH4;
using Xunit;

namespace CatHome4.Core.Tests
{
    /// <summary>
    /// 延迟指令族测试（design-ch4-delay §5.1）——前缀识别与参数面：**偏移量正确性**（判例 2026-09-22：
    /// `delay.addatloop` 的 Substring 少一位 → head 为空 → ERR|BAD_ARGS|时刻非法，条目静默不落表）+ 各指令落表语义。
    /// 断言取状态面（DelayQueue.List）——指令结果文本经日志面观测，单测只判"条目是否按语义落表"。
    /// 静态队列 → GlobalToolState 集合串行（测试隔离纪律）。
    /// </summary>
    [Collection("GlobalToolState")]
    public class DelayCommandTests : IDisposable
    {
        /// <summary>假时钟当前值——毫秒</summary>
        private long _now = 1000000;

        /// <summary>
        /// 建立测试环境——重置队列 + 注入假时钟。
        /// </summary>
        public DelayCommandTests()
        {
            DelayQueue.ResetForTest();
            DelayQueue.NowProvider = FakeNow;
        }

        /// <summary>
        /// 清理——重置队列（假时钟随 ResetForTest 复位）。
        /// </summary>
        public void Dispose()
        {
            DelayQueue.ResetForTest();
        }

        /// <summary>假时钟——返回可控当前毫秒</summary>
        /// <returns>当前毫秒</returns>
        private long FakeNow()
        {
            return _now;
        }

        /// <summary>
        /// delay.add|&lt;秒&gt;|&lt;内容&gt;——相对时长登记（间隔时长随条目落表供循环使用）。
        /// </summary>
        [Fact]
        public void Add_RegistersRelativeEntry()
        {
            bool handled = DelayCommand.Handle("catA", "delay.add|90|喝水提醒");
            Assert.True(handled);
            DelayEntry[] entries = DelayQueue.List("catA");
            Assert.Single(entries);
            Assert.Equal("喝水提醒", entries[0].Content);
            Assert.Equal("delay", entries[0].Source);
            Assert.Equal(_now + 90000, entries[0].DueAt);
            Assert.Equal(90000, entries[0].IntervalMs);
            Assert.False(entries[0].Loop);
        }

        /// <summary>
        /// delay.addat|&lt;unixms&gt;|&lt;内容&gt;——绝对时刻登记（间隔 = 登记跨度）。
        /// </summary>
        [Fact]
        public void AddAt_RegistersAbsoluteEntry()
        {
            DelayCommand.Handle("catA", "delay.addat|" + (_now + 60000).ToString() + "|看构建");
            DelayEntry[] entries = DelayQueue.List("catA");
            Assert.Single(entries);
            Assert.Equal("看构建", entries[0].Content);
            Assert.Equal(_now + 60000, entries[0].DueAt);
            Assert.Equal(60000, entries[0].IntervalMs);
            Assert.False(entries[0].Loop);
        }

        /// <summary>
        /// delay.addatloop|&lt;unixms&gt;|&lt;内容&gt;——绝对时刻 + 循环登记（偏移量判例：head 必须取到时刻本身）。
        /// </summary>
        [Fact]
        public void AddAtLoop_RegistersLoopEntry()
        {
            bool handled = DelayCommand.Handle("catA", "delay.addatloop|" + (_now + 120000).ToString() + "|巡检");
            Assert.True(handled);
            DelayEntry[] entries = DelayQueue.List("catA");
            Assert.Single(entries);
            Assert.Equal("巡检", entries[0].Content);
            Assert.Equal(_now + 120000, entries[0].DueAt);
            Assert.Equal(120000, entries[0].IntervalMs);
            Assert.True(entries[0].Loop);
        }

        /// <summary>
        /// delay.addatloop 参数面——空内容 / 已过时刻拒绝（条目不落表）。
        /// </summary>
        [Fact]
        public void AddAtLoop_RejectsBadArgs()
        {
            DelayCommand.Handle("catA", "delay.addatloop|" + (_now + 60000).ToString() + "|   ");
            DelayCommand.Handle("catA", "delay.addatloop|" + (_now - 1000).ToString() + "|过期");
            DelayCommand.Handle("catA", "delay.addatloop|notanumber|坏时刻");
            Assert.Empty(DelayQueue.List("catA"));
        }

        /// <summary>
        /// delay.loop|&lt;id&gt;|&lt;0|1&gt;——切换循环标记（非法标记不改状态）。
        /// </summary>
        [Fact]
        public void Loop_TogglesMark()
        {
            DelayCommand.Handle("catA", "delay.add|60|单次");
            long id = DelayQueue.List("catA")[0].Id;
            DelayCommand.Handle("catA", "delay.loop|" + id.ToString() + "|1");
            Assert.True(DelayQueue.List("catA")[0].Loop);
            DelayCommand.Handle("catA", "delay.loop|" + id.ToString() + "|2");
            Assert.True(DelayQueue.List("catA")[0].Loop);
            DelayCommand.Handle("catA", "delay.loop|" + id.ToString() + "|0");
            Assert.False(DelayQueue.List("catA")[0].Loop);
        }

        /// <summary>
        /// delay.set|&lt;id&gt;|&lt;unixms&gt; 与 delay.cancel|&lt;id&gt;——改时刻与取消落表语义。
        /// </summary>
        [Fact]
        public void SetAndCancel_Work()
        {
            DelayCommand.Handle("catA", "delay.add|60|改我");
            long id = DelayQueue.List("catA")[0].Id;
            DelayCommand.Handle("catA", "delay.set|" + id.ToString() + "|" + (_now + 300000).ToString());
            Assert.Equal(_now + 300000, DelayQueue.List("catA")[0].DueAt);
            DelayCommand.Handle("catA", "delay.cancel|" + id.ToString());
            Assert.Empty(DelayQueue.List("catA"));
        }

        /// <summary>
        /// 前缀识别——非本族指令返回 false（不吞其他指令）；未知 delay 子指令不改表。
        /// </summary>
        [Fact]
        public void Prefix_And_UnknownCommand()
        {
            Assert.False(DelayCommand.Handle("catA", "session count"));
            Assert.True(DelayCommand.Handle("catA", "delay.nope"));
            Assert.Empty(DelayQueue.List("catA"));
        }

        /// <summary>
        /// 越权面——条目不属于本会话时拒绝（catB 不能改 catA 的条目）。
        /// </summary>
        [Fact]
        public void CrossSession_Rejected()
        {
            DelayCommand.Handle("catA", "delay.add|60|我的");
            long id = DelayQueue.List("catA")[0].Id;
            DelayCommand.Handle("catB", "delay.cancel|" + id.ToString());
            Assert.Single(DelayQueue.List("catA"));
        }
    }
}
