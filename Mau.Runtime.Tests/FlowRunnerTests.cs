using System;
using Mau.Runtime;
using Xunit;

namespace Mau.Runtime.Tests
{
    /// <summary>
    /// FlowRegistry + FlowRunner 单元测试——实体注册机制 + 帧序编排（D2 下沉验证）
    /// </summary>
    public sealed class FlowRunnerTests
    {
        /// <summary>
        /// 测试用 Flow——Tick 计数
        /// </summary>
        private sealed class CountingFlow : IFlow
        {
            /// <summary>
            /// Tick 次数
            /// </summary>
            public int TickCount;

            /// <summary>
            /// 每帧驱动
            /// </summary>
            public void Tick()
            {
                TickCount = TickCount + 1;
            }
        }

        /// <summary>
        /// 组装 FlowRunner——全部机制注入
        /// </summary>
        /// <returns>组装完成的 Runner</returns>
        private static FlowRunner CreateRunner()
        {
            ThreadGuard guard = new ThreadGuard();
            OA oa = new OA(guard);
            CommandBus cmd = new CommandBus(guard);
            IdAllocator ids = new IdAllocator();
            return new FlowRunner(guard, oa, cmd, ids);
        }

        /// <summary>
        /// Registry——注册/查找/回收/快照全流程
        /// </summary>
        [Fact]
        public void Registry_RegisterGetUnregister_Works()
        {
            ThreadGuard guard = new ThreadGuard();
            IdAllocator ids = new IdAllocator();
            FlowRegistry registry = new FlowRegistry(guard, ids);

            CountingFlow a = new CountingFlow();
            CountingFlow b = new CountingFlow();
            long idA = registry.Register(a, "Alpha");
            long idB = registry.Register(b, "Beta");
            Assert.Equal(1, idA);
            Assert.Equal(2, idB);
            Assert.Same(a, registry.Get(idA));
            Assert.Same(b, registry.Get(idB));
            Assert.Null(registry.Get(999));
            Assert.Equal("Alpha", registry.GetName(idA));
            Assert.Equal("Beta", registry.GetName(idB));
            Assert.Equal(2, registry.Count);

            FlowEntry[] entries = registry.Entries;
            Assert.Equal(2, entries.Length);
            Assert.Equal(idA, entries[0].Id);
            Assert.Equal("Alpha", entries[0].Name);
            Assert.Equal("CountingFlow", entries[0].TypeName);

            // 冻结性——外部修改快照不影响注册表
            entries[0].Name = "篡改";
            Assert.Equal("Alpha", registry.GetName(idA));

            Assert.True(registry.Unregister(idA));
            Assert.Null(registry.Get(idA));
            Assert.Equal(1, registry.Count);
            Assert.False(registry.Unregister(idA));
        }

        /// <summary>
        /// Runner——Tick 帧序驱动实体 + 帧号递增
        /// </summary>
        [Fact]
        public void Runner_Tick_DrivesAllFlows()
        {
            FlowRunner runner = CreateRunner();
            CountingFlow a = new CountingFlow();
            CountingFlow b = new CountingFlow();
            long idA = runner.RegisterFlow(a, "A");
            long idB = runner.RegisterFlow(b, "B");

            runner.Tick();
            runner.Tick();
            Assert.Equal(2, a.TickCount);
            Assert.Equal(2, b.TickCount);
            Assert.Equal(2, runner.GetStatus().Frame);
            Assert.Same(a, runner.GetFlow(idA));
            Assert.Same(b, runner.GetFlow(idB));
        }

        /// <summary>
        /// Runner——PostToMain 跨线程桥（后台投递 → 主线程执行）
        /// </summary>
        [Fact]
        public void Runner_PostToMain_ExecutesOnMainThread()
        {
            FlowRunner runner = CreateRunner();
            int mainThreadId = Environment.CurrentManagedThreadId;
            int executedThreadId = -1;
            bool executed = false;

            System.Threading.ManualResetEventSlim posted = new System.Threading.ManualResetEventSlim(false);
            System.Threading.Thread poster = new System.Threading.Thread(new System.Threading.ThreadStart(delegate
            {
                runner.PostToMain(delegate
                {
                    executed = true;
                    executedThreadId = Environment.CurrentManagedThreadId;
                });
                posted.Set();
            }));
            poster.Start();
            posted.Wait(System.Threading.Timeout.InfiniteTimeSpan, TestContext.Current.CancellationToken);

            runner.Tick();
            Assert.True(executed);
            Assert.Equal(mainThreadId, executedThreadId);
        }

        /// <summary>
        /// Runner——指令分发钩子：Set 后帧冻结 → 钩子收到邮件
        /// </summary>
        [Fact]
        public void Runner_CommandDispatch_ReceivesMail()
        {
            ThreadGuard guard = new ThreadGuard();
            OA oa = new OA(guard);
            CommandBus cmd = new CommandBus(guard);
            IdAllocator ids = new IdAllocator();
            FlowRunner runner = new FlowRunner(guard, oa, cmd, ids);

            CommandPack? received = null;
            long receivedId = -1;
            runner.SetCommandDispatch(delegate (long id, CommandPack mail)
            {
                receivedId = id;
                received = mail;
            });

            CountingFlow flow = new CountingFlow();
            long id = runner.RegisterFlow(flow, "CmdCat");
            // 注册指令 key（三段式）
            cmd.Register(id, new string[] { "CH4_Ping_Cat" });

            cmd.Set("CH4_Ping_Cat", 42);
            runner.Tick();
            Assert.NotNull(received);
            Assert.Equal(id, receivedId);
            Assert.Equal(42, received.Value.CmdValues[0]);

            // 取走后不再投递
            received = null;
            runner.Tick();
            Assert.Null(received);
        }

        /// <summary>
        /// Runner——Shutdown 后驱动拒绝；快照含 OA/Command 域摘要
        /// </summary>
        [Fact]
        public void Runner_Shutdown_LocksAndSnapshotComplete()
        {
            FlowRunner runner = CreateRunner();
            CountingFlow flow = new CountingFlow();
            runner.RegisterFlow(flow, "A");
            runner.Tick();

            HostSnapshot status = runner.GetStatus();
            Assert.Equal(1, status.Frame);
            Assert.True(status.IsMainThread);
            Assert.NotNull(status.OA);
            Assert.NotNull(status.Command);
            Assert.Single(status.Flows!);
            Assert.Equal("A", status.Flows![0].Name);

            runner.Shutdown();
            Assert.Throws<InvalidOperationException>(delegate
            {
                runner.Tick();
            });
            Assert.Equal(0, runner.Registry.Count);
        }
    }
}
