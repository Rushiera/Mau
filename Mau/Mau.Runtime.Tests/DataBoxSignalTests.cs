#nullable disable
using System;
using System.Collections.Generic;
using Mau.Runtime;
using Xunit;

namespace Mau.Runtime.Tests
{
    /// <summary>
    /// DataBox 事件层测试——内部交互总线（P3.1）：注册/投递/消费/未注册抛异常/覆盖合并/审计埋点
    /// </summary>
    [CollectionDefinition("DataBoxSignalSerial", DisableParallelization = true)]
    public sealed class DataBoxSignalSerialCollection
    {
    }

    /// <summary>
    /// 事件层行为测试——静态状态需串行
    /// </summary>
    [Collection("DataBoxSignalSerial")]
    public sealed class DataBoxSignalTests
    {
        /// <summary>
        /// 每个测试清理信号注册——finally 恢复现场
        /// </summary>
        public DataBoxSignalTests()
        {
            DataBox.ResetSignals();
        }

        /// <summary>
        /// 注册 + 投递 + 消费——沿置位返回 true 并清除，二次查询 false
        /// </summary>
        [Fact]
        public void Signal_Poll_EdgeConsumed()
        {
            DataBox.RegisterSignal("P_Go");
            DataBox.Signal("P_Go");
            Assert.True(DataBox.TryPoll("P_Go"));
            Assert.False(DataBox.TryPoll("P_Go"));
        }

        /// <summary>
        /// 覆盖合并——消费前多次投递只算一个沿（信号无队列——特征）
        /// </summary>
        [Fact]
        public void Signal_MultiplePosts_MergeToSingleEdge()
        {
            DataBox.RegisterSignal("P_Go");
            DataBox.Signal("P_Go");
            DataBox.Signal("P_Go");
            DataBox.Signal("P_Go");
            Assert.True(DataBox.TryPoll("P_Go"));
            Assert.False(DataBox.TryPoll("P_Go"));
        }

        /// <summary>
        /// 未注册投递——InvalidOperationException（与 CommandBus 未注册 Key REJECT 同语义）
        /// </summary>
        [Fact]
        public void Signal_Unregistered_Throws()
        {
            Assert.Throws<InvalidOperationException>(delegate ()
            {
                DataBox.Signal("P_NoSuch");
            });
        }

        /// <summary>
        /// 未注册消费——InvalidOperationException
        /// </summary>
        [Fact]
        public void TryPoll_Unregistered_Throws()
        {
            Assert.Throws<InvalidOperationException>(delegate ()
            {
                DataBox.TryPoll("P_NoSuch");
            });
        }

        /// <summary>
        /// 重复注册幂等——不抛异常
        /// </summary>
        [Fact]
        public void RegisterSignal_Idempotent()
        {
            DataBox.RegisterSignal("P_X");
            DataBox.RegisterSignal("P_X");
            Assert.Contains("P_X", DataBox.SignalNames());
        }

        /// <summary>
        /// 信号清单——排序稳定
        /// </summary>
        [Fact]
        public void SignalNames_Sorted()
        {
            DataBox.RegisterSignal("P_B");
            DataBox.RegisterSignal("P_A");
            string[] names = DataBox.SignalNames();
            // 排序稳定性断言——P_A 在 P_B 之前（SignalNames 全量排序；并行测试类可能注册其他信号名，不做数量断言——判例：128 测试并行撞注册残留）
            int ia = Array.IndexOf(names, "P_A");
            int ib = Array.IndexOf(names, "P_B");
            Assert.True(ia >= 0 && ib >= 0, "P_A/P_B 均应在信号清单中");
            Assert.True(ia < ib, "P_A 应排在 P_B 之前");
        }
        /// <summary>
        /// 审计埋点——signal.post / signal.consume 为 trace 级噪声（persistable=false——O2 定案：不产生审计）
        /// </summary>
        [Fact]
        public void Signal_AuditEvents()
        {
            LogStore.ClearForTest();
            AuditStore audit = new AuditStore();
            AuditStore.Default = audit;
            try
            {
                DataBox.RegisterSignal("P_Audit");
                DataBox.Signal("P_Audit");
                DataBox.TryPoll("P_Audit");
                AuditEvent[] snap = audit.Snapshot();
                // O2：signal.* 高频噪声从审计面出局（DataBox 调用侧 persistable=false）——0 条
                Assert.Empty(snap);
            }
            finally
            {
                AuditStore.Default = null;
                audit.Shutdown();
            }
        }

        /// <summary>
        /// 未注册审计不产生（Signal 抛异常在审计前）
        /// </summary>
        [Fact]
        public void Signal_Unregistered_NoAuditEvent()
        {
            LogStore.ClearForTest();
            AuditStore audit = new AuditStore();
            AuditStore.Default = audit;
            try
            {
                Assert.Throws<InvalidOperationException>(delegate ()
                {
                    DataBox.Signal("P_Ghost");
                });
                Assert.Empty(audit.Snapshot());
            }
            finally
            {
                AuditStore.Default = null;
                audit.Shutdown();
            }
        }
    }
}
