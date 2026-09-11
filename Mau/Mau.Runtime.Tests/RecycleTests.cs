using System;
using System.IO;
using Mau.Runtime;
using Xunit;

namespace Mau.Runtime.Tests
{
    /// <summary>
    /// 软删除回收站测试——非空目录整棵子树迁移 + 内容统计 + 受控根拒绝（2026-09-11 放开目录回收）
    /// </summary>
    public sealed class RecycleTests : IDisposable
    {
        /// <summary>
        /// 可写受控根
        /// </summary>
        private readonly string _rw;

        /// <summary>
        /// 文件系统服务
        /// </summary>
        private readonly FileSystemService _fs;

        /// <summary>
        /// 创建夹具——临时根 + 服务
        /// </summary>
        public RecycleTests()
        {
            string baseDir = Path.Combine(Path.GetTempPath(), "mau_recycle_test_" + Guid.NewGuid().ToString("N"));
            _rw = Path.Combine(baseDir, "rw");
            Directory.CreateDirectory(_rw);
            WorkspaceConfig.RootEntry entry = new WorkspaceConfig.RootEntry();
            entry.Id = "rw";
            entry.Path = _rw;
            entry.Writable = true;
            _fs = new FileSystemService(new WorkspaceConfig.RootEntry[] { entry }, Path.Combine(_rw, "recycle"));
        }

        /// <summary>
        /// 释放夹具——尽力删除临时根
        /// </summary>
        public void Dispose()
        {
            try
            {
                Directory.Delete(Path.GetDirectoryName(_rw)!, true);
            }
            catch
            {
            }
        }

        /// <summary>
        /// 非空目录——整棵子树进回收站、结构保留、统计准确
        /// </summary>
        [Fact]
        public void RecycleNonEmptyDirectoryMovesWholeTree()
        {
            string target = Path.Combine(_rw, "probe");
            Directory.CreateDirectory(Path.Combine(target, "b"));
            Directory.CreateDirectory(Path.Combine(target, "d"));
            File.WriteAllText(Path.Combine(target, "b", "c.txt"), "hello");
            File.WriteAllBytes(Path.Combine(target, "b", "c.bin"), new byte[1000]);
            File.WriteAllText(Path.Combine(target, "d", "e.txt"), "xy");
            RecycleOutcome outcome = _fs.Recycle(target);
            Assert.True(outcome.IsDirectory);
            Assert.Equal(3, outcome.FileCount);
            Assert.Equal(2, outcome.DirectoryCount);
            Assert.Equal(1007, outcome.TotalBytes);
            Assert.False(Directory.Exists(target));
            Assert.True(File.Exists(Path.Combine(outcome.Target, "b", "c.txt")));
            Assert.True(File.Exists(Path.Combine(outcome.Target, "d", "e.txt")));
        }

        /// <summary>
        /// 文件——单件统计（文件数 1、体积为文件长度）
        /// </summary>
        [Fact]
        public void RecycleFileReportsSingleItemStats()
        {
            string target = Path.Combine(_rw, "single.txt");
            File.WriteAllText(target, "abcdef");
            RecycleOutcome outcome = _fs.Recycle(target);
            Assert.False(outcome.IsDirectory);
            Assert.Equal(1, outcome.FileCount);
            Assert.Equal(6, outcome.TotalBytes);
            Assert.False(File.Exists(target));
            Assert.True(File.Exists(outcome.Target));
        }

        /// <summary>
        /// 受控根本身——拒绝回收（软删可恢复，但根被移走等于运行面整体失踪）
        /// </summary>
        [Fact]
        public void RecycleRejectsControlledRoot()
        {
            Assert.Throws<InvalidOperationException>(() => _fs.Recycle(_rw));
        }
    }
}
