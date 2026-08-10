using System;
using System.IO;
using Mau.Runtime;
using Xunit;

namespace Mau.Runtime.Tests
{
    /// <summary>
    /// 审计存储测试——AuditStore（A.1 存储模型）
    /// 隔离：临时目录 Guid 唯一命名 + finally 清理；时间源注入固定时钟验证跨天滚动
    /// </summary>
    [Collection("AuditSerial")]
    public sealed class AuditStoreTests
    {
        /// <summary>
        /// 共享读文件——写者仍持有 FileShare.ReadWrite 句柄时允许读（AuditQuery 同款读法）
        /// </summary>
        /// <param name="path">文件路径</param>
        /// <returns>全文</returns>
        private static string ReadAllTextShared(string path)
        {
            using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                using (StreamReader r = new StreamReader(fs, System.Text.Encoding.UTF8))
                {
                    return r.ReadToEnd();
                }
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

        /// <summary>
        /// 未配置时——事件入环形缓冲，不落盘，Total 递增
        /// </summary>
        [Fact]
        public void Record_NoConfigure_KeepsInMemory()
        {
            AuditStore store = new AuditStore();
            try
            {
                store.Record("CommandBus", "cmd.set", 10, new AuditProp[] { new AuditProp("key", "chat_x_msg") });
                store.Record("OA", "oa.post", 11, new AuditProp[] { new AuditProp("id", "7") });
                Assert.Equal(2L, store.Total);
                AuditEvent[] snap = store.Snapshot();
                Assert.Equal(2, snap.Length);
                Assert.Equal("CommandBus", snap[0].Source);
                Assert.Equal("cmd.set", snap[0].Category);
                Assert.Equal(10L, snap[0].Frame);
                Assert.Equal("OA", snap[1].Source);
            }
            finally
            {
                store.Shutdown();
            }
        }

        /// <summary>
        /// 环形缓冲覆盖——容量 3 写 5 条，保留最新 3 条且序号连续
        /// </summary>
        [Fact]
        public void RingOverflow_KeepsLatest()
        {
            AuditStore store = new AuditStore(3);
            try
            {
                for (int i = 1; i <= 5; i = i + 1)
                {
                    store.Record("Src", "cat." + i, i, new AuditProp[] { new AuditProp("n", i.ToString()) });
                }
                Assert.Equal(5L, store.Total);
                AuditEvent[] snap = store.Snapshot();
                Assert.Equal(3, snap.Length);
                Assert.Equal(3L, snap[0].Seq);
                Assert.Equal(5L, snap[2].Seq);
            }
            finally
            {
                store.Shutdown();
            }
        }

        /// <summary>
        /// 配置后——事件落盘：会话头 + 事件 MD 格式 + Flush 后文件可见
        /// </summary>
        [Fact]
        public void Configure_ThenRecord_WritesFileWithHeader()
        {
            string root = Path.Combine(Path.GetTempPath(), "audit_ok_" + Guid.NewGuid().ToString("N"));
            DateTime fixedNow = new DateTime(2026, 8, 10, 23, 40, 1);
            AuditStore store = new AuditStore(10000, () => fixedNow);
            try
            {
                store.ConfigureAudit(root, "test", 20, 5);
                store.Record("CommandBus", "cmd.set", 1284, new AuditProp[] { new AuditProp("key", "chat_x_msg"), new AuditProp("result", "accepted") });
                store.Flush();
                // 会话目录 + 当日文件存在
                string sessionDir = Path.Combine(root, "20260810_234001");
                string file = Path.Combine(sessionDir, "20260810.md");
                Assert.True(Directory.Exists(sessionDir));
                Assert.True(File.Exists(file));
                string text = ReadAllTextShared(file);
                // 会话头
                Assert.Contains("# Audit Session 20260810_234001", text);
                Assert.Contains("# 模式: test", text);
                Assert.Contains("# 加载数: 20", text);
                Assert.Contains("# 起始帧: 5", text);
                // 事件行
                Assert.Contains("| CommandBus | cmd.set", text);
                Assert.Contains("| AuditStore | app.start", text);
                Assert.Contains("- key: chat_x_msg", text);
                Assert.Contains("- result: accepted", text);
            }
            finally
            {
                store.Shutdown();
                if (Directory.Exists(root))
                {
                    TryDeleteDir(root);
                }
            }
        }

        /// <summary>
        /// 不可落盘事件——内存有，磁盘无
        /// </summary>
        [Fact]
        public void NonPersistable_SkipsDisk()
        {
            string root = Path.Combine(Path.GetTempPath(), "audit_np_" + Guid.NewGuid().ToString("N"));
            AuditStore store = new AuditStore(10000, () => new DateTime(2026, 8, 10, 12, 0, 0));
            try
            {
                store.ConfigureAudit(root, "test");
                store.Record("Flow", "trace.fire", 3, new AuditProp[] { new AuditProp("prop", "P_Ok") }, false);
                store.Flush();
                Assert.Equal(2L, store.Total);
                Assert.Equal(2, store.Snapshot().Length);
                string sessionDir = Path.Combine(root, "20260810_120000");
                string file = Path.Combine(sessionDir, "20260810.md");
                Assert.True(File.Exists(file));
                string text = ReadAllTextShared(file);
                Assert.DoesNotContain("trace.fire", text);
            }
            finally
            {
                store.Shutdown();
                if (Directory.Exists(root))
                {
                    TryDeleteDir(root);
                }
            }
        }

        /// <summary>
        /// 跨天滚动——日期变化生成新文件，新文件带会话头且起始帧为当前事件帧
        /// </summary>
        [Fact]
        public void CrossDay_RollsToNewFileWithHeader()
        {
            string root = Path.Combine(Path.GetTempPath(), "audit_day_" + Guid.NewGuid().ToString("N"));
            DateTime t1 = new DateTime(2026, 8, 10, 23, 59, 59);
            AuditStore store = new AuditStore(10000, () => t1);
            try
            {
                store.ConfigureAudit(root, "test", 0, 0);
                store.Record("CommandBus", "cmd.set", 99, new AuditProp[] { new AuditProp("key", "k1") });
                // 时间跨天——下一事件在新文件
                t1 = new DateTime(2026, 8, 11, 0, 0, 5);
                store.Record("CommandBus", "cmd.consume", 100, new AuditProp[] { new AuditProp("key", "k2") });
                store.Flush();
                string sessionDir = Path.Combine(root, "20260810_235959");
                string f1 = Path.Combine(sessionDir, "20260810.md");
                string f2 = Path.Combine(sessionDir, "20260811.md");
                Assert.True(File.Exists(f1));
                Assert.True(File.Exists(f2));
                string text1 = ReadAllTextShared(f1);
                string text2 = ReadAllTextShared(f2);
                Assert.Contains("cmd.set", text1);
                Assert.DoesNotContain("cmd.consume", text1);
                // 新文件带会话头 + 起始帧 = 跨天事件帧
                Assert.Contains("# Audit Session 20260810_235959", text2);
                Assert.Contains("# 起始帧: 100", text2);
                Assert.Contains("cmd.consume", text2);
            }
            finally
            {
                store.Shutdown();
                if (Directory.Exists(root))
                {
                    TryDeleteDir(root);
                }
            }
        }

        /// <summary>
        /// 过期清理——retainDays=1 时更早会话删除，当日会话保留
        /// </summary>
        [Fact]
        public void CleanOldSessions_RemovesExpired()
        {
            string root = Path.Combine(Path.GetTempPath(), "audit_cln_" + Guid.NewGuid().ToString("N"));
            DateTime now = new DateTime(2026, 8, 10, 10, 0, 0);
            try
            {
                // 预置过期会话目录（8月8日）与当日会话目录
                Directory.CreateDirectory(Path.Combine(root, "20260808_000000"));
                Directory.CreateDirectory(Path.Combine(root, "20260810_090000"));
                AuditStore store = new AuditStore(10000, () => now);
                try
                {
                    store.ConfigureAudit(root, "test", 0, 0, 1);
                }
                finally
                {
                    store.Shutdown();
                }
                Assert.False(Directory.Exists(Path.Combine(root, "20260808_000000")));
                Assert.True(Directory.Exists(Path.Combine(root, "20260810_090000")));
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    TryDeleteDir(root);
                }
            }
        }

        /// <summary>
        /// 序号单调——多条记录 Seq 递增且帧号保留
        /// </summary>
        [Fact]
        public void Seq_Monotonic()
        {
            AuditStore store = new AuditStore();
            try
            {
                store.Record("A", "a.1", 1, null);
                store.Record("B", "b.1", 2, null);
                store.Record("C", "c.1", 3, null);
                AuditEvent[] snap = store.Snapshot();
                Assert.Equal(1L, snap[0].Seq);
                Assert.Equal(2L, snap[1].Seq);
                Assert.Equal(3L, snap[2].Seq);
                Assert.Equal(3L, snap[2].Frame);
                // 无属性事件——格式化为空属性
                Assert.Empty(snap[0].Props);
            }
            finally
            {
                store.Shutdown();
            }
        }
    }
}
