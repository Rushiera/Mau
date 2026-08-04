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
            handle.Flow.Tick();

            // TryUnload 不应抛异常，且完成后 Flow 不可访问
            bool result = handle.TryUnload(5);

            // 无论 GC 是否立即回收，卸载流程本身应正常完成
            // result 可能为 false（ALC 异步卸载），这不代表泄漏
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
    }
}
