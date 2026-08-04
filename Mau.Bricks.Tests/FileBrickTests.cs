// ═══════════════════════════════════════════════
// 测试: Mau.Bricks.Standard——FileBrick / MathBrick / FileSystemService
// 引用: Mau.Bricks.Tests → Mau.Bricks.Standard + Mau.Contracts
// 原理: CH3 FileToolTests 随迁改写——直接调用静态积木方法验证
// 常用: 积木库移植正确性回归
// ═══════════════════════════════════════════════
using System;
using System.IO;
using Xunit;

namespace Mau.Bricks.Tests
{
    /// <summary>
    /// 文件积木测试——CH3 FileToolTests 随迁（受控文件系统 + 边界）
    /// </summary>
    public sealed class FileBrickTests
    {
        /// <summary>
        /// 读写、替换、搜索、移动和软删除均保持在允许根内
        /// </summary>
        [Fact]
        public void FileSystemServiceCompletesReusableOperations()
        {
            string root = CreateTempRoot();
            string outside = CreateTempRoot();
            try
            {
                FileSystemService fs = new FileSystemService(
                    new string[] { root }, Path.Combine(root, "Recycle"));
                fs.WriteText("docs/a.txt", "one\ntwo\nthree");
                fs.AppendText("docs/a.txt", "\nfour");
                Assert.Equal(1, fs.ReplaceText("docs/a.txt", "two", "TWO"));
                Assert.Contains("2: TWO", fs.ReadLines("docs/a.txt", 2, 3));
                Assert.Contains("docs", fs.Tree(".", 2, 20));
                Assert.Equal("a.txt", Assert.Single(fs.Find("docs", "*.txt", false, 20)));
                fs.Move("docs/a.txt", "docs/b.txt");
                Assert.False(File.Exists(Path.Combine(root, "docs", "a.txt")));
                string recycled = fs.Recycle("docs/b.txt");
                Assert.True(File.Exists(recycled));
                Assert.Throws<UnauthorizedAccessException>(delegate
                {
                    fs.ReadText(Path.Combine(outside, "private.txt"));
                });
            }
            finally
            {
                Directory.Delete(root, true);
                Directory.Delete(outside, true);
            }
        }

        /// <summary>
        /// 读取入口和写入入口都拒绝穿过现存重解析点
        /// </summary>
        [Fact]
        public void FileSystemServiceRejectsReadThroughSymbolicLink()
        {
            string root = CreateTempRoot();
            string outside = CreateTempRoot();
            try
            {
                string outsideFile = Path.Combine(outside, "private.txt");
                File.WriteAllText(outsideFile, "private");
                string link = Path.Combine(root, "linked.txt");
                try
                {
                    File.CreateSymbolicLink(link, outsideFile);
                }
                catch (UnauthorizedAccessException)
                {
                    return;
                }
                catch (PlatformNotSupportedException)
                {
                    return;
                }
                catch (IOException)
                {
                    return;
                }
                FileSystemService fs = new FileSystemService(
                    new string[] { root }, Path.Combine(root, "Recycle"));

                Assert.Throws<UnauthorizedAccessException>(delegate
                {
                    fs.ReadText("linked.txt");
                });
                Assert.Throws<UnauthorizedAccessException>(delegate
                {
                    fs.WriteText("linked.txt", "changed");
                });
                Assert.Equal("private", File.ReadAllText(outsideFile));
            }
            finally
            {
                Directory.Delete(root, true);
                Directory.Delete(outside, true);
            }
        }

        /// <summary>
        /// 递归 Find 不得进入指向允许根外的目录链接
        /// </summary>
        [Fact]
        public void RecursiveFindSkipsDirectorySymbolicLinks()
        {
            string root = CreateTempRoot();
            string outside = CreateTempRoot();
            string link = Path.Combine(root, "linked");
            try
            {
                File.WriteAllText(Path.Combine(outside, "private.txt"), "private");
                try
                {
                    Directory.CreateSymbolicLink(link, outside);
                }
                catch (UnauthorizedAccessException)
                {
                    return;
                }
                catch (PlatformNotSupportedException)
                {
                    return;
                }
                catch (IOException)
                {
                    return;
                }
                FileSystemService fs = new FileSystemService(
                    new string[] { root }, Path.Combine(root, "Recycle"));

                string[] found = fs.Find(".", "*.txt", true, 20);

                Assert.Empty(found);
            }
            finally
            {
                Directory.Delete(root, true);
                Directory.Delete(outside, true);
            }
        }

        /// <summary>
        /// FileBrick 静态积木方法——写入/追加/替换/行读/搜索/移动/软删/批量
        /// </summary>
        [Fact]
        public void FileBrickStaticMethodsExecuteReusableOperations()
        {
            string root = CreateTempRoot();
            try
            {
                string a = Path.Combine(root, "docs", "a.txt");
                string b = Path.Combine(root, "docs", "b.txt");

                // [段1] 配置受控根后写/追加/替换/行读
                FileBrick.ConfigureRoots(new string[] { root }, Path.Combine(root, "Recycle"));
                Assert.True(FileBrick.Write(a, "one\ntwo\nthree"));
                Assert.True(FileBrick.Append(a, "\nfour"));
                Assert.True(FileBrick.Replace(a, "two", "TWO"));
                Assert.True(FileBrick.ReadLines(a, 2, 3, out string lines));
                Assert.Contains("2: TWO", lines);
                Assert.True(FileBrick.Read(a, out string content));
                Assert.Contains("TWO", content);

                // [段2] 搜索/移动/软删
                Assert.True(FileBrick.Find(root, "*.txt", true, 20, out string found));
                Assert.Contains("a.txt", found);
                Assert.True(FileBrick.Move(a, b));
                Assert.False(File.Exists(a));
                Assert.True(FileBrick.Delete(b, out string recycled));
                Assert.True(File.Exists(recycled));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        /// <summary>
        /// 创建独立临时目录
        /// </summary>
        /// <returns>目录</returns>
        private static string CreateTempRoot()
        {
            string root = Path.Combine(Path.GetTempPath(), "mau-file-"
                + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return root;
        }
    }

    /// <summary>
    /// 数学积木测试——CH3 SerializeAndMathTests 随迁
    /// </summary>
    public sealed class MathBrickTests
    {
        /// <summary>
        /// 数字校验与大小格式化确定性
        /// </summary>
        [Fact]
        public void MathBrickDeterministicChecks()
        {
            Assert.True(MathBrick.IsAllDigits("12345"));
            Assert.False(MathBrick.IsAllDigits("12a45"));
            Assert.False(MathBrick.IsAllDigits(""));
            Assert.False(MathBrick.IsAllDigits(null));

            Assert.Equal("999 B", MathBrick.FormatSize(999));
            Assert.Equal("1.00 K", MathBrick.FormatSize(1000));
            Assert.Equal("1.00 M", MathBrick.FormatSize(1000000));
        }

        /// <summary>
        /// 结果预览——OK 返回空串，长文本截断 15 字符
        /// </summary>
        [Fact]
        public void MathBrickResultPreviewTruncates()
        {
            Assert.Equal("", MathBrick.ResultPreview("OK"));
            Assert.Equal("", MathBrick.ResultPreview(""));
            Assert.Equal("", MathBrick.ResultPreview(null));
            Assert.Equal("（hello world!…总12 B）", MathBrick.ResultPreview("hello world!"));
            string longText = "012345678901234567890123456789";
            string preview = MathBrick.ResultPreview(longText);
            Assert.Contains("012345678901234", preview);
            Assert.Contains("总30 B", preview);
        }
    }

    /// <summary>
    /// 积木注册表测试——标准 + 数学积木全部注册且契约完整
    /// </summary>
    public sealed class BrickRegistrationTests
    {
        /// <summary>
        /// 标准积木注册后注册表包含全部 file.* 积木
        /// </summary>
        [Fact]
        public void StandardRegistrationRegistersAllFileBricks()
        {
            StandardBrickRegistration.RegisterAll();
            MathBrickRegistration.RegisterAll();
            DataBrickRegistration.RegisterAll();
            BoxBrickRegistration.RegisterAll();
            TextBrickRegistration.RegisterAll();
            ShellBrickRegistration.RegisterAll();
            LlmBrickRegistration.RegisterAll();
            ContextBrickRegistration.RegisterAll();
            ApprovalBrickRegistration.RegisterAll();
            ExcelBrickRegistration.RegisterAll();
            DocxBrickRegistration.RegisterAll();
            LogBrickRegistration.RegisterAll();

            Assert.True(Mau.Contracts.BrickRegistry.TryGet("file.convert", out _));
            Assert.True(Mau.Contracts.BrickRegistry.TryGet("file.read", out _));
            Assert.True(Mau.Contracts.BrickRegistry.TryGet("file.write", out _));
            Assert.True(Mau.Contracts.BrickRegistry.TryGet("file.append", out _));
            Assert.True(Mau.Contracts.BrickRegistry.TryGet("file.replace", out _));
            Assert.True(Mau.Contracts.BrickRegistry.TryGet("file.read_lines", out _));
            Assert.True(Mau.Contracts.BrickRegistry.TryGet("file.tree", out _));
            Assert.True(Mau.Contracts.BrickRegistry.TryGet("file.find", out _));
            Assert.True(Mau.Contracts.BrickRegistry.TryGet("file.move", out _));
            Assert.True(Mau.Contracts.BrickRegistry.TryGet("file.delete", out _));
            Assert.True(Mau.Contracts.BrickRegistry.TryGet("file.batch", out _));
            Assert.True(Mau.Contracts.BrickRegistry.TryGet("math.is_all_digits", out _));
            Assert.True(Mau.Contracts.BrickRegistry.TryGet("math.format_size", out _));
            Assert.True(Mau.Contracts.BrickRegistry.TryGet("math.result_preview", out _));
            Assert.True(Mau.Contracts.BrickRegistry.TryGet("data.snapshot_encode", out _));
            Assert.True(Mau.Contracts.BrickRegistry.TryGet("data.snapshot_decode", out _));
            Assert.True(Mau.Contracts.BrickRegistry.TryGet("data.box_set", out _));
            Assert.True(Mau.Contracts.BrickRegistry.TryGet("data.box_get", out _));
            Assert.True(Mau.Contracts.BrickRegistry.TryGet("data.box_set_dic", out _));
            Assert.True(Mau.Contracts.BrickRegistry.TryGet("data.box_get_dic", out _));
            Assert.True(Mau.Contracts.BrickRegistry.TryGet("text.md_parse", out _));
            Assert.True(Mau.Contracts.BrickRegistry.TryGet("shell.exec", out _));
            Assert.True(Mau.Contracts.BrickRegistry.TryGet("llm.chat", out _));
            Assert.True(Mau.Contracts.BrickRegistry.TryGet("llm.ctx_trim", out _));
            Assert.True(Mau.Contracts.BrickRegistry.TryGet("approval.request", out _));
            Assert.True(Mau.Contracts.BrickRegistry.TryGet("excel.read", out _));
            Assert.True(Mau.Contracts.BrickRegistry.TryGet("docx.write", out _));
            Assert.True(Mau.Contracts.BrickRegistry.TryGet("log.write", out _));
        }
    }
}
