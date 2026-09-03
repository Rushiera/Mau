using System;
using System.IO;
using Xunit;

namespace Mau.Runtime.Tests
{
    /// <summary>
    /// 猫级文件系统注册表测试——P2 配置作用域单向流：注册/注销/显式 catId 解析/当前猫解析/作用域回退。
    /// </summary>
    public sealed class FileSystemRegistryTests
    {
        /// <summary>
        /// 建临时根 + 猫级 FileSystemService（可写根）
        /// </summary>
        private static FileSystemService CreateFs(out string baseDir)
        {
            baseDir = Path.Combine(Path.GetTempPath(), "mau_fsreg_" + Guid.NewGuid().ToString("N"));
            string rw = Path.Combine(baseDir, "rw");
            Directory.CreateDirectory(rw);
            WorkspaceConfig.RootEntry[] entries = new WorkspaceConfig.RootEntry[]
            {
                new WorkspaceConfig.RootEntry() { Id = "rw", Path = rw, Writable = true }
            };
            return new FileSystemService(entries, Path.Combine(rw, "recycle"));
        }

        /// <summary>
        /// 注册 + 显式 catId 解析——命中猫级实例
        /// </summary>
        [Fact]
        public void Register_ResolveByCatId_ReturnsCatFs()
        {
            string baseDir;
            FileSystemService fs = CreateFs(out baseDir);
            try
            {
                FileSystemRegistry.Register("catA", fs);
                FileSystemService? got = FileSystemRegistry.Resolve("catA");
                Assert.NotNull(got);
                Assert.Same(fs, got);
            }
            finally
            {
                FileSystemRegistry.Unregister("catA");
                TryDelete(baseDir);
            }
        }

        /// <summary>
        /// 未注册 catId → null（调用方回退全局）
        /// </summary>
        [Fact]
        public void Resolve_UnknownCat_ReturnsNull()
        {
            string baseDir;
            FileSystemService fs = CreateFs(out baseDir);
            try
            {
                FileSystemRegistry.Register("catA", fs);
                Assert.Null(FileSystemRegistry.Resolve("catB"));
                Assert.Null(FileSystemRegistry.Resolve(""));
                Assert.Null(FileSystemRegistry.Resolve(""));
            }
            finally
            {
                FileSystemRegistry.Unregister("catA");
                TryDelete(baseDir);
            }
        }

        /// <summary>
        /// 注销后解析 → null
        /// </summary>
        [Fact]
        public void Unregister_ThenResolve_ReturnsNull()
        {
            string baseDir;
            FileSystemService fs = CreateFs(out baseDir);
            try
            {
                FileSystemRegistry.Register("catA", fs);
                FileSystemRegistry.Unregister("catA");
                Assert.Null(FileSystemRegistry.Resolve("catA"));
            }
            finally
            {
                TryDelete(baseDir);
            }
        }

        /// <summary>
        /// 当前猫 AsyncLocal——SetCurrentCat 后 ResolveCurrent 命中；恢复后 null
        /// </summary>
        [Fact]
        public void SetCurrentCat_ResolveCurrent_ReturnsCatFs()
        {
            string baseDir;
            FileSystemService fs = CreateFs(out baseDir);
            string? prev = FileSystemRegistry.CurrentCatKey;
            try
            {
                FileSystemRegistry.Register("catA", fs);
                FileSystemRegistry.SetCurrentCat("catA");
                FileSystemService? got = FileSystemRegistry.ResolveCurrent();
                Assert.NotNull(got);
                Assert.Same(fs, got);
            }
            finally
            {
                FileSystemRegistry.SetCurrentCat(prev);
                FileSystemRegistry.Unregister("catA");
                TryDelete(baseDir);
            }
        }

        /// <summary>
        /// 作用域解析——显式 catId 优先；空回退当前猫；都空返回 null
        /// </summary>
        [Fact]
        public void ResolveScoped_CatIdPriority_ThenCurrent()
        {
            string baseDirA;
            string baseDirB;
            FileSystemService fsA = CreateFs(out baseDirA);
            FileSystemService fsB = CreateFs(out baseDirB);
            string? prev = FileSystemRegistry.CurrentCatKey;
            try
            {
                FileSystemRegistry.Register("catA", fsA);
                FileSystemRegistry.Register("catB", fsB);
                // 显式 catId 优先于当前猫
                FileSystemRegistry.SetCurrentCat("catB");
                FileSystemService? got = FileSystemRegistry.ResolveScoped("catA");
                Assert.Same(fsA, got);
                // 空 catId → 当前猫
                got = FileSystemRegistry.ResolveScoped("");
                Assert.Same(fsB, got);
                got = FileSystemRegistry.ResolveScoped("");
                Assert.Same(fsB, got);
                // 当前猫未注册 → null
                FileSystemRegistry.SetCurrentCat("");
                Assert.Null(FileSystemRegistry.ResolveScoped(""));
            }
            finally
            {
                FileSystemRegistry.SetCurrentCat(prev);
                FileSystemRegistry.Unregister("catA");
                FileSystemRegistry.Unregister("catB");
                TryDelete(baseDirA);
                TryDelete(baseDirB);
            }
        }

        /// <summary>
        /// 根 id 匹配大小写不敏感——P4 ①：CCBP: 与 ccbp: 均可解析（FileSystemService.Resolve OrdinalIgnoreCase）
        /// </summary>
        [Fact]
        public void NamespacePath_CaseInsensitiveRootId()
        {
            string baseDir = Path.Combine(Path.GetTempPath(), "mau_ns_ci_" + Guid.NewGuid().ToString("N"));
            string rw = Path.Combine(baseDir, "rw");
            Directory.CreateDirectory(rw);
            try
            {
                WorkspaceConfig.RootEntry[] entries = new WorkspaceConfig.RootEntry[]
                {
                    new WorkspaceConfig.RootEntry() { Id = "CCBP", Path = rw, Writable = true }
                };
                FileSystemService fs = new FileSystemService(entries, Path.Combine(rw, "recycle"));
                // 大写 id 注册——两种拼写都命中
                fs.WriteText("CCBP:a.txt", "x");
                Assert.Equal("x", fs.ReadText("ccbp:a.txt"));
                // 相对路径（无前缀）仍按首根基准
                Assert.Equal("x", fs.ReadText("a.txt"));
            }
            finally
            {
                TryDelete(baseDir);
            }
        }

        /// <summary>
        /// 临时目录清理
        /// </summary>
        /// <param name="dir">目录</param>
        private static void TryDelete(string dir)
        {
            try
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, true);
                }
            }
            catch (Exception)
            {
            }
        }
    }
}
