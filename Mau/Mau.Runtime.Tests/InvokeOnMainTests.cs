using System;
using Mau.Runtime;
using Xunit;

namespace Mau.Runtime.Tests
{
    /// <summary>
    /// InvokeOnMain 原语 + 查询 API 线程安全契约测试（2026-08-10 基建评审 #1/#2）。
    /// 覆盖：主线程投递等待/结果回传/超时/异常传播 + CommandBus 锁内快照跨线程 + SysCommand 守卫路径经 InvokeOnMain。
    /// 集合：AuditSerial（DataBox 全局静态 Bind/Unbind——M60 静态污染同族）
    /// </summary>
    [Collection("AuditSerial")]
    public sealed class InvokeOnMainTests
    {
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
        /// 驱动主线程 Tick 直到完成标志置位或超时——worker 投递与主线程消费竞态消除
        /// </summary>
        /// <param name="runner">帧序宿主</param>
        /// <param name="done">完成标志</param>
        private static void DriveUntilDone(FlowRunner runner, System.Threading.ManualResetEventSlim done)
        {
            long deadline = Environment.TickCount64 + 5000;
            while (!done.IsSet && Environment.TickCount64 < deadline)
            {
                runner.Tick();
                System.Threading.Thread.Sleep(1);
            }
            Assert.True(done.IsSet, "worker 未在 5 秒内完成");
        }

        /// <summary>
        /// InvokeOnMain&lt;T&gt;——工作线程投递 → 主线程执行 → 结果回传
        /// </summary>
        [Fact]
        public void InvokeOnMain_Result_ExecutesOnMainThread()
        {
            FlowRunner runner = CreateRunner();
            int mainThreadId = Environment.CurrentManagedThreadId;
            int executedThreadId = -1;
            int result = -1;

            System.Threading.ManualResetEventSlim done = new System.Threading.ManualResetEventSlim(false);
            System.Threading.Thread worker = new System.Threading.Thread(new System.Threading.ThreadStart(delegate
            {
                bool ok = runner.InvokeOnMain<int>(delegate
                {
                    executedThreadId = Environment.CurrentManagedThreadId;
                    return 42;
                }, 3000, out result);
                Assert.True(ok);
                done.Set();
            }));
            worker.Start();
            DriveUntilDone(runner, done);
            worker.Join(1000);
            Assert.Equal(42, result);
            Assert.Equal(mainThreadId, executedThreadId);
        }

        /// <summary>
        /// InvokeOnMain（Action 版本）——工作线程投递 → 主线程执行
        /// </summary>
        [Fact]
        public void InvokeOnMain_Action_ExecutesOnMainThread()
        {
            FlowRunner runner = CreateRunner();
            int mainThreadId = Environment.CurrentManagedThreadId;
            int executedThreadId = -1;

            System.Threading.ManualResetEventSlim done = new System.Threading.ManualResetEventSlim(false);
            System.Threading.Thread worker = new System.Threading.Thread(new System.Threading.ThreadStart(delegate
            {
                bool ok = runner.InvokeOnMain(delegate
                {
                    executedThreadId = Environment.CurrentManagedThreadId;
                }, 3000);
                Assert.True(ok);
                done.Set();
            }));
            worker.Start();
            DriveUntilDone(runner, done);
            worker.Join(1000);
            Assert.Equal(mainThreadId, executedThreadId);
        }

        /// <summary>
        /// InvokeOnMain——主线程未驱动（不 Tick）→ 超时返回 false（不挂死）
        /// </summary>
        [Fact]
        public void InvokeOnMain_Timeout_WhenMainNotDriving()
        {
            FlowRunner runner = CreateRunner();
            bool ok = true;
            System.Threading.Thread worker = new System.Threading.Thread(new System.Threading.ThreadStart(delegate
            {
                ok = runner.InvokeOnMain(delegate
                {
                    // 不应执行
                }, 300);
            }));
            worker.Start();
            // 不驱动 Tick——Inbox 无人消费
            worker.Join(3000);
            Assert.False(ok);
        }

        /// <summary>
        /// InvokeOnMain——回调异常在调用线程重抛（不吞、不挂死）
        /// </summary>
        [Fact]
        public void InvokeOnMain_PropagatesCallbackException()
        {
            FlowRunner runner = CreateRunner();
            System.Exception? caught = null;

            System.Threading.ManualResetEventSlim done = new System.Threading.ManualResetEventSlim(false);
            System.Threading.Thread worker = new System.Threading.Thread(new System.Threading.ThreadStart(delegate
            {
                try
                {
                    runner.InvokeOnMain(delegate
                    {
                        throw new InvalidOperationException("回调失败");
                    }, 3000);
                }
                catch (System.Exception ex)
                {
                    caught = ex;
                }
                done.Set();
            }));
            worker.Start();
            DriveUntilDone(runner, done);
            worker.Join(1000);
            Assert.NotNull(caught);
            Assert.Contains("回调失败", caught!.Message);
        }

        /// <summary>
        /// CommandBus 锁内快照——工作线程直调 GetSnapshot/GetKeyDic 不抛（基建评审 #1 锁内化验证）
        /// </summary>
        [Fact]
        public void CommandBus_ThreadSafeSnapshot_CrossThreadOk()
        {
            ThreadGuard guard = new ThreadGuard();
            CommandBus cmd = new CommandBus(guard);
            cmd.Register(1, new string[] { "CH4_Ping_Cat", "CH4_Ping_Dog" });

            System.Exception? caught = null;
            string[] keyLines = Array.Empty<string>();
            System.Threading.ManualResetEventSlim done = new System.Threading.ManualResetEventSlim(false);
            System.Threading.Thread worker = new System.Threading.Thread(new System.Threading.ThreadStart(delegate
            {
                try
                {
                    CommandSnapshot snap = cmd.GetSnapshot();
                    Assert.Equal(2, snap.RegisteredKeyCount);
                    keyLines = cmd.GetKeyDic();
                }
                catch (System.Exception ex)
                {
                    caught = ex;
                }
                done.Set();
            }));
            worker.Start();
            Assert.True(done.Wait(2000, TestContext.Current.CancellationToken));
            worker.Join(1000);
            Assert.Null(caught);
            Assert.True(keyLines.Length >= 1);
        }

        /// <summary>
        /// SysCommand 守卫路径——工作线程直调 sys.flow（runner.GetStatus 守卫）抛异常；经 InvokeOnMain 成功
        /// </summary>
        [Fact]
        public void SysCommand_GuardPath_NeedsInvokeOnMain()
        {
            ThreadGuard guard = new ThreadGuard();
            OA oa = new OA(guard);
            CommandBus cmd = new CommandBus(guard);
            IdAllocator ids = new IdAllocator();
            FlowRunner runner = new FlowRunner(guard, oa, cmd, ids);
            AuditStore audit = new AuditStore();
            AuditQuery query = new AuditQuery(audit);
            SysCommand sys = new SysCommand(query);
            DataBox.Bind<FlowRunner>(runner);
            DataBox.Bind<ICommandBus>(cmd);
            try
            {
                // [段1] 工作线程直调——sys.flow 实时态段访问 runner.GetStatus（守卫）→ 抛
                System.Exception? directError = null;
                System.Threading.ManualResetEventSlim s1 = new System.Threading.ManualResetEventSlim(false);
                System.Threading.Thread w1 = new System.Threading.Thread(new System.Threading.ThreadStart(delegate
                {
                    try
                    {
                        sys.Execute("sys.flow");
                    }
                    catch (System.Exception ex)
                    {
                        directError = ex;
                    }
                    s1.Set();
                }));
                w1.Start();
                Assert.True(s1.Wait(2000, TestContext.Current.CancellationToken));
                w1.Join(1000);
                Assert.NotNull(directError);
                Assert.Contains("主线程", directError!.Message);

                // [段2] 经 InvokeOnMain——主线程执行 → 成功返回 MD
                string result = "";
                System.Threading.ManualResetEventSlim s2 = new System.Threading.ManualResetEventSlim(false);
                System.Threading.Thread w2 = new System.Threading.Thread(new System.Threading.ThreadStart(delegate
                {
                    bool ok = runner.InvokeOnMain(delegate
                    {
                        return sys.Execute("sys.flow");
                    }, 3000, out result);
                    Assert.True(ok);
                    s2.Set();
                }));
                w2.Start();
                DriveUntilDone(runner, s2);
                w2.Join(1000);
                Assert.Contains("SYS.FLOW", result);
            }
            finally
            {
                DataBox.Unbind<FlowRunner>();
                DataBox.Unbind<ICommandBus>();
            }
        }
    }
}
