using Mau.Runtime;
using Xunit;

namespace Mau.Runtime.Tests
{
    /// <summary>
    /// sys.* 指令解析器测试——A.5 基座能力（--cmd/UI/管道统一指令入口）
    /// 隔离：AuditStore 内存模式（不触碰 Default）；DataBox 测试后 ClearAll
    /// </summary>
    public sealed class SysCommandTests
    {
        /// <summary>
        /// 构造带预置事件的 SysCommand——模拟六机制埋点结果
        /// </summary>
        /// <returns>指令解析器</returns>
        private static SysCommand CreateSysWithEvents()
        {
            AuditStore store = new AuditStore();
            // CommandBus
            store.Record("CommandBus", "cmd.register", 1, new AuditProp[] { new AuditProp("owner", "1"), new AuditProp("keys", "chat_x_msg"), new AuditProp("result", "accepted") });
            store.Record("CommandBus", "cmd.set", 2, new AuditProp[] { new AuditProp("key", "chat_x_msg"), new AuditProp("payload", "str:4:\"你好\""), new AuditProp("result", "accepted") });
            store.Record("CommandBus", "cmd.consume", 3, new AuditProp[] { new AuditProp("owner", "1"), new AuditProp("keys", "chat_x_msg") });
            // OA
            store.Record("OA", "oa.post", 4, new AuditProp[] { new AuditProp("officeId", "7"), new AuditProp("name", "ORDER") });
            store.Record("OA", "oa.claim", 5, new AuditProp[] { new AuditProp("officeId", "7"), new AuditProp("catId", "2"), new AuditProp("result", "claimed") });
            store.Record("OA", "oa.complete", 6, new AuditProp[] { new AuditProp("officeId", "7"), new AuditProp("result", "ints:0,strs:1") });
            // FlowRunner
            store.Record("FlowRunner", "flow.register", 7, new AuditProp[] { new AuditProp("flowId", "10"), new AuditProp("name", "Alpha"), new AuditProp("kind", "flow") });
            // trace（生成物）
            store.Record("Flow", "trace.fire", 8, new AuditProp[] { new AuditProp("flow", "FL_Alpha"), new AuditProp("name", "P_Go"), new AuditProp("result", "fire"), new AuditProp("flow_frame", "3") }, false);
            // 配置
            store.Record("ConfigStore", "cfg.change", 9, new AuditProp[] { new AuditProp("key", "endpoint"), new AuditProp("value", "str:8:\"https://x\"") });
            // 日志
            store.Record("RuntimeLog", "log.error", 10, new AuditProp[] { new AuditProp("message", "str:5:\"boom\"") });
            return new SysCommand(new AuditQuery(store));
        }

        /// <summary>
        /// sys.audit——类别过滤 + tail 截断
        /// </summary>
        [Fact]
        public void SysAudit_FiltersByCategoryAndTail()
        {
            SysCommand sys = CreateSysWithEvents();
            string text = sys.Execute("sys.audit category=cmd.set tail=1");
            Assert.Contains("## SYS.AUDIT", text);
            Assert.Contains("## E0000002", text);
            Assert.DoesNotContain("## E0000003", text);
        }

        /// <summary>
        /// sys.cmd——指定 key 的投递/消费记录
        /// </summary>
        [Fact]
        public void SysCmd_FindsKeyHistory()
        {
            SysCommand sys = CreateSysWithEvents();
            string text = sys.Execute("sys.cmd key=chat_x_msg");
            Assert.Contains("## SYS.CMD", text);
            Assert.Contains("cmd.set", text);
            Assert.Contains("cmd.consume", text);
            Assert.DoesNotContain("oa.post", text);
        }

        /// <summary>
        /// sys.keys——key 注册清单
        /// </summary>
        [Fact]
        public void SysKeys_ListsRegistrations()
        {
            SysCommand sys = CreateSysWithEvents();
            string text = sys.Execute("sys.keys owner=1");
            Assert.Contains("## SYS.KEYS", text);
            Assert.Contains("cmd.register", text);
            Assert.Contains("chat_x_msg", text);
            string none = sys.Execute("sys.keys owner=99");
            Assert.Contains("（无匹配）", none);
        }

        /// <summary>
        /// sys.oa——工单生命周期（id 全链路）
        /// </summary>
        [Fact]
        public void SysOa_LifecycleById()
        {
            SysCommand sys = CreateSysWithEvents();
            string text = sys.Execute("sys.oa id=7");
            Assert.Contains("## SYS.OA", text);
            Assert.Contains("oa.post", text);
            Assert.Contains("oa.claim", text);
            Assert.Contains("oa.complete", text);
            Assert.DoesNotContain("cmd.set", text);
        }

        /// <summary>
        /// sys.trace——生成物执行序列（flow 过滤）
        /// </summary>
        [Fact]
        public void SysTrace_FiltersByFlow()
        {
            SysCommand sys = CreateSysWithEvents();
            string text = sys.Execute("sys.trace flow=FL_Alpha");
            Assert.Contains("## SYS.TRACE", text);
            Assert.Contains("trace.fire", text);
            Assert.Contains("P_Go", text);
            Assert.DoesNotContain("flow.register", text);
        }

        /// <summary>
        /// sys.logs——日志查询
        /// </summary>
        [Fact]
        public void SysLogs_ReturnsLogEvents()
        {
            SysCommand sys = CreateSysWithEvents();
            string text = sys.Execute("sys.logs");
            Assert.Contains("## SYS.LOGS", text);
            Assert.Contains("log.error", text);
        }

        /// <summary>
        /// sys.box——DataBox 当前键值
        /// </summary>
        [Fact]
        public void SysBox_ReadsCurrentData()
        {
            try
            {
                DataBox.Set("llm", "endpoint", "https://api.example.com");
                DataBox.Set("llm", "model", "deepseek-v4");
                SysCommand sys = CreateSysWithEvents();
                string text = sys.Execute("sys.box scope=llm");
                Assert.Contains("## SYS.BOX", text);
                Assert.Contains("endpoint", text);
                Assert.Contains("https://api.example.com", text);
                string byKey = sys.Execute("sys.box scope=llm key=model");
                Assert.Contains("deepseek-v4", byKey);
                Assert.DoesNotContain("endpoint", byKey);
                string none = sys.Execute("sys.box scope=nope");
                Assert.Contains("（无匹配）", none);
            }
            finally
            {
                DataBox.ClearScope("llm");
            }
        }

        /// <summary>
        /// sys.conf——配置变更记录（key 过滤）
        /// </summary>
        [Fact]
        public void SysConf_RecentChanges()
        {
            SysCommand sys = CreateSysWithEvents();
            string text = sys.Execute("sys.conf key=endpoint");
            Assert.Contains("## SYS.CONF", text);
            Assert.Contains("cfg.change", text);
            Assert.DoesNotContain("cmd.set", text);
        }

        /// <summary>
        /// sys.flow——实体生命周期记录
        /// </summary>
        [Fact]
        public void SysFlow_ListsLifecycle()
        {
            SysCommand sys = CreateSysWithEvents();
            string text = sys.Execute("sys.flow");
            Assert.Contains("## SYS.FLOW", text);
            Assert.Contains("flow.register", text);
            Assert.Contains("Alpha", text);
        }

        /// <summary>
        /// 未知指令——返回用法提示
        /// </summary>
        [Fact]
        public void UnknownCommand_ReturnsUsage()
        {
            SysCommand sys = CreateSysWithEvents();
            string text = sys.Execute("sys.nope");
            Assert.Contains("未知指令", text);
            Assert.Contains("SYS USAGE", text);
            string empty = sys.Execute("");
            Assert.Contains("SYS USAGE", empty);
        }

        /// <summary>
        /// 无匹配——明确输出（无匹配）
        /// </summary>
        [Fact]
        public void NoMatch_ReturnsNoMatch()
        {
            SysCommand sys = CreateSysWithEvents();
            string text = sys.Execute("sys.trace flow=FL_Nope");
            Assert.Contains("（无匹配）", text);
        }

        /// <summary>
        /// G2 实时源——sys.keys 输出当前注册态（ICommandBus 绑定时）
        /// </summary>
        [Fact]
        public void SysKeys_RealtimeSection_WhenCommandBusBound()
        {
            ThreadGuard guard = new ThreadGuard();
            CommandBus bus = new CommandBus(guard);
            bus.OpenInput();
            bus.Register(1, new string[] { "chat_a_msg" });
            bus.Register(2, new string[] { "talk_a_stop" });
            try
            {
                DataBox.Bind<ICommandBus>(bus);
                SysCommand sys = CreateSysWithEvents();
                string text = sys.Execute("sys.keys");
                Assert.Contains("## 当前注册（实时）", text);
                Assert.Contains("chat_a_msg", text);
                Assert.Contains("talk_a_stop", text);
                Assert.Contains("## 注册历史（审计）", text);
            }
            finally
            {
                DataBox.Unbind<ICommandBus>();
                bus.Unregister(1);
                bus.Unregister(2);
            }
        }

        /// <summary>
        /// G2 实时源——sys.flow 输出当前实体清单（FlowRunner 绑定时）
        /// </summary>
        [Fact]
        public void SysFlow_RealtimeSection_WhenRunnerBound()
        {
            ThreadGuard guard = new ThreadGuard();
            OA oa = new OA(guard);
            CommandBus cmd = new CommandBus(guard);
            IdAllocator ids = new IdAllocator();
            FlowRunner runner = new FlowRunner(guard, oa, cmd, ids);
            try
            {
                DataBox.Bind<FlowRunner>(runner);
                SysCommand sys = CreateSysWithEvents();
                string text = sys.Execute("sys.flow");
                Assert.Contains("## 当前实体（实时）", text);
                Assert.Contains("## 生命周期（审计）", text);
            }
            finally
            {
                DataBox.Unbind<FlowRunner>();
            }
        }

        /// <summary>
        /// G2 实时源——sys.oa 输出当前工单统计（IOA 绑定时）
        /// </summary>
        [Fact]
        public void SysOa_RealtimeSection_WhenOaBound()
        {
            ThreadGuard guard = new ThreadGuard();
            OA oa = new OA(guard);
            try
            {
                DataBox.Bind<IOA>(oa);
                SysCommand sys = CreateSysWithEvents();
                string text = sys.Execute("sys.oa");
                Assert.Contains("## 当前工单（实时）", text);
                Assert.Contains("## 工单历史（审计）", text);
            }
            finally
            {
                DataBox.Unbind<IOA>();
            }
        }
    }
}
