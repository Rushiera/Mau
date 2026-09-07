using CH4;
using Xunit;

namespace CatHome4.Core.Tests
{
    /// <summary>
    /// ToolSummaryFormatter 测试——P7 工具折叠气泡 summary 格式化（对齐 CH2 LLMToolDisplay）。
    /// 覆盖：TextCat 读写替换检索 / CsCat 统计 / ConfigCat / MauCat / PsCat / 内置 / 未知兜底 / 单行化。
    /// </summary>
    public class ToolSummaryFormatterTests
    {
        // ── TextCat——读取 ──

        [Fact]
        public void TextRead_SingleLineShort()
        {
            string s = ToolSummaryFormatter.Build("text-read", "{\"path\":\"a.txt\"}", "hello");
            Assert.Equal("读取文件 \"a.txt\" → \"hello\"", s);
        }

        [Fact]
        public void TextRead_MultiLine_ShowsLinesAndSize()
        {
            string s = ToolSummaryFormatter.Build("text-read", "{\"path\":\"a.txt\"}", "hello\nworld");
            Assert.Equal("读取文件 \"a.txt\" → \"hello（2行 · 11 B）\"", s);
        }

        [Fact]
        public void TextReadBetween_ShowsAnchors()
        {
            string s = ToolSummaryFormatter.Build("text-read_between", "{\"path\":\"a.txt\",\"str1\":\"## 一\",\"str2\":\"## 二\"}", "body");
            Assert.Equal("区间读取 \"a.txt\" 「## 一」 ~ 「## 二」 → \"body\"", s);
        }

        [Fact]
        public void TextReadLines_ShowsRange()
        {
            string s = ToolSummaryFormatter.Build("text-read_lines", "{\"path\":\"C:\\\\a.txt\",\"start_line\":\"10\",\"end_line\":\"20\"}", "line10");
            Assert.Equal("按行读取 \"a.txt\" \"第 10 行到第 20 行\" → \"line10\"", s);
        }

        // ── TextCat——写入 ──

        [Fact]
        public void TextWrite_ShowsContentPreview()
        {
            string s = ToolSummaryFormatter.Build("text-write", "{\"path\":\"a.txt\",\"content\":\"hi\"}", "写入成功 (overwrite): a.txt");
            Assert.Equal("写入文件 \"a.txt\" ← \"hi\" → OK", s);
        }

        [Fact]
        public void TextAppend_ShowsContentPreview()
        {
            string s = ToolSummaryFormatter.Build("text-append", "{\"path\":\"a.txt\",\"content\":\"tail\"}", "追加成功: a.txt");
            Assert.Equal("追加文本 \"a.txt\" ← \"tail\" → OK", s);
        }

        // ── TextCat——修改/操作 ──

        [Fact]
        public void TextReplace_ShowsByteCounts()
        {
            string s = ToolSummaryFormatter.Build("text-replace", "{\"path\":\"a.txt\",\"str\":\"old\",\"new_str\":\"new\"}", "替换成功");
            Assert.Equal("替换文本 \"a.txt\" → \"new\"(3B) 覆盖 \"old\"(3B)", s);
        }

        [Fact]
        public void TextFind_ShowsHitStats()
        {
            string s = ToolSummaryFormatter.Build("text-find", "{\"dir\":\"D:\\\\x\",\"pattern\":\"**/*.cs\"}", "a.cs\nb.cs\nsub/c.cs");
            Assert.Equal("搜索文件 \"D:\\x\" glob=\"**/*.cs\" → 3文件（\"a.cs\", \"b.cs\", \"sub/c.cs\"）", s);
        }

        [Fact]
        public void TextGrep_ShowsHitStats()
        {
            string s = ToolSummaryFormatter.Build("text-grep", "{\"dir\":\"D:\\\\x\",\"keyword\":\"Cat\"}", "a.cs:1: Cat\nb.cs:2: Cat\nb.cs:5: Cat");
            Assert.Equal("检索内容 \"D:\\x\" 含 \"Cat\" → 2文件·3命中（\"a.cs\", \"b.cs\"）", s);
        }

        [Fact]
        public void TextTree_ShowsFileDirCount()
        {
            string s = ToolSummaryFormatter.Build("text-tree", "{\"path\":\"C:\\\\x\",\"depth\":\"2\"}", "  a.cs (1 KB)\n  dir/\n");
            Assert.StartsWith("展开目录 \"C:\\x\" depth=\"2\" → \"1文件 1目录", s);
        }

        [Fact]
        public void TextMove_ShowsSrcDest()
        {
            string s = ToolSummaryFormatter.Build("text-move", "{\"src\":\"C:\\\\a.txt\",\"dest\":\"C:\\\\b.txt\"}", "移动成功: C:\\a.txt → C:\\b.txt");
            Assert.StartsWith("移动文件 \"C:\\a.txt\" → \"C:\\b.txt\" → \"移动成功", s);
        }

        [Fact]
        public void TextDelete_ShowsResult()
        {
            string s = ToolSummaryFormatter.Build("text-delete", "{\"path\":\"C:\\\\a.txt\"}", "软删除成功: DeleteIn20260907_a.txt");
            Assert.Contains("删除文件 \"C:\\a.txt\"", s);
            Assert.Contains("软删除成功", s);
        }

        // ── CsCat ──

        [Fact]
        public void CsCheck_JsonResult_ShowsErrWarn()
        {
            string s = ToolSummaryFormatter.Build("cs-check", "{\"csproj\":\"X.csproj\"}", "{\"ok\":true,\"errors\":0,\"warnings\":2}");
            Assert.Equal("C#检查 \"X.csproj\" → OK（共0错 2警）", s);
        }

        [Fact]
        public void CsCheck_JsonFail_ShowsFail()
        {
            string s = ToolSummaryFormatter.Build("cs-check", "{}", "{\"ok\":false,\"errors\":3,\"warnings\":1}");
            Assert.Equal("C#检查 → FAIL（共3错 1警）", s);
        }

        [Fact]
        public void CsDead_TextStats()
        {
            string s = ToolSummaryFormatter.Build("cs-dead", "{}", "共 3 个零引用成员");
            Assert.Equal("C#死代码扫描 → OK（\"共3个死代码\"）", s);
        }

        [Fact]
        public void CsCommentCheck_TextStats()
        {
            string s = ToolSummaryFormatter.Build("cs-comment_check", "{}", "共 2 处缺少 summary 注释");
            Assert.Equal("C#注释检查 → OK（\"共2处缺注释\"）", s);
        }

        [Fact]
        public void CsList_TextStats()
        {
            string s = ToolSummaryFormatter.Build("cs-list", "{\"class\":\"A\"}", "共 10 成员");
            Assert.Equal("C#列表 \"A\" → OK（\"共10成员\"）", s);
        }

        [Fact]
        public void CsPatch_ShowsClassMemberRange()
        {
            string s = ToolSummaryFormatter.Build("cs-patch", "{\"class\":\"A\",\"method\":\"B\",\"start_line\":\"3\",\"end_line\":\"5\"}", "OK");
            Assert.Equal("C#修改 \"A\".\"B\" L\"3\"~\"5\" → OK", s);
        }

        // ── ConfigCat / MauCat / PsCat ──

        [Fact]
        public void ConfigSet_ShowsKeyValue()
        {
            string s = ToolSummaryFormatter.Build("config-set", "{\"key\":\"ui.chat_font_size\",\"value\":\"16\"}", "OK");
            Assert.Equal("设置配置 \"ui.chat_font_size\"=\"16\" → \"OK\"", s);
        }

        [Fact]
        public void MauVerify_ShowsFile()
        {
            string s = ToolSummaryFormatter.Build("mau-verify", "{\"file\":\"a.mau\"}", "MAU_VERIFY_OK");
            Assert.Equal("Mau验证 \"a.mau\" → \"MAU_VERIFY_OK\"", s);
        }

        [Fact]
        public void Powershell_ShowsCommand()
        {
            string s = ToolSummaryFormatter.Build("powershell", "{\"command\":\"dir CatTemp\"}", "hello");
            Assert.Equal("执行命令 \"dir CatTemp\" → \"hello\"", s);
        }

        [Fact]
        public void WebSearch_ShowsQuery()
        {
            string s = ToolSummaryFormatter.Build("web-search", "{\"query\":\"Mau 语法\"}", "结果1\n结果2");
            Assert.Equal("搜索网页 \"Mau 语法\" → \"结果1（2行 · 7 B）\"", s);
        }

        [Fact]
        public void TempExec_ShowsKey()
        {
            string s = ToolSummaryFormatter.Build("temp-exec", "{\"key\":\"mykey\"}", "done");
            Assert.Equal("临时执行 \"mykey\" → \"done\"", s);
        }

        // ── 内置工具 ──

        [Fact]
        public void Note_ShowsProgress()
        {
            string s = ToolSummaryFormatter.Build("Note", "{\"action\":\"set\",\"content\":\"任务1\"}", "返回当前第2/5条 已完成1 待完成4 任务目标：测试任务");
            Assert.Equal("任务追踪 → 第\"2/5\"条 完成\"1\" 待做\"4\" \"测试任务\"", s);
        }

        [Fact]
        public void Random_ShowsRange()
        {
            string s = ToolSummaryFormatter.Build("random", "{\"min\":1,\"max\":10}", "7");
            Assert.Equal("随机数 \"[1,10)\" → \"7\"", s);
        }

        [Fact]
        public void Time_ShowsResult()
        {
            string s = ToolSummaryFormatter.Build("time", "{}", "2026-09-07 17:00:00");
            Assert.Equal("获取时间 → \"2026-09-07 17:00:00\"", s);
        }

        // ── 兜底与边界 ──

        [Fact]
        public void UnknownTool_ShowsParamsAndResult()
        {
            string s = ToolSummaryFormatter.Build("weird-tool", "{\"x\":\"y\"}", "zzz");
            Assert.Equal("调用工具 \"weird-tool\" 参数=1个 → \"zzz\"", s);
        }

        [Fact]
        public void NullArgs_NoCrash()
        {
            string s = ToolSummaryFormatter.Build("text-read", null, null);
            Assert.Equal("读取文件 \"\" → \"空\"", s);
        }

        [Fact]
        public void MultiLine_SingleLined()
        {
            string s = ToolSummaryFormatter.Build("text-read", "{\"path\":\"a\\nb.txt\"}", "x");
            Assert.DoesNotContain("\n", s);
            Assert.DoesNotContain("\r", s);
        }

        [Fact]
        public void LongPath_Truncated()
        {
            string longPath = "C:\\very\\long\\directory\\path\\that\\exceeds\\fifty\\five\\characters\\for\\sure\\file.txt";
            string s = ToolSummaryFormatter.Build("text-read", "{\"path\":\"" + longPath.Replace("\\", "\\\\") + "\"}", "x");
            Assert.Contains("file.txt", s);
            Assert.StartsWith("读取文件", s);
        }
    }
}
