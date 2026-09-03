using System;
using System.Collections.Generic;
using Mau.Runtime;
using Xunit;

namespace Mau.Runtime.Tests
{
    /// <summary>
    /// 审计集成测试——A.2 机制埋点（CommandBus/OA/FlowRunner/ConfigStore/RuntimeLog）
    /// 隔离：AuditStore 内存模式（不落盘）；LlmBridge 静态测试单独串行集合
    /// </summary>
    [Collection("AuditSerial")]
    public sealed class AuditIntegrationTests
    {
        /// <summary>
        /// 测试用 Flow——仅计数 Tick
        /// </summary>
        private sealed class CountingFlow : IFlow
        {
            /// <summary>
            /// Tick 计数
            /// </summary>
            public int TickCount;

            /// <summary>
            /// 每帧驱动——计数（帧号注入忽略）
            /// </summary>
            /// <param name="frame">宿主帧号</param>
            public void Tick(int frame)
            {
                TickCount = TickCount + 1;
            }

            /// <summary>
            /// Flow 自曝元数据——测试桩固定空组
            /// </summary>
            public string GetMetaJson()
            {
                return "{\"group\":\"\",\"claims\":[]}";
            }

            /// <summary>
            /// Flow 自曝工具定义——测试桩固定空工具组
            /// </summary>
            public string GetToolsJson()
            {
                return "{\"group\":\"\",\"tools\":[]}";
            }
        }

        /// <summary>
        /// 从事件属性中取指定键的值——不存在返回空串
        /// </summary>
        /// <param name="e">事件</param>
        /// <param name="key">属性键</param>
        /// <returns>属性值</returns>
        private static string FindProp(AuditEvent e, string key)
        {
            for (int i = 0; i < e.Props.Length; i = i + 1)
            {
                if (e.Props[i].Key == key)
                {
                    return e.Props[i].Value;
                }
            }
            return "";
        }

        /// <summary>
        /// CommandBus 埋点——注册/投递/拒绝/消费/注销全类别 + 载荷摘要
        /// </summary>
        [Fact]
        public void CommandBus_RecordsRegisterSetConsumeClean()
        {
            LogStore.ClearForTest();
            ThreadGuard guard = new ThreadGuard();
            CommandBus bus = new CommandBus(guard);
            AuditStore audit = new AuditStore();
            bus.Audit = audit;
            try
            {
                bus.Register(1, new string[] { "chat_x_msg", "talk_x_stop" });
                bus.SetText("chat_x_msg", "你好世界", "test");
                bus.SetText("nokey", "x", "test");
                bus.BeginTickInput();
                bus.GetCommandEmail(1);
                bus.Unregister(1);

                AuditEvent[] snap = audit.Snapshot();
                Assert.Equal(5, snap.Length);
                // cmd.register accepted
                Assert.Equal("CommandBus", snap[0].Source);
                Assert.Equal("cmd.register", snap[0].Category);
                Assert.Equal("accepted", FindProp(snap[0], "result"));
                Assert.Contains("chat_x_msg", FindProp(snap[0], "keys"));
                // cmd.set accepted + 摘要
                Assert.Equal("cmd.set", snap[1].Category);
                Assert.Equal("accepted", FindProp(snap[1], "result"));
                Assert.Equal("str:4:\"你好世界\"", FindProp(snap[1], "payload"));
                // cmd.set rejected 未注册
                Assert.Equal("cmd.set", snap[2].Category);
                Assert.Equal("rejected", FindProp(snap[2], "result"));
                Assert.Equal("未注册", FindProp(snap[2], "reason"));
                // cmd.consume
                Assert.Equal("cmd.consume", snap[3].Category);
                Assert.Contains("chat_x_msg", FindProp(snap[3], "keys"));
                // cmd.clean unregister
                Assert.Equal("cmd.clean", snap[4].Category);
                Assert.Equal("unregister", FindProp(snap[4], "reason"));
            }
            finally
            {
                audit.Shutdown();
            }
        }

        /// <summary>
        /// CommandBus 重复注册——拒绝事件带原因
        /// </summary>
        [Fact]
        public void CommandBus_DuplicateRegister_RecordsReject()
        {
            LogStore.ClearForTest();
            ThreadGuard guard = new ThreadGuard();
            CommandBus bus = new CommandBus(guard);
            AuditStore audit = new AuditStore();
            bus.Audit = audit;
            try
            {
                bus.Register(1, new string[] { "cat_a1_msg" });
                bus.Register(1, new string[] { "cat_a2_msg" });
                AuditEvent[] snap = audit.Snapshot();
                Assert.Equal(2, snap.Length);
                Assert.Equal("accepted", FindProp(snap[0], "result"));
                Assert.Equal("cmd.register", snap[1].Category);
                Assert.Equal("rejected", FindProp(snap[1], "result"));
                Assert.Equal("重复注册", FindProp(snap[1], "reason"));
            }
            finally
            {
                audit.Shutdown();
            }
        }

        /// <summary>
        /// OA 埋点——post/claim/complete/settle 全生命周期类别
        /// </summary>
        [Fact]
        public void OA_RecordsPostClaimCompleteSettle()
        {
            LogStore.ClearForTest();
            ThreadGuard guard = new ThreadGuard();
            OA oa = new OA(guard);
            AuditStore audit = new AuditStore();
            oa.Audit = audit;
            try
            {
                long officeId = oa.Post(1, "TEST", "ORDER", 100);
                List<Office> claimed = oa.ClaimBatch(2, new long[] { officeId });
                Assert.Single(claimed);
                OfficeData result = OfficeData.Empty();
                result.Strs["k"] = "v";
                oa.Complete(officeId, 2, result);

                long tOfficeId = oa.Post(1, "TEST", "TIMEOUT", 1);
                oa.Tick();
                oa.Tick();

                AuditEvent[] snap = audit.Snapshot();
                Assert.Equal(5, snap.Length);
                // oa.post
                Assert.Equal("oa.post", snap[0].Category);
                Assert.Equal("ORDER", FindProp(snap[0], "name"));
                // oa.claim
                Assert.Equal("oa.claim", snap[1].Category);
                Assert.Equal("claimed", FindProp(snap[1], "result"));
                // oa.complete + 回执摘要
                Assert.Equal("oa.complete", snap[2].Category);
                Assert.Equal("ints:0,strs:1", FindProp(snap[2], "result"));
                // 第二个 post
                Assert.Equal("oa.post", snap[3].Category);
                // oa.settle
                Assert.Equal("oa.settle", snap[4].Category);
                Assert.Equal(tOfficeId.ToString(), FindProp(snap[4], "officeId"));
            }
            finally
            {
                audit.Shutdown();
            }
        }

        /// <summary>
        /// FlowRunner——全局帧号驱动 + flow.register/unregister + flow.tick 默认关
        /// </summary>
        [Fact]
        public void FlowRunner_RecordsFrameRegisterUnregister()
        {
            LogStore.ClearForTest();
            ThreadGuard guard = new ThreadGuard();
            OA oa = new OA(guard);
            CommandBus cmd = new CommandBus(guard);
            IdAllocator ids = new IdAllocator();
            FlowRunner runner = new FlowRunner(guard, oa, cmd, ids);
            AuditStore audit = new AuditStore();
            runner.Audit = audit;
            try
            {
                CountingFlow flow = new CountingFlow();
                long flowId = runner.RegisterFlow(flow, "Alpha");
                runner.Tick();
                runner.Tick();
                // 全局帧号已驱动
                Assert.Equal(2L, audit.CurrentFrame);
                // flow.tick 默认关
                bool hasTick = false;
                AuditEvent[] snap = audit.Snapshot();
                for (int i = 0; i < snap.Length; i = i + 1)
                {
                    if (snap[i].Category == "flow.tick")
                    {
                        hasTick = true;
                    }
                }
                Assert.False(hasTick);
                // 开启后下一帧有 flow.tick
                audit.EnableTickEvents = true;
                runner.Tick();
                snap = audit.Snapshot();
                Assert.Equal("flow.tick", snap[snap.Length - 1].Category);
                Assert.Equal("1", FindProp(snap[snap.Length - 1], "entities"));
                // 注销
                runner.UnregisterFlow(flowId);
                snap = audit.Snapshot();
                Assert.Equal("flow.unregister", snap[snap.Length - 1].Category);
            }
            finally
            {
                audit.Shutdown();
            }
        }

        /// <summary>
        /// ConfigStore——cfg.change 事件 + 值摘要（超 16 字符截断）
        /// </summary>
        [Fact]
        public void ConfigStore_Set_RecordsChangeWithSummary()
        {
            LogStore.ClearForTest();
            ConfigStore cfg = new ConfigStore();
            AuditStore audit = new AuditStore();
            cfg.Audit = audit;
            try
            {
                cfg.Set("endpoint", "http://example.com/long-endpoint-url");
                AuditEvent[] snap = audit.Snapshot();
                Assert.Single(snap);
                Assert.Equal("cfg.change", snap[0].Category);
                Assert.Equal("endpoint", FindProp(snap[0], "key"));
                Assert.Equal("str:36:\"http://example.c\"", FindProp(snap[0], "value"));
            }
            finally
            {
                audit.Shutdown();
            }
        }

        /// <summary>
        /// RuntimeLog——错误通道并入审计（log.error + 消息摘要）
        /// </summary>
        [Fact]
        public void RuntimeLog_ErrorOut_RecordsLogError()
        {
            LogStore.ClearForTest();
            AuditStore audit = new AuditStore();
            AuditStore.Default = audit;
            try
            {
                RuntimeLog.ErrorOut("测试错误消息");
                AuditEvent[] snap = audit.Snapshot();
                Assert.Single(snap);
                Assert.Equal("log.error", snap[0].Category);
                Assert.Equal("str:6:\"测试错误消息\"", FindProp(snap[0], "message"));
            }
            finally
            {
                AuditStore.Default = null;
                audit.Shutdown();
            }
        }
    }

    /// <summary>
    /// 审计静态测试集合定义——AuditStore.Default 静态状态需串行执行
    /// </summary>
    [CollectionDefinition("AuditSerial", DisableParallelization = true)]
    public sealed class AuditSerialCollection
    {
    }
}
