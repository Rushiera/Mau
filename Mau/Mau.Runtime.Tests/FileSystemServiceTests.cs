using System;
using System.IO;
using Xunit;

namespace Mau.Runtime.Tests
{
    /// <summary>
    /// 受控根边界测试——P8.5 配置群（design-ch4-workspace §三）：writable=false 根拒绝写、读放行、越界拒绝；P8.5b 命名空间寻址（id:relative）。
    /// </summary>
    public sealed class FileSystemServiceTests
    {
        /// <summary>
        /// 只读根写拒绝 + 可写根正常 + 只读根读放行 + 越界拒绝——四断言一链
        /// </summary>
        [Fact]
        public void ReadOnlyRoot_WriteRejected_ReadAllowed_OutsideRejected()
        {
            string baseDir = Path.Combine(Path.GetTempPath(), "mau_fs_test_" + Guid.NewGuid().ToString("N"));
            string ro = Path.Combine(baseDir, "ro");
            string rw = Path.Combine(baseDir, "rw");
            Directory.CreateDirectory(ro);
            Directory.CreateDirectory(rw);
            try
            {
                WorkspaceConfig.RootEntry[] entries = new WorkspaceConfig.RootEntry[]
                {
                    new WorkspaceConfig.RootEntry() { Id = "ro", Path = ro, Writable = false },
                    new WorkspaceConfig.RootEntry() { Id = "rw", Path = rw, Writable = true }
                };
                FileSystemService fs = new FileSystemService(entries, Path.Combine(rw, "recycle"));
                // 只读根写拒绝（WriteText/AppendText 均走 Resolve forWrite）
                Assert.Throws<UnauthorizedAccessException>(() => fs.WriteText(Path.Combine(ro, "a.txt"), "x"));
                Assert.Throws<UnauthorizedAccessException>(() => fs.AppendText(Path.Combine(ro, "a.txt"), "x"));
                // 可写根正常读写
                fs.WriteText(Path.Combine(rw, "a.txt"), "x");
                Assert.Equal("x", fs.ReadText(Path.Combine(rw, "a.txt")));
                // 只读根读放行
                File.WriteAllText(Path.Combine(ro, "b.txt"), "readok");
                Assert.Equal("readok", fs.ReadText(Path.Combine(ro, "b.txt")));
                // 越界拒绝（不在任何根内）
                Assert.Throws<UnauthorizedAccessException>(() => fs.ReadText(Path.Combine(baseDir, "outside.txt")));
            }
            finally
            {
                TryDelete(baseDir);
            }
        }

        /// <summary>
        /// 命名空间寻址——id:relative 前缀映射受控根（ccbp:/mau:/runtime:；P8.5b 自举路径语义）
        /// </summary>
        [Fact]
        public void NamespacePath_ResolvesToRoot()
        {
            string baseDir = Path.Combine(Path.GetTempPath(), "mau_ns_test_" + Guid.NewGuid().ToString("N"));
            string ro = Path.Combine(baseDir, "ro");
            string rw = Path.Combine(baseDir, "rw");
            Directory.CreateDirectory(ro);
            Directory.CreateDirectory(rw);
            try
            {
                WorkspaceConfig.RootEntry[] entries = new WorkspaceConfig.RootEntry[]
                {
                    new WorkspaceConfig.RootEntry() { Id = "ro", Path = ro, Writable = false },
                    new WorkspaceConfig.RootEntry() { Id = "rw", Path = rw, Writable = true }
                };
                FileSystemService fs = new FileSystemService(entries, Path.Combine(rw, "recycle"));
                // 命名空间读写
                fs.WriteText("rw:ns.txt", "ns-ok");
                Assert.Equal("ns-ok", fs.ReadText("rw:ns.txt"));
                // 只读根命名空间写拒绝
                Assert.Throws<UnauthorizedAccessException>(() => fs.WriteText("ro:ns.txt", "x"));
                // 未知命名空间 → 保持原样（按相对路径根基准处理——不崩溃不改写）
            }
            finally
            {
                TryDelete(baseDir);
            }
        }

        /// <summary>
        /// 尽力删除临时目录——不掩盖断言结果
        /// </summary>
        /// <param name="dir">临时目录</param>
        private static void TryDelete(string dir)
        {
            try
            {
                Directory.Delete(dir, true);
            }
            catch (Exception)
            {
                // 清理尽力而为
            }
        }
    }
}