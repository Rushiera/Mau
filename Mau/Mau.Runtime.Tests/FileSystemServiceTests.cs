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
/// 编码内建 + 换行保真（P1/P2——design-ch4-text-tools §五）：.md 带 BOM、.cs 无 BOM、.bat GBK+CRLF、读侧 BOM 剥离
/// </summary>
[Fact]
public void AutoEncoding_TypeContractAndNewlinePreserve()
{
            string baseDir = Path.Combine(Path.GetTempPath(), "mau_autoenc_" + Guid.NewGuid().ToString("N"));
            string rw = Path.Combine(baseDir, "rw");
            Directory.CreateDirectory(rw);
            try
            {
                WorkspaceConfig.RootEntry[] entries = new WorkspaceConfig.RootEntry[]
                {
                    new WorkspaceConfig.RootEntry() { Id = "rw", Path = rw, Writable = true }
                };
                FileSystemService fs = new FileSystemService(entries, Path.Combine(rw, "recycle"));
                // [段1] .md 写带 BOM——首三字节 EF BB BF
                string mdPath = Path.Combine(rw, "doc.md");
                fs.WriteTextAuto(mdPath, "标题\n正文");
                byte[] mdBytes = File.ReadAllBytes(mdPath);
                Assert.True(mdBytes.Length >= 3 && mdBytes[0] == 0xEF && mdBytes[1] == 0xBB && mdBytes[2] == 0xBF, ".md 应带 UTF-8 BOM");
                // 读侧 BOM 剥离——无 \uFEFF 残留
                Assert.Equal("标题\n正文", fs.ReadTextAuto(mdPath));
                // [段2] .cs 写无 BOM——首字节即内容
                string csPath = Path.Combine(rw, "a.cs");
                fs.WriteTextAuto(csPath, "using System;");
                byte[] csBytes = File.ReadAllBytes(csPath);
                Assert.False(csBytes.Length >= 3 && csBytes[0] == 0xEF && csBytes[1] == 0xBB && csBytes[2] == 0xBF, ".cs 不应带 BOM");
                // [段3] .bat GBK(936) 编码——中文以高位字节存
                string batPath = Path.Combine(rw, "run.bat");
                fs.WriteTextAuto(batPath, "echo 你好");
                byte[] batBytes = File.ReadAllBytes(batPath);
                Assert.Contains(batBytes, b => b >= 0x80);
                // [段4] 换行保真——CRLF 追加后仍 CRLF（含尾换行断言）
                fs.AppendTextAuto(batPath, "\necho 再见\n");
                byte[] batAfter = File.ReadAllBytes(batPath);
                Assert.True(batAfter[batAfter.Length - 2] == 0x0D && batAfter[batAfter.Length - 1] == 0x0A, "追加后仍 CRLF");
                bool hasCrLf = false;
                for (int i = 0; i < batAfter.Length - 1; i = i + 1)
                {
                    if (batAfter[i] == 0x0D && batAfter[i + 1] == 0x0A)
                    {
                        hasCrLf = true;
                        break;
                    }
                }
                Assert.True(hasCrLf, ".bat 内容含 CRLF");
                // [段5] LF 文件追加后保持 LF（不被强制改 CRLF）
                string lfPath = Path.Combine(rw, "b.txt");
                fs.WriteTextAuto(lfPath, "line1\nline2");
                fs.AppendTextAuto(lfPath, "\nline3");
                string lfText = fs.ReadTextAuto(lfPath);
                Assert.DoesNotContain("\r\n", lfText);
                Assert.Equal("line1\nline2\nline3", lfText);
            }
            finally
            {
                TryDelete(baseDir);
            }
        }/// <summary>
/// Move 目录整棵移动 + 自动建父目录 + 目标存在拒绝（D1/D2 修复：目录移动/自动建目录）
/// </summary>
[Fact]
public void Move_DirectoryAndAutoParent()
{
    string baseDir = Path.Combine(Path.GetTempPath(), "mau_fs_move_" + Guid.NewGuid().ToString("N"));
    string rw = Path.Combine(baseDir, "rw");
    Directory.CreateDirectory(rw);
    try
    {
        WorkspaceConfig.RootEntry[] entries = new WorkspaceConfig.RootEntry[]
        {
            new WorkspaceConfig.RootEntry()
            {
                Id = "rw",
                Path = rw,
                Writable = true
            }
        };
        FileSystemService fs = new FileSystemService(entries, Path.Combine(rw, "recycle"));
        // [段1] 目录整棵移动——目标父目录不存在（自动建）+ 含子文件
        Directory.CreateDirectory(Path.Combine(rw, "src", "sub"));
        File.WriteAllText(Path.Combine(rw, "src", "sub", "a.txt"), "x");
        fs.Move(Path.Combine(rw, "src"), Path.Combine(rw, "dst", "nested", "src"));
        Assert.True(Directory.Exists(Path.Combine(rw, "dst", "nested", "src", "sub")));
        Assert.Equal("x", File.ReadAllText(Path.Combine(rw, "dst", "nested", "src", "sub", "a.txt")));
        Assert.False(Directory.Exists(Path.Combine(rw, "src")));
        // [段2] 文件移动 + 自动建父目录
        File.WriteAllText(Path.Combine(rw, "file.txt"), "y");
        fs.Move(Path.Combine(rw, "file.txt"), Path.Combine(rw, "new", "dir", "file.txt"));
        Assert.Equal("y", File.ReadAllText(Path.Combine(rw, "new", "dir", "file.txt")));
        // [段3] 目标已存在拒绝（不覆盖）
        File.WriteAllText(Path.Combine(rw, "file.txt"), "z");
        Assert.Throws<IOException>(() => fs.Move(Path.Combine(rw, "file.txt"), Path.Combine(rw, "new", "dir", "file.txt")));
        Assert.Equal("y", File.ReadAllText(Path.Combine(rw, "new", "dir", "file.txt")));
        // [段4] 源不存在报错
        Assert.Throws<FileNotFoundException>(() => fs.Move(Path.Combine(rw, "missing.txt"), Path.Combine(rw, "m.txt")));
    }
    finally
    {
        TryDelete(baseDir);
    }
}
/// <summary>
/// 锚点三态诊断（P3——design-ch4-text-tools §六）：唯一替换 / 未找到差异定位 / 多次出现候选；regex 全替换
/// </summary>
[Fact]
public void ReplaceAuto_ThreeStateDiagnostics()
{
    string baseDir = Path.Combine(Path.GetTempPath(), "mau_replauto_" + Guid.NewGuid().ToString("N"));
    string rw = Path.Combine(baseDir, "rw");
    Directory.CreateDirectory(rw);
    try
    {
        WorkspaceConfig.RootEntry[] entries = new WorkspaceConfig.RootEntry[]
        {
            new WorkspaceConfig.RootEntry()
            {
                Id = "rw",
                Path = rw,
                Writable = true
            }
        };
        FileSystemService fs = new FileSystemService(entries, Path.Combine(rw, "recycle"));
        string p = Path.Combine(rw, "doc.md");
        // [段1] 唯一命中 → Ok + 数量 + Snippet
        fs.WriteTextAuto(p, "第一行\n目标 这里\n第三行");
        TextReplaceOutcome ok = fs.ReplaceTextAuto(p, "目标 这里", "换成 那里", "exact");
        Assert.Equal(TextReplaceStatus.Ok, ok.Status);
        Assert.Equal(1, ok.Count);
        Assert.Contains("换成 那里", fs.ReadTextAuto(p));
        Assert.Contains("换成 那里", ok.Snippet);
        // [段2] 未找到 → NotFound + 差异定位（期望/实际非空）
        TextReplaceOutcome nf = fs.ReplaceTextAuto(p, "目标 这里", "x", "exact");
        Assert.Equal(TextReplaceStatus.NotFound, nf.Status);
        Assert.True(nf.DiffByteIndex >= 0);
        // [段3] 多次出现 → Ambiguous + 候选行号
        fs.WriteTextAuto(p, "行一\n重复词\n行三\n重复词\n行五");
        TextReplaceOutcome amb = fs.ReplaceTextAuto(p, "重复词", "X", "exact");
        Assert.Equal(TextReplaceStatus.Ambiguous, amb.Status);
        Assert.True(amb.CandidateLines.Length >= 2);
        // 文件未被修改（绝不静默写入）
        Assert.Contains("重复词", fs.ReadTextAuto(p));
        // [段4] ignore_case 唯一命中
        fs.WriteTextAuto(p, "Hello world");
        TextReplaceOutcome ic = fs.ReplaceTextAuto(p, "hello", "HELLO", "ignore_case");
        Assert.Equal(TextReplaceStatus.Ok, ic.Status);
        Assert.Contains("HELLO world", fs.ReadTextAuto(p));
        // [段5] regex 全替换 + 捕获组
        fs.WriteTextAuto(p, "v1 v2 v3");
        TextReplaceOutcome re = fs.ReplaceTextAuto(p, "v(\\d)", "V$1", "regex");
        Assert.Equal(TextReplaceStatus.Ok, re.Status);
        Assert.Equal(3, re.Count);
        Assert.Equal("V1 V2 V3", fs.ReadTextAuto(p));
    }
    finally
    {
        TryDelete(baseDir);
    }
}        /// <summary>
/// 内容检索（P4——text-grep）：受控根内递归扫文本，返回 相对路径:行号:上下文；二进制跳过
/// </summary>
[Fact]
public void Grep_ContentSearchWithinRoot()
{
    string baseDir = Path.Combine(Path.GetTempPath(), "mau_grep_" + Guid.NewGuid().ToString("N"));
    string rw = Path.Combine(baseDir, "rw");
    Directory.CreateDirectory(rw);
    try
    {
        WorkspaceConfig.RootEntry[] entries = new WorkspaceConfig.RootEntry[]
        {
            new WorkspaceConfig.RootEntry()
            {
                Id = "rw",
                Path = rw,
                Writable = true
            }
        };
        FileSystemService fs = new FileSystemService(entries, Path.Combine(rw, "recycle"));
        Directory.CreateDirectory(Path.Combine(rw, "sub"));
        fs.WriteText(Path.Combine(rw, "a.txt"), "hello world\nfoo bar");
        fs.WriteText(Path.Combine(rw, "sub", "b.txt"), "line with target here\nnothing");
        string[] hits = fs.Grep(rw, "target", "*", 100);
        Assert.Single(hits);
        Assert.Contains("b.txt:1:", hits[0]);
        Assert.Contains("target", hits[0]);
        // 大小写敏感——hello 不命中 HELLO
        fs.WriteText(Path.Combine(rw, "sub", "c.txt"), "HELLO case");
        string[] sensitive = fs.Grep(rw, "hello", "*", 100);
        Assert.Single(sensitive);
    }
    finally
    {
        TryDelete(baseDir);
    }
}/// <summary>
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