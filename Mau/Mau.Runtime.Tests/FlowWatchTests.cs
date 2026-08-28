using System;
using System.IO;
using Xunit;
using Mau.Runtime;

namespace Mau.Runtime.Tests
{
    /// <summary>
    /// 热感知测试（② D5-D10）——FlowWatchService 指纹轮询 + pending 标记 + 宿主 ReloadFlows 全链路。
    /// 隔离：每测试独立临时目录 + finally 清理；AuditSerial 串行（静态态无——纯实例，可并行但保持一致）
    /// </summary>
    public sealed class FlowWatchTests
    {
        /// <summary>
        /// 临时监听目录——每测试独立
        /// </summary>
        private readonly string _watchDir = Path.Combine(Path.GetTempPath(),
            "mauwatch_" + Guid.NewGuid().ToString("N").Substring(0, 8));

        /// <summary>
        /// 夹具 dll 目录
        /// </summary>
        private static string FixtureDir
        {
            get
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                return Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "fixtures", "bin"));
            }
        }

        /// <summary>
        /// 夹具——建临时目录 + 复制有效 dll
        /// </summary>
        public FlowWatchTests()
        {
            Directory.CreateDirectory(_watchDir);
        }

        /// <summary>
        /// 复制夹具 dll 到监听目录
        /// </summary>
        /// <param name="name">目标文件名</param>
        /// <returns>目标路径</returns>
        private string CopyFixtureDll(string name)
        {
            FixtureBuilder.Ensure();
            string src = Path.Combine(FixtureDir, "FL_ValidFlow.dll");
            string dst = Path.Combine(_watchDir, name);
            File.Copy(src, dst, true);
            return dst;
        }

        /// <summary>
        /// 清理临时目录
        /// </summary>
        private void Cleanup()
        {
            try
            {
                if (Directory.Exists(_watchDir))
                {
                    Directory.Delete(_watchDir, true);
                }
            }
            catch
            {
                // 清理失败不影响结果
            }
        }

        /// <summary>
        /// StartWatch + PollNow——首次扫描全量标记 pending（新增）
        /// </summary>
        [Fact]
        public void StartWatch_FirstPoll_MarksAllPending()
        {
            try
            {
                CopyFixtureDll("FL_A.dll");
                FlowWatchService watcher = new FlowWatchService(60);
                watcher.StartWatch(_watchDir);
                watcher.PollNow();
                Assert.Contains(watcher.Pending, p => p.EndsWith("FL_A.dll"));
            }
            finally
            {
                Cleanup();
            }
        }

        /// <summary>
        /// 轮询节流——帧内不重复扫描（D9：Tick 帧计数节流）
        /// </summary>
        [Fact]
        public void Tick_Throttled_NoRescanWithinWindow()
        {
            try
            {
                CopyFixtureDll("FL_A.dll");
                FlowWatchService watcher = new FlowWatchService(60);
                watcher.StartWatch(_watchDir);
                watcher.PollNow();
                string[] pending = watcher.ConsumePending();
                Assert.Contains(pending, p => p.EndsWith("FL_A.dll"));

                // 节流窗口内 Tick——不再扫描（即使文件变化也不标记）
                string changed = Path.Combine(_watchDir, "FL_A.dll");
                File.SetLastWriteTimeUtc(changed, DateTime.UtcNow.AddSeconds(5));
                watcher.Tick(1);
                Assert.Empty(watcher.Pending);

                // 越过节流窗口——重新扫描标记
                watcher.Tick(61);
                Assert.Contains(watcher.Pending, p => p.EndsWith("FL_A.dll"));
            }
            finally
            {
                Cleanup();
            }
        }

        /// <summary>
        /// 修改检测——内容/时间戳变化 → pending（D6 指纹轮询）
        /// </summary>
        [Fact]
        public void PollNow_FileChanged_MarksPending()
        {
            try
            {
                string dll = CopyFixtureDll("FL_A.dll");
                FlowWatchService watcher = new FlowWatchService(60);
                watcher.StartWatch(_watchDir);
                watcher.PollNow();
                watcher.ConsumePending();

                // 模拟产物更新——覆盖复制 + 修改时间戳（指纹含时间戳，变化即标记）
                File.Copy(Path.Combine(FixtureDir, "FL_ValidFlow.dll"), dll, true);
                File.SetLastWriteTimeUtc(dll, DateTime.UtcNow.AddSeconds(1));
                watcher.PollNow();
                Assert.Contains(watcher.Pending, p => p.EndsWith("FL_A.dll"));
            }
            finally
            {
                Cleanup();
            }
        }

        /// <summary>
        /// 新增检测——新 dll 出现 → pending
        /// </summary>
        [Fact]
        public void PollNow_NewDll_MarksPending()
        {
            try
            {
                CopyFixtureDll("FL_A.dll");
                FlowWatchService watcher = new FlowWatchService(60);
                watcher.StartWatch(_watchDir);
                watcher.PollNow();
                watcher.ConsumePending();

                CopyFixtureDll("FL_B.dll");
                watcher.PollNow();
                Assert.Contains(watcher.Pending, p => p.EndsWith("FL_B.dll"));
            }
            finally
            {
                Cleanup();
            }
        }

        /// <summary>
        /// 删除检测——dll 消失 → pending（宿主按文件名找不到旧 handle 时忽略）
        /// </summary>
        [Fact]
        public void PollNow_DllRemoved_MarksPending()
        {
            try
            {
                string dll = CopyFixtureDll("FL_A.dll");
                FlowWatchService watcher = new FlowWatchService(60);
                watcher.StartWatch(_watchDir);
                watcher.PollNow();
                watcher.ConsumePending();

                File.Delete(dll);
                watcher.PollNow();
                Assert.Contains(watcher.Pending, p => p.EndsWith("FL_A.dll"));
            }
            finally
            {
                Cleanup();
            }
        }

        /// <summary>
        /// ConsumePending——取走并清空（重载完成后调用）
        /// </summary>
        [Fact]
        public void ConsumePending_Clears()
        {
            try
            {
                CopyFixtureDll("FL_A.dll");
                FlowWatchService watcher = new FlowWatchService(60);
                watcher.StartWatch(_watchDir);
                watcher.PollNow();
                string[] pending = watcher.ConsumePending();
                Assert.NotEmpty(pending);
                Assert.Empty(watcher.Pending);
            }
            finally
            {
                Cleanup();
            }
        }

        /// <summary>
        /// Reset——清空监听/指纹/pending（D26 统一 Reset 契约）
        /// </summary>
        [Fact]
        public void Reset_ClearsAll()
        {
            try
            {
                CopyFixtureDll("FL_A.dll");
                FlowWatchService watcher = new FlowWatchService(60);
                watcher.StartWatch(_watchDir);
                watcher.PollNow();
                watcher.Reset();
                Assert.Empty(watcher.Pending);
                // Reset 后 Tick 不扫描（未监听）
                watcher.Tick(0);
                Assert.Empty(watcher.Pending);
            }
            finally
            {
                Cleanup();
            }
        }

        /// <summary>
        /// 全链路——watcher 检测 → host.ReloadFlows 热重载（D8/D10 宿主主动刷新）
        /// </summary>
        [Fact]
        public void Watch_ThenReload_EndToEnd()
        {
            try
            {
                string dll = CopyFixtureDll("FL_ValidFlow.dll");
                FlowWatchService watcher = new FlowWatchService(60);
                watcher.StartWatch(_watchDir);
                watcher.PollNow();
                string[] pending = watcher.ConsumePending();
                Assert.Contains(pending, p => p.EndsWith("FL_ValidFlow.dll"));

                // 宿主加载初始版本
                using (FlowHost host = new FlowHost())
                {
                    FlowHandle[] olds = host.LoadAll(dll);
                    int initialCount = host.Count;
                    Assert.True(initialCount > 0);

                    // 模拟产物更新（staging→rename 原子发布：覆盖复制 + 时间戳变化）
                    File.Copy(Path.Combine(FixtureDir, "FL_ValidFlow.dll"), dll, true);
                    File.SetLastWriteTimeUtc(dll, DateTime.UtcNow.AddSeconds(1));
                    watcher.PollNow();
                    string[] changed = watcher.Pending;
                    Assert.Contains(changed, p => p.EndsWith("FL_ValidFlow.dll"));

                    // 宿主主动刷新——按 pending 重载
                    string[] report = host.ReloadFlows(changed);
                    Assert.Contains(report, r => r.Contains("热重载成功"));
                    Assert.Equal(initialCount, host.Count);
                    watcher.ConsumePending();
                    Assert.Empty(watcher.Pending);
                }
            }
            finally
            {
                Cleanup();
            }
        }
    }
}
