using System;
using System.Collections.Generic;
using Mau.Runtime;
using Xunit;

namespace Mau.Runtime.Tests
{
    /// <summary>
    /// QueryBus 统一查询通道测试——③ 线程聚合（D11-D13）：注册/线程域路由/投递/Reset 契约 + ④ sys.summary 聚合视图
    /// 集合：AuditSerial（DataBox 全局静态 Bind/Unbind——M60 静态污染同族）
    /// </summary>
    [Collection("AuditSerial")]
    public sealed class QueryBusTests
    {
        /// <summary>
        /// 构造带 QueryBus 的 SysCommand——summary 处理器自动注册（Main 域）
        /// </summary>
        /// <returns>指令解析器 + 通道</returns>
        private static SysCommand CreateSysWithQueryBus(out QueryBus bus)
        {
            AuditStore store = new AuditStore();
            store.Record("CommandBus", "cmd.set", 1, new AuditProp[] { new AuditProp("key", "chat_x_msg"), new AuditProp("result", "accepted") });
            bus = new QueryBus(1000);
            // 测试用投递器——直接执行（等价主线程已驱动）
            bus.BindMainDispatcher(delegate (Action action, int timeoutMs)
            {
                action();
                return true;
            });
            return new SysCommand(new AuditQuery(store), bus);
        }

        /// <summary>
        /// Any 域——直接执行（调用线程，无投递）
        /// </summary>
        [Fact]
        public void AnyDomain_ExecutesDirectly()
        {
            QueryBus bus = new QueryBus();
            bool invoked = false;
            bus.Register("ping", QueryDomain.Any, delegate (Dictionary<string, string> args)
            {
                invoked = true;
                return "pong";
            });
            string result;
            bool ok = bus.TryExecute("ping", new Dictionary<string, string>(), out result);
            Assert.True(ok);
            Assert.True(invoked);
            Assert.Equal("pong", result);
        }

        /// <summary>
        /// Main 域——经投递器执行（GAP.2 内部机制）
        /// </summary>
        [Fact]
        public void MainDomain_DispatchesToMain()
        {
            QueryBus bus = new QueryBus(1000);
            bool dispatched = false;
            bus.BindMainDispatcher(delegate (Action action, int timeoutMs)
            {
                dispatched = true;
                action();
                return true;
            });
            bus.Register("status", QueryDomain.Main, delegate (Dictionary<string, string> args)
            {
                return "frame=" + (args.ContainsKey("frame") ? args["frame"] : "0");
            });
            Dictionary<string, string> args = new Dictionary<string, string>();
            args["frame"] = "42";
            string result;
            bool ok = bus.TryExecute("status", args, out result);
            Assert.True(ok);
            Assert.True(dispatched);
            Assert.Equal("frame=42", result);
        }

        /// <summary>
        /// Main 域——投递器未绑定时明确失败
        /// </summary>
        [Fact]
        public void MainDomain_NoDispatcher_FailsWithMessage()
        {
            QueryBus bus = new QueryBus();
            bus.Register("status", QueryDomain.Main, delegate (Dictionary<string, string> args)
            {
                return "ok";
            });
            string result;
            bool ok = bus.TryExecute("status", new Dictionary<string, string>(), out result);
            Assert.False(ok);
            Assert.Contains("主线程投递器未绑定", result);
        }

        /// <summary>
        /// Main 域——投递超时明确失败（不挂死）
        /// </summary>
        [Fact]
        public void MainDomain_DispatcherTimeout_FailsWithMessage()
        {
            QueryBus bus = new QueryBus(1000);
            bus.BindMainDispatcher(delegate (Action action, int timeoutMs)
            {
                return false;
            });
            bus.Register("status", QueryDomain.Main, delegate (Dictionary<string, string> args)
            {
                return "ok";
            });
            string result;
            bool ok = bus.TryExecute("status", new Dictionary<string, string>(), out result);
            Assert.False(ok);
            Assert.Contains("超时", result);
        }

        /// <summary>
        /// 处理器异常——不扩散，结果含错误块
        /// </summary>
        [Fact]
        public void HandlerException_ReturnsErrorResult()
        {
            QueryBus bus = new QueryBus(1000);
            bus.BindMainDispatcher(delegate (Action action, int timeoutMs)
            {
                action();
                return true;
            });
            bus.Register("boom", QueryDomain.Main, delegate (Dictionary<string, string> args)
            {
                throw new InvalidOperationException("boom-msg");
            });
            string result;
            bool ok = bus.TryExecute("boom", new Dictionary<string, string>(), out result);
            Assert.True(ok);
            Assert.Contains("错误", result);
            Assert.Contains("boom-msg", result);
        }

        /// <summary>
        /// 注销——之后执行失败
        /// </summary>
        [Fact]
        public void Unregister_RemovesHandler()
        {
            QueryBus bus = new QueryBus();
            bus.Register("ping", QueryDomain.Any, delegate (Dictionary<string, string> args)
            {
                return "pong";
            });
            Assert.True(bus.Unregister("ping"));
            string result;
            bool ok = bus.TryExecute("ping", new Dictionary<string, string>(), out result);
            Assert.False(ok);
            Assert.Contains("未注册", result);
            Assert.False(bus.Unregister("ping"));
        }

        /// <summary>
        /// 同名注册——覆盖（最后生效）
        /// </summary>
        [Fact]
        public void ReRegister_OverridesHandler()
        {
            QueryBus bus = new QueryBus();
            bus.Register("v", QueryDomain.Any, delegate (Dictionary<string, string> args)
            {
                return "old";
            });
            bus.Register("v", QueryDomain.Any, delegate (Dictionary<string, string> args)
            {
                return "new";
            });
            string result;
            bool ok = bus.TryExecute("v", new Dictionary<string, string>(), out result);
            Assert.True(ok);
            Assert.Equal("new", result);
            Assert.Equal(1, bus.Count);
        }

        /// <summary>
        /// 未注册——明确失败
        /// </summary>
        [Fact]
        public void UnknownName_ReturnsFailure()
        {
            QueryBus bus = new QueryBus();
            string result;
            bool ok = bus.TryExecute("nope", new Dictionary<string, string>(), out result);
            Assert.False(ok);
            Assert.Contains("未注册", result);
        }

        /// <summary>
        /// Reset——清空注册 + 解绑投递器（D26 契约）
        /// </summary>
        [Fact]
        public void Reset_ClearsAll()
        {
            QueryBus bus = new QueryBus();
            bus.BindMainDispatcher(delegate (Action action, int timeoutMs)
            {
                action();
                return true;
            });
            bus.Register("a", QueryDomain.Any, delegate (Dictionary<string, string> args)
            {
                return "a";
            });
            bus.Register("b", QueryDomain.Main, delegate (Dictionary<string, string> args)
            {
                return "b";
            });
            bus.Reset();
            Assert.Equal(0, bus.Count);
            string result;
            bool ok = bus.TryExecute("a", new Dictionary<string, string>(), out result);
            Assert.False(ok);
            bus.Register("c", QueryDomain.Main, delegate (Dictionary<string, string> args)
            {
                return "c";
            });
            ok = bus.TryExecute("c", new Dictionary<string, string>(), out result);
            Assert.False(ok);
            Assert.Contains("投递器未绑定", result);
        }

        /// <summary>
        /// 空名注册——拒绝
        /// </summary>
        [Fact]
        public void Register_EmptyName_Throws()
        {
            QueryBus bus = new QueryBus();
            Assert.Throws<ArgumentException>(delegate ()
            {
                bus.Register("", QueryDomain.Any, delegate (Dictionary<string, string> args)
                {
                    return "";
                });
            });
            Assert.Throws<ArgumentNullException>(delegate ()
            {
                bus.Register("x", QueryDomain.Any, null!);
            });
        }

        /// <summary>
        /// Names——注册清单
        /// </summary>
        [Fact]
        public void Names_ListsRegistered()
        {
            QueryBus bus = new QueryBus();
            bus.Register("a", QueryDomain.Any, delegate (Dictionary<string, string> args)
            {
                return "a";
            });
            bus.Register("b", QueryDomain.Main, delegate (Dictionary<string, string> args)
            {
                return "b";
            });
            string[] names = bus.Names();
            Assert.Equal(2, names.Length);
            Assert.Contains("a", names);
            Assert.Contains("b", names);
        }

        /// <summary>
        /// sys.query——经通道出口执行（未绑定 QueryBus 时明确错误）
        /// </summary>
        [Fact]
        public void SysQuery_ExecutesThroughBus()
        {
            SysCommand sys;
            QueryBus bus;
            sys = CreateSysWithQueryBus(out bus);
            bus.Register("flow.status", QueryDomain.Any, delegate (Dictionary<string, string> args)
            {
                return "## FLOW.STATUS\n- frame=7\n- flows=2";
            });
            string text = sys.Execute("sys.query name=flow.status");
            Assert.Contains("## FLOW.STATUS", text);
            Assert.Contains("frame=7", text);
            string noName = sys.Execute("sys.query");
            Assert.Contains("用法", noName);
            string unknown = sys.Execute("sys.query name=nope");
            Assert.Contains("未注册", unknown);
            SysCommand plain = new SysCommand(new AuditQuery(new AuditStore()));
            string noBus = plain.Execute("sys.query name=flow.status");
            Assert.Contains("QueryBus 未绑定", noBus);
        }

        /// <summary>
        /// sys.summary——无宿主绑定时降级输出（不炸）
        /// </summary>
        [Fact]
        public void SysSummary_DegradesWithoutHost()
        {
            SysCommand sys;
            QueryBus bus;
            sys = CreateSysWithQueryBus(out bus);
            string text = sys.Execute("sys.summary");
            Assert.Contains("## SYS.SUMMARY", text);
            Assert.Contains("无 FlowRunner", text);
        }
    }
}
