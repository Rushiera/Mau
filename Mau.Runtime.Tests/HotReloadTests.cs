using System;
using System.IO;
using Xunit;
using Mau.Runtime;

namespace Mau.Runtime.Tests
{
    /// <summary>
    /// 热拔插基础设施测试——FlowALC + FlowHandle
    /// </summary>
    public sealed class HotReloadTests
    {
        private static string FixtureDir
        {
            get
            {
                // 从测试输出目录导航到 fixtures/bin/
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string dir = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "fixtures", "bin"));
                return dir;
            }
        }

        private static string ValidDllPath
        {
            get { return Path.Combine(FixtureDir, "FL_ValidFlow.dll"); }
        }

        private static string NoInterfaceDllPath
        {
            get { return Path.Combine(FixtureDir, "FL_NoInterface.dll"); }
        }

        private static string NotADllPath
        {
            get { return Path.Combine(FixtureDir, "not-a-dll.txt"); }
        }

        /// <summary>
        /// 确保 fixture DLL 已编译
        /// </summary>
        private static void AssertFixturesExist()
        {
            if (!File.Exists(ValidDllPath))
            {
                throw new FileNotFoundException("Fixture DLL 未编译。请先运行 fixtures/build-fixtures.cmd。缺失: " + ValidDllPath);
            }
            if (!File.Exists(NoInterfaceDllPath))
            {
                throw new FileNotFoundException("Fixture DLL 未编译。请先运行 fixtures/build-fixtures.cmd。缺失: " + NoInterfaceDllPath);
            }
        }

        // ──────────────────────────────────────
        // V1: 加载有效 DLL
        // ──────────────────────────────────────

        [Fact]
        public void Load_ValidDll_ReturnsFlow()
        {
            AssertFixturesExist();
            using FlowHandle handle = FlowHandle.Load(ValidDllPath);
            Assert.NotNull(handle.Flow);
        }

        [Fact]
        public void Load_ValidDll_FlowIsObservableFlow()
        {
            AssertFixturesExist();
            using FlowHandle handle = FlowHandle.Load(ValidDllPath);
            IObservableFlow flow = handle.Flow;
            Assert.True(flow is IObservableFlow);
        }

        [Fact]
        public void Load_ValidDll_TickAdvancesFrame()
        {
            AssertFixturesExist();
            using FlowHandle handle = FlowHandle.Load(ValidDllPath);
            IObservableFlow flow = handle.Flow;

            RuntimeStatus before = flow.GetStatus();
            Assert.Equal(0L, before.Frame);

            flow.Tick();
            RuntimeStatus after = flow.GetStatus();
            Assert.Equal(1L, after.Frame);
        }

        // ──────────────────────────────────────
        // V2: 加载无 IObservableFlow 的 DLL
        // ──────────────────────────────────────

        [Fact]
        public void Load_NoInterface_ThrowsInvalidOperation()
        {
            AssertFixturesExist();
            string path = NoInterfaceDllPath;
            Assert.True(File.Exists(path), "Fixture DLL not found: " + path);

            // FL_NoInterface 不实现 IObservableFlow——Load 应抛 InvalidOperationException
            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => FlowHandle.Load(path)
            );
            Assert.Contains("IObservableFlow", ex.Message);
        }

        // ──────────────────────────────────────
        // V3: DLL 不存在
        // ──────────────────────────────────────

        [Fact]
        public void Load_NonExistentFile_ThrowsFileNotFound()
        {
            string fakePath = Path.Combine(FixtureDir, "nonexistent.dll");
            Assert.Throws<FileNotFoundException>(
                () => FlowHandle.Load(fakePath)
            );
        }

        // ──────────────────────────────────────
        // V4: 损坏的 DLL
        // ──────────────────────────────────────

        [Fact]
        public void Load_BadImage_ThrowsBadImageFormat()
        {
            // 确保 not-a-dll.txt 存在
            if (!File.Exists(NotADllPath))
            {
                File.WriteAllText(NotADllPath, "this is not a dll");
            }

            Assert.Throws<BadImageFormatException>(
                () => FlowHandle.Load(NotADllPath)
            );
        }

        // ──────────────────────────────────────
        // V5: TryUnload 回收成功
        // ──────────────────────────────────────

        [Fact]
        public void TryUnload_CompletesWithoutError()
        {
            AssertFixturesExist();
            FlowHandle handle = FlowHandle.Load(ValidDllPath);
            IObservableFlow? flow = handle.Flow;
            flow.Tick();
            flow = null;    // 释放局部引用——减少调用方栈帧残留

            // TryUnload 不应抛异常，且完成后 Flow 不可访问
            bool result = handle.TryUnload(5);

            // ⚠️ 不检查 result：TryUnload 的 GC 确认受调用方栈帧引用影响（flow 临时槽可能残留到方法尾），
            // false 不必然泄漏。真实回收断言见 V10 TryUnload_AssemblyWeakRef_ReclaimedAfterScopeExit（作用域外弱引用死亡）。
            Assert.Throws<ObjectDisposedException>(() => handle.Flow);
        }

        // ──────────────────────────────────────
        // V6: 卸载后重新加载同一 DLL——验证 ALC 隔离
        // ──────────────────────────────────────

        [Fact]
        public void Unload_Reload_Works()
        {
            AssertFixturesExist();
            FlowHandle handle1 = FlowHandle.Load(ValidDllPath);
            handle1.Flow.Tick();
            handle1.TryUnload(5);

            // 卸载后重新加载同一 DLL 应成功
            using FlowHandle handle2 = FlowHandle.Load(ValidDllPath);
            Assert.NotNull(handle2.Flow);
            handle2.Flow.Tick();
            RuntimeStatus status = handle2.Flow.GetStatus();
            Assert.Equal(1L, status.Frame);
        }

        // ──────────────────────────────────────
        // V7: 已 Dispose 后访问 Flow 抛异常
        // ──────────────────────────────────────

        [Fact]
        public void Flow_AfterDispose_ThrowsObjectDisposed()
        {
            AssertFixturesExist();
            FlowHandle handle = FlowHandle.Load(ValidDllPath);
            handle.Dispose();
            Assert.Throws<ObjectDisposedException>(() => handle.Flow);
        }

        // ──────────────────────────────────────
        // V8: 多次 Tick + GetStatus + GetLogs
        // ──────────────────────────────────────

        [Fact]
        public void TickMultiple_StatusAndLogsCorrect()
        {
            AssertFixturesExist();
            using FlowHandle handle = FlowHandle.Load(ValidDllPath);
            IObservableFlow flow = handle.Flow;

            for (int i = 0; i < 5; i = i + 1)
            {
                flow.Tick();
            }

            RuntimeStatus status = flow.GetStatus();
            Assert.Equal(5L, status.Frame);

            MauDebug[] logs = flow.GetLogs();
            Assert.Equal(5, logs.Length);

            for (int i = 0; i < logs.Length; i = i + 1)
            {
                Assert.Equal((long)(i + 1), logs[i].Frame);
                Assert.Equal("T_Test", logs[i].TransitionName);
            }
        }

        // ──────────────────────────────────────
        // V9: FlowHost.ReloadFlows——热重载（② 热感知 D8/D10）
        // ──────────────────────────────────────

        /// <summary>
        /// 热重载——同 dll 加载新版本替换旧 handle，SourceDll 匹配
        /// </summary>
        [Fact]
        public void ReloadFlows_ValidDll_ReplacesOldHandle()
        {
            AssertFixturesExist();
            using (FlowHost host = new FlowHost())
            {
                FlowHandle[] olds = host.LoadAll(ValidDllPath);
                Assert.NotEmpty(olds);
                Assert.Equal(olds.Length, host.Count);

                string[] report = host.ReloadFlows(new string[] { ValidDllPath });
                Assert.Single(report);
                Assert.Contains("热重载成功", report[0]);
                Assert.Equal(olds.Length, host.Count);
            }
        }

        /// <summary>
        /// 热重载失败——坏 dll 加载失败保留旧 handle（D8 失败回滚）
        /// </summary>
        [Fact]
        public void ReloadFlows_BadDll_KeepsOldHandle()
        {
            AssertFixturesExist();
            using (FlowHost host = new FlowHost())
            {
                FlowHandle[] olds = host.LoadAll(ValidDllPath);
                Assert.Equal(olds.Length, host.Count);

                // 损坏 dll——不是有效 .NET 程序集
                string badDll = Path.Combine(FixtureDir, "bad-reload.dll");
                File.WriteAllText(badDll, "this is not a real dll at all");
                try
                {
                    string[] report = host.ReloadFlows(new string[] { badDll });
                    Assert.Single(report);
                    Assert.Contains("加载失败", report[0]);
                    // 旧 handle 保留——数量不变
                    Assert.Equal(olds.Length, host.Count);
                }
                finally
                {
                    if (File.Exists(badDll))
                    {
                        File.Delete(badDll);
                    }
                }
            }
        }

        /// <summary>
        /// 热重载无 IObservableFlow 的 dll——失败保留旧（验证 LoadAll 校验）
        /// </summary>
        [Fact]
        public void ReloadFlows_NoInterface_KeepsOldHandle()
        {
            AssertFixturesExist();
            using (FlowHost host = new FlowHost())
            {
                FlowHandle[] olds = host.LoadAll(ValidDllPath);
                Assert.Equal(olds.Length, host.Count);

                string[] report = host.ReloadFlows(new string[] { NoInterfaceDllPath });
                Assert.Single(report);
                Assert.Contains("加载失败", report[0]);
                Assert.Equal(olds.Length, host.Count);
            }
        }

        /// <summary>
        /// FlowHandle.SourceDll——热重载按 dll 粒度匹配（D7）
        /// </summary>
        [Fact]
        public void Load_SourceDll_Populated()
        {
            AssertFixturesExist();
            using FlowHandle handle = FlowHandle.Load(ValidDllPath);
            Assert.Equal(Path.GetFullPath(ValidDllPath), handle.SourceDll);
        }

        // ──────────────────────────────────────
        // V10: ALC 真实回收断言——作用域退出后 assembly 弱引用死亡（外部评审补）
        // ──────────────────────────────────────

        /// <summary>
        /// 卸载后 ALC 真实回收——独立作用域加载/卸载，方法返回（栈帧消失）后 GC 循环断言 assembly 弱引用死亡。
        /// 不依赖 TryUnload 返回值（受调用方栈帧引用影响，false 不必然泄漏）——直接验证回收结果。
        /// </summary>
        [Fact]
        public void TryUnload_AssemblyWeakRef_ReclaimedAfterScopeExit()
        {
            AssertFixturesExist();
            WeakReference assemblyRef;
            RunUnloadInScope(out assemblyRef);

            // 作用域外 GC 循环——ALC 内 assembly 弱引用死亡 = ALC 真实回收（长期热重载无泄漏前提）
            for (int i = 0; i < 20; i = i + 1)
            {
                GC.Collect(2, GCCollectionMode.Forced, true, true);
                GC.WaitForPendingFinalizers();
                if (!assemblyRef.IsAlive)
                {
                    break;
                }
            }
            Assert.False(assemblyRef.IsAlive, "卸载后 ALC 应被回收（assembly 弱引用死亡）——否则长期热重载会累积泄漏");
        }

        /// <summary>
        /// V10 辅助——独立作用域加载+卸载+取 Assembly 弱引用（方法返回后调用栈无生成物引用残留）
        /// </summary>
        /// <param name="assemblyRef">ALC 内 assembly 的弱引用</param>
        private static void RunUnloadInScope(out WeakReference assemblyRef)
        {
            FlowHandle handle = FlowHandle.Load(ValidDllPath);
            handle.Flow.Tick();
            assemblyRef = new WeakReference(handle.Flow.GetType().Assembly);
            handle.TryUnload(10);
        }
    }
}
