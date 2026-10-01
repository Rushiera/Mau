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
        /// 锚点歧义三态——Ambiguous 必须带真实出现次数（曾漏赋值 ⇒ 报「锚点出现 0 次」自相矛盾），且绝不静默写入
        /// </summary>
        [Fact]
        public void ReplaceTextAuto_Ambiguous_ReportsRealCount()
        {
            string baseDir = Path.Combine(Path.GetTempPath(), "mau_replace_test_" + Guid.NewGuid().ToString("N"));
            string rw = Path.Combine(baseDir, "rw");
            Directory.CreateDirectory(rw);
            try
            {
                WorkspaceConfig.RootEntry[] entries = new WorkspaceConfig.RootEntry[]
                {
                    new WorkspaceConfig.RootEntry() { Id = "rw", Path = rw, Writable = true }
                };
                FileSystemService fs = new FileSystemService(entries, Path.Combine(rw, "recycle"));
                string file = Path.Combine(rw, "probe.txt");
                fs.WriteText(file, "A行B行C行");
                TextReplaceOutcome outcome = fs.ReplaceTextAuto(file, "行", "X", "exact");
                Assert.Equal(TextReplaceStatus.Ambiguous, outcome.Status);
                Assert.Equal(3, outcome.Count);
                Assert.Equal("A行B行C行", fs.ReadText(file));
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
        /// 裸根名适配——整串即根 id（无冒号）→ 该根根目录（"rw" ≡ "rw:"；2026-09-18）
        /// 负例：非根名仍按默认根下相对路径解析（不误判）。
        /// </summary>
        [Fact]
        public void BareRootId_ResolvesToRootDirectory()
        {
            string baseDir = Path.Combine(Path.GetTempPath(), "mau_bare_root_" + Guid.NewGuid().ToString("N"));
            string rw = Path.Combine(baseDir, "rw");
            Directory.CreateDirectory(rw);
            try
            {
                WorkspaceConfig.RootEntry[] entries = new WorkspaceConfig.RootEntry[]
                {
                    new WorkspaceConfig.RootEntry() { Id = "rw", Path = rw, Writable = true }
                };
                FileSystemService fs = new FileSystemService(entries, Path.Combine(rw, "recycle"));
                fs.WriteText("rw:probe.txt", "x");
                // 裸根名 ≡ 带冒号形态（大小写宽容）——且能真实列树
                Assert.Equal(Path.GetFullPath(rw), fs.Resolve("rw", false));
                Assert.Equal(Path.GetFullPath(rw), fs.Resolve("rw:", false));
                Assert.Equal(Path.GetFullPath(rw), fs.Resolve("RW", false));
                Assert.Contains("probe.txt", fs.Tree("rw", 1, 10));
                // 非根名——不误判为根，仍按默认根下相对路径（目录不存在 → 显式失败）
                Assert.Throws<DirectoryNotFoundException>(() => fs.Tree("not-a-root", 1, 10));
            }
            finally
            {
                TryDelete(baseDir);
            }
        }
        /// <summary>编码内建 + 两态风格（P1/P2——design-ch4-text-tools §四）：新建按类型契约（.md/.cs/.mau 带 BOM、.cs/.mau CRLF、.bat GBK+CRLF），既有文件 BOM/换行保真，读侧 BOM 剥离。</summary>
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
                // [段2] 新建 .cs 按工程与语料契约——BOM + CRLF（A25）
                string csPath = Path.Combine(rw, "a.cs");
                fs.WriteTextAuto(csPath, "using System;\nclass A\n{\n}\n");
                byte[] csBytes = File.ReadAllBytes(csPath);
                Assert.True(csBytes.Length >= 3 && csBytes[0] == 0xEF && csBytes[1] == 0xBB && csBytes[2] == 0xBF, ".cs 应带 UTF-8 BOM");
                Assert.True(IsAllCrLf(csBytes), ".cs 换行应为 CRLF");
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
                // [段6] 既有文件保真——无 BOM + LF 的 .cs 覆写后两态不变（不补契约）
                string legacyPath = Path.Combine(rw, "legacy.cs");
                File.WriteAllText(legacyPath, "class L\n{\n}\n", new System.Text.UTF8Encoding(false));
                fs.WriteTextAuto(legacyPath, "class L\n{\n    int x;\n}\n");
                byte[] legacyBytes = File.ReadAllBytes(legacyPath);
                Assert.False(legacyBytes.Length >= 3 && legacyBytes[0] == 0xEF && legacyBytes[1] == 0xBB && legacyBytes[2] == 0xBF, "既有无 BOM .cs 不应被补 BOM");
                Assert.DoesNotContain("\r\n", File.ReadAllText(legacyPath, new System.Text.UTF8Encoding(false)));
                // [段7] 既有 BOM + CRLF 的 .cs 覆写后保真（不剥 BOM / 不改换行）
                string bomPath = Path.Combine(rw, "keep.cs");
                File.WriteAllText(bomPath, "class K\r\n{\r\n}\r\n", new System.Text.UTF8Encoding(true));
                fs.WriteTextAuto(bomPath, "class K\n{\n    int y;\n}\n");
                byte[] bomBytes = File.ReadAllBytes(bomPath);
                Assert.True(bomBytes.Length >= 3 && bomBytes[0] == 0xEF && bomBytes[1] == 0xBB && bomBytes[2] == 0xBF, "既有 BOM .cs 应保真带 BOM");
                Assert.True(IsAllCrLf(bomBytes), "既有 CRLF .cs 应保持 CRLF");
                // [段8] 新建 .mau 契约——BOM + CRLF（语料族同族）
                string mauPath = Path.Combine(rw, "flow.mau");
                fs.WriteTextAuto(mauPath, "§ 'S_A' = { 'A' }\n");
                byte[] mauBytes = File.ReadAllBytes(mauPath);
                Assert.True(mauBytes.Length >= 3 && mauBytes[0] == 0xEF && mauBytes[1] == 0xBB && mauBytes[2] == 0xBF, ".mau 应带 UTF-8 BOM");
                Assert.True(IsAllCrLf(mauBytes), ".mau 换行应为 CRLF");
            }
            finally
            {
                TryDelete(baseDir);
            }
        }
        /// <summary>
        /// 全 CRLF 判定——每个 0x0A 都紧跟 0x0D 之后（文件级换行风格断言）
        /// </summary>
        /// <param name="raw">原始字节</param>
        /// <returns>true=换行全为 CRLF</returns>
        private static bool IsAllCrLf(byte[] raw)
        {
            for (int i = 0; i < raw.Length; i = i + 1)
            {
                if (raw[i] == 0x0A && (i == 0 || raw[i - 1] != 0x0D))
                {
                    return false;
                }
            }
            return true;
        }
        /// <summary>
        /// Web 资产族契约——新建 .html/.js/.css 落 CRLF + 无 BOM（仓库实测全量 CRLF；A25 延伸面）
        /// </summary>
        [Fact]
        public void WebAsset_NewFile_IsCrLfNoBom()
        {
            string baseDir = Path.Combine(Path.GetTempPath(), "mau_webenc_" + Guid.NewGuid().ToString("N"));
            string rw = Path.Combine(baseDir, "rw");
            Directory.CreateDirectory(rw);
            try
            {
                WorkspaceConfig.RootEntry[] entries = new WorkspaceConfig.RootEntry[]
                {
                            new WorkspaceConfig.RootEntry() { Id = "rw", Path = rw, Writable = true }
                };
                FileSystemService fs = new FileSystemService(entries, Path.Combine(rw, "recycle"));
                string[] names = new string[] { "index.html", "app.js", "site.css" };
                for (int i = 0; i < names.Length; i = i + 1)
                {
                    string target = Path.Combine(rw, names[i]);
                    fs.WriteTextAuto(target, "line1\nline2\n");
                    byte[] raw = File.ReadAllBytes(target);
                    Assert.False(raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF, names[i] + " 不应带 BOM");
                    Assert.True(IsAllCrLf(raw), names[i] + " 换行应为 CRLF");
                }
            }
            finally
            {
                TryDelete(baseDir);
            }
        }
        /// <summary>
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
         /// **/ 目录通配前缀——剥前缀 + 强制递归（text-find 的 **/*.txt 语义）；纯文件名模式非递归对照
         /// </summary>
        [Fact]
        public void Find_DirWildcardPrefixForcesRecursive()
        {
            string baseDir = Path.Combine(Path.GetTempPath(), "mau_find_wild_" + Guid.NewGuid().ToString("N"));
            string rw = Path.Combine(baseDir, "rw");
            Directory.CreateDirectory(Path.Combine(rw, "sub", "deep"));
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
                fs.WriteText(Path.Combine(rw, "a.txt"), "x");
                fs.WriteText(Path.Combine(rw, "sub", "b.txt"), "x");
                fs.WriteText(Path.Combine(rw, "sub", "deep", "c.cs"), "x");
                fs.WriteText(Path.Combine(rw, "sub", "d.md"), "x");
                // [段1] **/*.txt 前缀——recursive=false 也被强制递归，返回全部 txt
                string[] allTxt = fs.Find(rw, "**/*.txt", false, 100);
                Assert.Equal(2, allTxt.Length);
                Assert.Contains("a.txt", allTxt);
                Assert.Contains("sub" + Path.DirectorySeparatorChar + "b.txt", allTxt);
                // [段2] 纯文件名模式非递归——只扫当前目录
                string[] topTxt = fs.Find(rw, "*.txt", false, 100);
                Assert.Single(topTxt);
                Assert.Contains("a.txt", topTxt);
                // [段3] **/*.cs 递归——深层文件命中
                string[] cs = fs.Find(rw, "**/*.cs", true, 100);
                Assert.Single(cs);
                Assert.Contains("sub" + Path.DirectorySeparatorChar + "deep" + Path.DirectorySeparatorChar + "c.cs", cs);
                // [段4] **/ 剥空回退 *——全部文件
                string[] all = fs.Find(rw, "**/", true, 100);
                Assert.Equal(4, all.Length);
            }
            finally
            {
                TryDelete(baseDir);
            }
        }/// <summary>
         /// ReadLinesAuto 编码契约——.md BOM 剥离、.bat GBK 中文行、行号格式与 ReadLines 一致（P5 编码契约补全）
         /// </summary>
        [Fact]
        public void ReadLinesAuto_EncodingContractAndLineFormat()
        {
            string baseDir = Path.Combine(Path.GetTempPath(), "mau_rlauto_" + Guid.NewGuid().ToString("N"));
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
                // [段1] .md 带 BOM 读取——首行无 \uFEFF 残留（Ordinal 比较——U+FEFF 是格式字符，文化比较会当可忽略字符误匹配）
                string mdPath = Path.Combine(rw, "doc.md");
                fs.WriteTextAuto(mdPath, "标题\n正文一\n正文二");
                string mdLines = fs.ReadLinesAuto(mdPath, 1, 0);
                Assert.Contains("1: 标题", mdLines);
                Assert.DoesNotContain("\uFEFF", mdLines, StringComparison.Ordinal);
                // [段2] .bat GBK 中文行号读取
                string batPath = Path.Combine(rw, "run.bat");
                fs.WriteTextAuto(batPath, "echo 你好\necho 再见");
                string batLines = fs.ReadLinesAuto(batPath, 1, 0);
                Assert.Contains("1: echo 你好", batLines);
                Assert.Contains("2: echo 再见", batLines);
                // [段3] 区间裁剪——end 指定行数
                string txtPath = Path.Combine(rw, "a.txt");
                fs.WriteTextAuto(txtPath, "l1\nl2\nl3");
                string range = fs.ReadLinesAuto(txtPath, 2, 3);
                Assert.Equal("2: l2\n3: l3", range);
                // [段4] CRLF 文件行尾不残留 \r
                string crlfPath = Path.Combine(rw, "b.txt");
                File.WriteAllText(crlfPath, "x\r\ny\r\n");
                string crlfLines = fs.ReadLinesAuto(crlfPath, 1, 0);
                Assert.DoesNotContain("\r", crlfLines);
            }
            finally
            {
                TryDelete(baseDir);
            }
        }/// <summary>
         /// Grep 编码内建——GBK .bat 中文关键词命中、.md BOM 剥离后可搜（P5 编码契约补全）
         /// </summary>
        [Fact]
        public void Grep_EncodingAwareContentSearch()
        {
            string baseDir = Path.Combine(Path.GetTempPath(), "mau_grepenc_" + Guid.NewGuid().ToString("N")); string rw = Path.Combine(baseDir, "rw"); Directory.CreateDirectory(rw); try
            {
                WorkspaceConfig.RootEntry[] entries = new WorkspaceConfig.RootEntry[] { new WorkspaceConfig.RootEntry() { Id = "rw", Path = rw, Writable = true } }; FileSystemService fs = new FileSystemService(entries, Path.Combine(rw, "recycle"));  // [段1] GBK 文件中文命中——.bat 写中文 → grep 中文关键词
                string batPath = Path.Combine(rw, "run.bat"); fs.WriteTextAuto(batPath, "echo 你好世界\necho 再见"); string[] batHits = fs.Grep(rw, "你好", "*", 100); Assert.Single(batHits); Assert.Contains("run.bat:1:", batHits[0]); Assert.Contains("你好", batHits[0]);  // [段2] .md BOM 文件命中——BOM 剥离后首行可搜
                string mdPath = Path.Combine(rw, "doc.md"); fs.WriteTextAuto(mdPath, "# 标题\n正文含目标"); string[] mdHits = fs.Grep(rw, "目标", "*", 100); Assert.Single(mdHits); Assert.Contains("doc.md:2:", mdHits[0]);  // [段3] 大小写敏感仍成立——无 HELLO 命中
                string[] noHit = fs.Grep(rw, "HELLO", "*", 100); Assert.Empty(noHit);
            }
            finally { TryDelete(baseDir); }
        }

        /// <summary>
        /// Q4 忽略目录（2026-09-08）——Tree 跳过 .git/bin/obj 并附 [git] 特征行；Find/Grep 跳过忽略目录并附 [skip] 提示；显式以忽略名为根不受影响
        /// </summary>
        [Fact]
        public void IgnoredDirs_TreeSkipped_GitInfo_AndSkipHint()
        {
            string baseDir = Path.Combine(Path.GetTempPath(), "mau_ignored_" + Guid.NewGuid().ToString("N"));
            string rw = Path.Combine(baseDir, "rw");
            Directory.CreateDirectory(rw);
            try
            {
                WorkspaceConfig.RootEntry[] entries = new WorkspaceConfig.RootEntry[]
                {
            new WorkspaceConfig.RootEntry() { Id = "rw", Path = rw, Writable = true }
                };
                FileSystemService fs = new FileSystemService(entries, Path.Combine(rw, "recycle"));
                // 布置——.git（含 HEAD + refs/heads/main）、bin 忽略目录、正常文件
                Directory.CreateDirectory(Path.Combine(rw, ".git", "refs", "heads"));
                File.WriteAllText(Path.Combine(rw, ".git", "HEAD"), "ref: refs/heads/main\n");
                File.WriteAllText(Path.Combine(rw, ".git", "refs", "heads", "main"), "abcdef0123456789\n");
                Directory.CreateDirectory(Path.Combine(rw, "bin"));
                File.WriteAllText(Path.Combine(rw, "bin", "x.dll"), "x");
                File.WriteAllText(Path.Combine(rw, "keep.txt"), "keep");
                Directory.CreateDirectory(Path.Combine(rw, "sub", ".git"));
                File.WriteAllText(Path.Combine(rw, "sub", "sub.txt"), "sub");
                // [段1] Tree——.git/bin 不列出；末尾 [git] 特征行（多 .git 多行）；正常条目保留
                string[] tree = fs.Tree(rw, 5, 500);
                Assert.DoesNotContain(tree, line => line.Contains(".git") && !line.StartsWith("[git]") && !line.StartsWith("[skip]"));
                // Tree 忽略计数提示——与 Find/Grep 同口径（2026-09-28 补齐；原为静默跳过）
                Assert.Contains(tree, line => line.StartsWith("[skip] "));
                Assert.DoesNotContain(tree, line => line == "bin/");
                Assert.Contains(tree, line => line == "keep.txt");
                Assert.Contains(tree, line => line.StartsWith("[git] 存在 .git（.git——HEAD: refs/heads/main", StringComparison.Ordinal));
                Assert.Contains(tree, line => line.StartsWith("[git] 存在 .git（sub", StringComparison.Ordinal));
                // [段2] Find——忽略目录内条目不列出 + [skip] 提示（提示行本身含 .git/bin 字样——按前缀排除）
                string[] found = fs.Find(rw, "*", true, 500);
                Assert.DoesNotContain(found, line => line.StartsWith("bin"));
                Assert.DoesNotContain(found, line => line.StartsWith(".git"));
                Assert.Contains(found, line => line.StartsWith("[skip] "));
                Assert.Contains(found, line => line == "keep.txt");
                // [段3] Grep——忽略目录不扫描 + [skip] 提示（bin 内文本不会被搜到；提示行本身是结果的一部分）
                string[] hits = fs.Grep(rw, "x.dll", "*", 100);
                Assert.DoesNotContain(hits, line => !line.StartsWith("[skip]"));
                string[] hits2 = fs.Grep(rw, "keep", "*", 100);
                Assert.Contains(hits2, line => line.StartsWith("keep.txt:"));
                // [段4] 显式以忽略名为根——bin 本身可列（忽略只在子项遍历生效）
                string[] binTree = fs.Tree(Path.Combine(rw, "bin"), 2, 100);
                Assert.Contains(binTree, line => line == "x.dll");
            }
            finally
            {
                TryDelete(baseDir);
            }
        }

        /// <summary>
        /// 软删除回收——非空目录整棵子树迁移进回收站、结构保留、统计准确（2026-09-11 放开目录回收）
        /// </summary>
        [Fact]
        public void Recycle_NonEmptyDirectory_MovesWholeTree()
        {
            string baseDir = Path.Combine(Path.GetTempPath(), "mau_recycle_test_" + Guid.NewGuid().ToString("N"));
            string rw = Path.Combine(baseDir, "rw");
            Directory.CreateDirectory(rw);
            try
            {
                FileSystemService fs = new FileSystemService(
                    new WorkspaceConfig.RootEntry[] { new WorkspaceConfig.RootEntry() { Id = "rw", Path = rw, Writable = true } },
                    Path.Combine(rw, "recycle"));
                string target = Path.Combine(rw, "probe");
                Directory.CreateDirectory(Path.Combine(target, "b"));
                Directory.CreateDirectory(Path.Combine(target, "d"));
                File.WriteAllText(Path.Combine(target, "b", "c.txt"), "hello");
                File.WriteAllBytes(Path.Combine(target, "b", "c.bin"), new byte[1000]);
                File.WriteAllText(Path.Combine(target, "d", "e.txt"), "xy");
                RecycleOutcome outcome = fs.Recycle(target);
                Assert.True(outcome.IsDirectory);
                Assert.Equal(3, outcome.FileCount);
                Assert.Equal(2, outcome.DirectoryCount);
                Assert.Equal(1007, outcome.TotalBytes);
                Assert.False(Directory.Exists(target));
                Assert.True(File.Exists(Path.Combine(outcome.Target, "b", "c.txt")));
                Assert.True(File.Exists(Path.Combine(outcome.Target, "d", "e.txt")));
            }
            finally
            {
                TryDelete(baseDir);
            }
        }

        /// <summary>
        /// 软删除回收——单文件：文件数 1、体积为文件长度、原路径消失
        /// </summary>
        [Fact]
        public void Recycle_File_ReportsSingleItemStats()
        {
            string baseDir = Path.Combine(Path.GetTempPath(), "mau_recycle_test_" + Guid.NewGuid().ToString("N"));
            string rw = Path.Combine(baseDir, "rw");
            Directory.CreateDirectory(rw);
            try
            {
                FileSystemService fs = new FileSystemService(
                    new WorkspaceConfig.RootEntry[] { new WorkspaceConfig.RootEntry() { Id = "rw", Path = rw, Writable = true } },
                    Path.Combine(rw, "recycle"));
                string target = Path.Combine(rw, "single.txt");
                File.WriteAllText(target, "abcdef");
                RecycleOutcome outcome = fs.Recycle(target);
                Assert.False(outcome.IsDirectory);
                Assert.Equal(1, outcome.FileCount);
                Assert.Equal(6, outcome.TotalBytes);
                Assert.False(File.Exists(target));
                Assert.True(File.Exists(outcome.Target));
            }
            finally
            {
                TryDelete(baseDir);
            }
        }

        /// <summary>
        /// 软删除回收——受控根本身拒绝回收（软删可恢复，但根被移走等于运行面整体失踪）
        /// </summary>
        [Fact]
        public void Recycle_ControlledRoot_Throws()
        {
            string baseDir = Path.Combine(Path.GetTempPath(), "mau_recycle_test_" + Guid.NewGuid().ToString("N"));
            string rw = Path.Combine(baseDir, "rw");
            Directory.CreateDirectory(rw);
            try
            {
                FileSystemService fs = new FileSystemService(
                    new WorkspaceConfig.RootEntry[] { new WorkspaceConfig.RootEntry() { Id = "rw", Path = rw, Writable = true } },
                    Path.Combine(rw, "recycle"));
                Assert.Throws<InvalidOperationException>(() => fs.Recycle(rw));
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
            catch (Exception ex)
            {
                // 清理尽力而为
                Console.WriteLine("[测试清理] 临时目录删除失败: " + ex.Message);
            }
        }
        /// <summary>
        /// Tree 广度优先与截断提示（2026-09-28）——同层条目先出（顶层结构不被深层吃掉）；limit 触顶时末行附 [截断] 提示
        /// </summary>
        [Fact]
        public void Tree_BreadthFirst_TruncationHint()
        {
            string baseDir = Path.Combine(Path.GetTempPath(), "mau_tree_bfs_" + Guid.NewGuid().ToString("N"));
            string rw = Path.Combine(baseDir, "rw");
            Directory.CreateDirectory(rw);
            try
            {
                FileSystemService fs = new FileSystemService(
                    new WorkspaceConfig.RootEntry[] { new WorkspaceConfig.RootEntry() { Id = "rw", Path = rw, Writable = true } },
                    Path.Combine(rw, "recycle"));
                // 布置——三个顶层目录各含子项（广度优先下同层先出）
                Directory.CreateDirectory(Path.Combine(rw, "aa", "deep"));
                File.WriteAllText(Path.Combine(rw, "aa", "a1.txt"), "a");
                File.WriteAllText(Path.Combine(rw, "aa", "deep", "a2.txt"), "a");
                Directory.CreateDirectory(Path.Combine(rw, "bb"));
                File.WriteAllText(Path.Combine(rw, "bb", "b1.txt"), "b");
                Directory.CreateDirectory(Path.Combine(rw, "cc"));
                File.WriteAllText(Path.Combine(rw, "cc", "c1.txt"), "c");
                // [段1] 广度优先——顶层三目录占据前三行（深度优先会把 aa 的整棵子树提前）
                string[] all = fs.Tree(rw, 3, 500);
                Assert.Equal(new string[] { "aa/", "bb/", "cc/" }, new string[] { all[0], all[1], all[2] });
                // [段2] limit 截断——先保顶层（aa/bb/cc 一个不少）+ 末行 [截断] 提示
                string[] cut = fs.Tree(rw, 3, 3);
                Assert.Contains(cut, line => line == "aa/");
                Assert.Contains(cut, line => line == "bb/");
                Assert.Contains(cut, line => line == "cc/");
                // 截断提示含总量——总数取未截断时的条目数（不写死字面量）
                Assert.StartsWith("[截断] 共 " + all.Length.ToString() + " 条，已列 3 条", cut[cut.Length - 1]);
                // [段3] 未截断——无 [截断] 行
                Assert.DoesNotContain(all, line => line.StartsWith("[截断]"));
            }
            finally
            {
                TryDelete(baseDir);
            }
        }
        /// <summary>
        /// Find limit 截断提示（2026-09-28）——达上限时末行附 [截断]（原为静默截断）；未触顶无该行
        /// </summary>
        [Fact]
        public void Find_LimitTruncationHint()
        {
            string baseDir = Path.Combine(Path.GetTempPath(), "mau_find_cut_" + Guid.NewGuid().ToString("N"));
            string rw = Path.Combine(baseDir, "rw");
            Directory.CreateDirectory(rw);
            try
            {
                FileSystemService fs = new FileSystemService(
                    new WorkspaceConfig.RootEntry[] { new WorkspaceConfig.RootEntry() { Id = "rw", Path = rw, Writable = true } },
                    Path.Combine(rw, "recycle"));
                for (int i = 1; i <= 5; i = i + 1)
                {
                    File.WriteAllText(Path.Combine(rw, "f" + i.ToString() + ".txt"), "x");
                }
                // [段1] 未触顶——5 条全出、无 [截断] 行
                string[] all = fs.Find(rw, "*.txt", true, 500);
                Assert.Equal(5, all.Length);
                Assert.DoesNotContain(all, line => line.StartsWith("[截断]"));
                // [段2] 触顶——3 条 + 末行 [截断]
                string[] cut = fs.Find(rw, "*.txt", true, 3);
                Assert.Equal(4, cut.Length);
                // 截断提示含总量——5 个匹配，limit=3
                Assert.StartsWith("[截断] 共 5 条，已列 3 条", cut[cut.Length - 1]);
            }
            finally
            {
                TryDelete(baseDir);
            }
        }
        /// <summary>
        /// Grep limit 截断提示（2026-09-28）——命中达上限时末行附 [截断]（含总条数）；未触顶无该行
        /// </summary>
        [Fact]
        public void Grep_LimitTruncationHint()
        {
            string baseDir = Path.Combine(Path.GetTempPath(), "mau_grep_cut_" + Guid.NewGuid().ToString("N"));
            string rw = Path.Combine(baseDir, "rw");
            Directory.CreateDirectory(rw);
            try
            {
                FileSystemService fs = new FileSystemService(
                    new WorkspaceConfig.RootEntry[] { new WorkspaceConfig.RootEntry() { Id = "rw", Path = rw, Writable = true } },
                    Path.Combine(rw, "recycle"));
                File.WriteAllText(Path.Combine(rw, "a.txt"), "hit\nhit\nhit\nhit");
                File.WriteAllText(Path.Combine(rw, "b.txt"), "hit\nhit");
                // [段1] 未触顶——6 命中全出、无 [截断] 行
                string[] all = fs.Grep(rw, "hit", "*", 100);
                Assert.Equal(6, all.Length);
                Assert.DoesNotContain(all, line => line.StartsWith("[截断]"));
                // [段2] 触顶——limit=2 出 2 条 + 末行 [截断] 共 6 条
                string[] cut = fs.Grep(rw, "hit", "*", 2);
                Assert.Equal(3, cut.Length);
                Assert.StartsWith("[截断] 共 6 条，已列 2 条", cut[cut.Length - 1]);
            }
            finally
            {
                TryDelete(baseDir);
            }
        }
    }
}