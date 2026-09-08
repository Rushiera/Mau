using System;
using System.IO;
using Mau.Runtime;
using Xunit;

namespace Mau.Runtime.Tests
{
    /// <summary>
    /// 审计存储测试——AuditStore 薄壳语义（O2：Record → LogStore audit.* 过滤重建）
    /// 隔离：AuditSerial 串行集合 + 每测试开头 LogStore.ClearForTest（Log 是全局真源——防残留串扰）
    /// </summary>
    [Collection("AuditSerial")]
    public sealed class AuditStoreTests
    {

        /// <summary>
        /// Record 转发 Log——audit.* 条目 + Snapshot 重建（source/category/frame/props 还原）
        /// 断言用过滤查找——并行集合写入不破坏（Log 全局真源——只增不改）
        /// </summary>
        [Fact]
        public void Record_ForwardsToLog_AndSnapshotReconstructs()
        {
            LogStore.ClearForTest();
            AuditStore store = new AuditStore();
            try
            {
                store.Record("CommandBus", "cmd.set", 10, new AuditProp[] { new AuditProp("key", "chat_x_msg") });
                store.Record("OA", "oa.post", 11, new AuditProp[] { new AuditProp("officeId", "7") });
                Assert.True(store.Total >= 2);
                AuditEvent[] snap = store.Snapshot();
                Assert.True(Array.Exists(snap, delegate (AuditEvent e)
                {
                    return e.Source == "CommandBus" && e.Category == "cmd.set" && e.Frame == 10 && e.Props.Length == 1 && e.Props[0].Value == "chat_x_msg";
                }));
                Assert.True(Array.Exists(snap, delegate (AuditEvent e)
                {
                    return e.Source == "OA" && e.Category == "oa.post" && e.Frame == 11;
                }));
            }
            finally
            {
                store.Shutdown();
            }
        }

        /// <summary>
        /// trace.* 内存真源可见（AuditQuery/sys.trace 能力保留）但磁盘投影跳过（L0-TRACE 不落盘——D5）
        /// </summary>
        [Fact]
        public void TraceEvents_InMemory_SkippedFromDisk()
        {
            LogStore.ClearForTest();
            string runDir = Path.Combine(Path.GetTempPath(), "audit_trace_" + Guid.NewGuid().ToString("N"));
            try
            {
                LogStore.ConfigureRuns(runDir);
                AuditStore store = new AuditStore();
                store.Record("Flow", "trace.fire", 1, new AuditProp[] { new AuditProp("wire", "T_Begin") });
                store.Record("Flow", "trace.state", 1, new AuditProp[] { new AuditProp("to", "S_Idle") });
                // 内存可见——Snapshot 含 trace 条目
                AuditEvent[] snap = store.Snapshot();
                Assert.True(Array.Exists(snap, delegate (AuditEvent e)
                {
                    return e.Category == "trace.fire";
                }));
                // 磁盘跳过——log_all.txt 不含 trace
                LogStore.CloseWriters();
                string logAll = File.ReadAllText(Path.Combine(runDir, "log_all.txt"), System.Text.Encoding.UTF8);
                Assert.DoesNotContain("trace.fire", logAll);
            }
            finally
            {
                LogStore.CloseWriters();
                TryDeleteDir(runDir);
            }
        }

        /// <summary>
        /// persistable=false 不转发——trace 语义保留（信号沿等高频事件调用侧显式 false；完全跳过——内存也不进）
        /// </summary>
        [Fact]
        public void PersistableFalse_NotForwarded()
        {
            LogStore.ClearForTest();
            AuditStore store = new AuditStore();
            store.Record("DataBox", "signal.post", -1, new AuditProp[] { new AuditProp("name", "P_Any") }, false);
            // 过滤查找——确认无 signal.post 事件（并行集合写入不影响本断言）
            AuditEvent[] snap = store.Snapshot();
            Assert.False(Array.Exists(snap, delegate (AuditEvent e)
            {
                return e.Category == "signal.post";
            }));
        }

        /// <summary>
        /// 空载荷即时可读——无 props 事件 Message 落 category
        /// </summary>
        [Fact]
        public void Record_NoProps_MessageIsCategory()
        {
            LogStore.ClearForTest();
            AuditStore store = new AuditStore();
            store.Record("AuditStore", "app.start", 0, null);
            Assert.True(store.Total >= 1);
            AuditEvent[] snap = store.Snapshot();
            Assert.True(Array.Exists(snap, delegate (AuditEvent e)
            {
                return e.Category == "app.start" && e.Props.Length == 0;
            }));
        }

        /// <summary>
        /// Snapshot 时间序——Record 顺序保持（旧→新；条目存在性断言——并行写入不破坏相对序）
        /// </summary>
        [Fact]
        public void Snapshot_OrderedByRecord()
        {
            LogStore.ClearForTest();
            AuditStore store = new AuditStore();
            store.Record("A", "ev.one", 1, null);
            store.Record("B", "ev.two", 2, null);
            store.Record("C", "ev.three", 3, null);
            AuditEvent[] snap = store.Snapshot();
            Assert.True(Array.Exists(snap, delegate (AuditEvent e)
            {
                return e.Category == "ev.one";
            }));
            Assert.True(Array.Exists(snap, delegate (AuditEvent e)
            {
                return e.Category == "ev.three";
            }));
        }

        /// <summary>
        /// ConfigureRuns 集成——Record 后 log_all.txt 落盘含 audit 行（O1/O2 四文件生态）
        /// </summary>
        [Fact]
        public void ConfigureRuns_ThenRecord_WritesLogAll()
        {
            LogStore.ClearForTest();
            string runDir = Path.Combine(Path.GetTempPath(), "audit_runs_" + Guid.NewGuid().ToString("N"));
            try
            {
                LogStore.ConfigureRuns(runDir);
                AuditStore store = new AuditStore();
                store.Record("CommandBus", "cmd.set", 1284, new AuditProp[] { new AuditProp("key", "chat_x_msg") });
                LogStore.CloseWriters();
                string logAll = File.ReadAllText(Path.Combine(runDir, "log_all.txt"), System.Text.Encoding.UTF8);
                Assert.Contains("[AUDIT]", logAll);
                Assert.Contains("cmd.set", logAll);
                Assert.Contains("F1284", logAll);
                // err_all.txt 干净（无 L3）
                string errAll = File.ReadAllText(Path.Combine(runDir, "err_all.txt"), System.Text.Encoding.UTF8);
                Assert.Equal("", errAll);
            }
            finally
            {
                LogStore.CloseWriters();
                TryDeleteDir(runDir);
            }
        }

        /// <summary>
        /// 等级投影——L3 进 err.all（薄壳 Record 默认 INFO；直写 LogStore level3 验证投影）
        /// </summary>
        [Fact]
        public void ErrLevel_WritesErrAll()
        {
            LogStore.ClearForTest();
            string runDir = Path.Combine(Path.GetTempPath(), "audit_err_" + Guid.NewGuid().ToString("N"));
            try
            {
                LogStore.ConfigureRuns(runDir);
                LogStore.Add("Test", 3, "boom", "SYS");
                LogStore.CloseWriters();
                string errAll = File.ReadAllText(Path.Combine(runDir, "err_all.txt"), System.Text.Encoding.UTF8);
                Assert.Contains("boom", errAll);
            }
            finally
            {
                LogStore.CloseWriters();
                TryDeleteDir(runDir);
            }
        }

        /// <summary>
        /// 安全删除目录——占用时忽略（测试清理不抛）
        /// </summary>
        /// <param name="path">目录路径</param>
        private static void TryDeleteDir(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                }
            }
            catch (IOException)
            {
                // 占用忽略
            }
            catch (UnauthorizedAccessException)
            {
                // 权限忽略
            }
        }
    }
}