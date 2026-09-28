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

        /// <summary>
        /// 读取工具摘要——单行短文本原样展示。
        /// </summary>
        [Fact]
        public void TextRead_SingleLineShort()
        {
            string s = ToolSummaryFormatter.Build("text-read", "{\"path\":\"a.txt\"}", "hello");
            Assert.Equal("读取文件 \"a.txt\" → \"hello\"", s);
        }

        /// <summary>
        /// 读取工具摘要——多行结果展示行数与字节数。
        /// </summary>
        [Fact]
        public void TextRead_MultiLine_ShowsLinesAndSize()
        {
            string s = ToolSummaryFormatter.Build("text-read", "{\"path\":\"a.txt\"}", "hello\nworld");
            Assert.Equal("读取文件 \"a.txt\" → \"hello（2行 · 11 B）\"", s);
        }

        /// <summary>
        /// 区间读取摘要——展示起止锚点。
        /// </summary>
        [Fact]
        public void TextReadBetween_ShowsAnchors()
        {
            string s = ToolSummaryFormatter.Build("text-read_between", "{\"path\":\"a.txt\",\"str1\":\"## 一\",\"str2\":\"## 二\"}", "body");
            Assert.Equal("区间读取 \"a.txt\" 「## 一」 ~ 「## 二」 → \"body\"", s);
        }

        /// <summary>
        /// 按行读取摘要——展示行号区间。
        /// </summary>
        [Fact]
        public void TextReadLines_ShowsRange()
        {
            string s = ToolSummaryFormatter.Build("text-read_lines", "{\"path\":\"C:\\\\a.txt\",\"start_line\":\"10\",\"end_line\":\"20\"}", "line10");
            Assert.Equal("按行读取 \"a.txt\" \"第 10 行到第 20 行\" → \"line10\"", s);
        }

        // ── TextCat——写入 ──

        /// <summary>
        /// 写入工具摘要——展示内容预览。
        /// </summary>
        [Fact]
        public void TextWrite_ShowsContentPreview()
        {
            string s = ToolSummaryFormatter.Build("text-write", "{\"path\":\"a.txt\",\"content\":\"hi\"}", "写入成功 (overwrite): a.txt");
            Assert.Equal("写入文件 \"a.txt\" ← \"hi\" → OK", s);
        }

        /// <summary>
        /// 追加工具摘要——展示内容预览。
        /// </summary>
        [Fact]
        public void TextAppend_ShowsContentPreview()
        {
            string s = ToolSummaryFormatter.Build("text-append", "{\"path\":\"a.txt\",\"content\":\"tail\"}", "追加成功: a.txt");
            Assert.Equal("追加文本 \"a.txt\" ← \"tail\" → OK", s);
        }

        // ── TextCat——修改/操作 ──

        /// <summary>
        /// 替换工具摘要——展示新旧文本字节数。
        /// </summary>
        [Fact]
        public void TextReplace_ShowsByteCounts()
        {
            // 参数键名对齐真实工具契约（BRIK-TEXT-004：path/old/new/mode）——旧 str/new_str 是错误参数名
            string s = ToolSummaryFormatter.Build("text-replace", "{\"path\":\"a.txt\",\"old\":\"old\",\"new\":\"new\"}", "替换成功");
            Assert.Equal("替换文本 \"a.txt\" → \"new\"(3B) 覆盖 \"old\"(3B)", s);
        }

        /// <summary>
        /// 替换工具摘要——真实参数 old/new 显示真实字节数（旧参数名取空回归）。
        /// </summary>
        [Fact]
        public void TextReplace_RealArgs_NoZeroBytes()
        {
            // 回归——真实参数 old/new 必须显示真实字节数（旧 str/new_str 取空 → 0B 0B）
            string s = ToolSummaryFormatter.Build("text-replace", "{\"path\":\"x.md\",\"old\":\"anchor_unique\",\"new\":\"replaced_ok\"}", "OK 替换完成: 1 处（x.md）");
            Assert.Equal("替换文本 \"x.md\" → \"replaced_ok\"(11B) 覆盖 \"anchor_unique\"(13B)", s);
        }
        /// <summary>
        /// 替换工具摘要——删除标记按语义显示（不暴露契约串；A88）。
        /// </summary>
        [Fact]
        public void TextReplace_DeleteKey_ShowsSemanticDelete()
        {
            string s = ToolSummaryFormatter.Build("text-replace", "{\"path\":\"a.txt\",\"old\":\"old\",\"new\":\"黑暗剑+22\"}", "OK 替换完成: 1 处（a.txt）");
            Assert.Equal("替换文本 \"a.txt\" → （删除） 覆盖 \"old\"(3B)", s);
        }

        /// <summary>
        /// 文件搜索摘要——展示命中文件数与文件名列表。
        /// </summary>
        [Fact]
        public void TextFind_ShowsHitStats()
        {
            string s = ToolSummaryFormatter.Build("file-find", "{\"dir\":\"D:\\\\x\",\"pattern\":\"**/*.cs\"}", "a.cs\nb.cs\nsub/c.cs");
            Assert.Equal("搜索文件 \"D:\\x\" glob=\"**/*.cs\" → 3文件（\"a.cs\", \"b.cs\", \"sub/c.cs\"）", s);
        }
        /// <summary>
        /// 文件搜索摘要——提示行（[skip] / [截断]）不计入文件统计（2026-09-28）
        /// </summary>
        [Fact]
        public void TextFind_HintLinesExcluded()
        {
            string s = ToolSummaryFormatter.Build("file-find", "{\"dir\":\"D:\\\\x\",\"pattern\":\"**/*.cs\"}",
                "a.cs\nb.cs\n[skip] 3 个忽略目录（.git/bin/obj/node_modules 等）未扫描——条目在其内已跳过\n[截断] 已列 2 条（limit=2）——匹配未列全：收窄 dir / pattern 或提高 limit 可看全");
            Assert.Equal("搜索文件 \"D:\\x\" glob=\"**/*.cs\" → 2文件（\"a.cs\", \"b.cs\"）", s);
        }

        /// <summary>
        /// 内容检索摘要——展示命中文件数与命中次数。
        /// </summary>
        [Fact]
        public void TextGrep_ShowsHitStats()
        {
            string s = ToolSummaryFormatter.Build("text-grep", "{\"dir\":\"D:\\\\x\",\"keyword\":\"Cat\"}", "a.cs:1: Cat\nb.cs:2: Cat\nb.cs:5: Cat");
            Assert.Equal("检索内容 \"D:\\x\" 含 \"Cat\" → 2文件·3命中（\"a.cs\", \"b.cs\"）", s);
        }

        /// <summary>
        /// 目录树摘要——旧输出形态（含大小/尾斜杠）统计文件与目录数。
        /// </summary>
        [Fact]
        public void TextTree_ShowsFileDirCount()
        {
            string s = ToolSummaryFormatter.Build("file-tree", "{\"path\":\"C:\\\\x\",\"depth\":\"2\"}", "  a.cs (1 KB)\n  dir/\n");
            Assert.StartsWith("展开目录 \"C:\\x\" depth=\"2\" → \"1文件 1目录", s);
        }
        /// <summary>
        /// 目录树摘要——提示行（[git] / [skip] / [截断]）不计入文件与目录统计（2026-09-28）
        /// </summary>
        [Fact]
        public void TextTree_HintLinesExcluded()
        {
            string s = ToolSummaryFormatter.Build("file-tree", "{\"path\":\"C:\\\\x\",\"depth\":\"2\"}",
                "a/\na\\b.cs\n[skip] 3 个忽略目录（.git/bin/obj/node_modules 等）未扫描——条目在其内已跳过\n[截断] 共 93 条，已列 2 条——提高 limit 至 ≥93，或收窄 path / 降 depth 可看全");
            Assert.StartsWith("展开目录", s);
            Assert.Contains("1文件 1目录", s);
            Assert.Contains("·共 93 条", s);
        }

        /// <summary>
        /// 目录树摘要——AppendTree 真实输出（纯相对路径 + 目录行尾斜杠）统计文件与目录数。
        /// </summary>
        [Fact]
        public void TextTree_RealOutput_CountsFilesAndDirs()
        {
            // 回归——AppendTree 真实输出（纯相对路径 + 目录行 / 尾）必须正确统计（旧解析按 KB)/ 尾匹配 → 0文件 0目录）
            string s = ToolSummaryFormatter.Build("file-tree", "{\"path\":\"C:\\\\x\",\"depth\":\"2\"}", "a.txt\nb/\nc/d.cs");
            Assert.StartsWith("展开目录 \"C:\\x\" depth=\"2\" → \"2文件 1目录", s);
        }

        /// <summary>
        /// 移动工具摘要——展示源与目标路径。
        /// </summary>
        [Fact]
        public void TextMove_ShowsSrcDest()
        {
            string s = ToolSummaryFormatter.Build("file-move", "{\"src\":\"C:\\\\a.txt\",\"dest\":\"C:\\\\b.txt\"}", "移动成功: C:\\a.txt → C:\\b.txt");
            Assert.StartsWith("移动文件 \"C:\\a.txt\" → \"C:\\b.txt\" → \"移动成功", s);
        }

        /// <summary>
        /// 删除工具摘要——展示软删除结果（回收站改名）。
        /// </summary>
        [Fact]
        public void TextDelete_ShowsResult()
        {
            string s = ToolSummaryFormatter.Build("file-delete", "{\"path\":\"C:\\\\a.txt\"}", "软删除成功: DeleteIn20260907_a.txt");
            Assert.Contains("删除文件 \"C:\\a.txt\"", s);
            Assert.Contains("软删除成功", s);
        }

        // ── CsCat ──

        /// <summary>
        /// cs-check 摘要——JSON 结果解析出错误与警告数（通过）。
        /// </summary>
        [Fact]
        public void CsCheck_JsonResult_ShowsErrWarn()
        {
            string s = ToolSummaryFormatter.Build("cs-check", "{\"csproj\":\"X.csproj\"}", "{\"ok\":true,\"errors\":0,\"warnings\":2}");
            Assert.Equal("C#检查 \"X.csproj\" → OK（共0错 2警）", s);
        }

        /// <summary>
        /// cs-check 摘要——JSON 结果为失败态时展示 FAIL。
        /// </summary>
        [Fact]
        public void CsCheck_JsonFail_ShowsFail()
        {
            string s = ToolSummaryFormatter.Build("cs-check", "{}", "{\"ok\":false,\"errors\":3,\"warnings\":1}");
            Assert.Equal("C#检查 → FAIL（共3错 1警）", s);
        }

        /// <summary>
        /// cs-dead 摘要——文本统计解析出零引用成员数。
        /// </summary>
        [Fact]
        public void CsDead_TextStats()
        {
            string s = ToolSummaryFormatter.Build("cs-dead", "{}", "共 3 个零引用成员");
            Assert.Equal("C#死代码扫描 → OK（\"共3个死代码\"）", s);
        }

        /// <summary>
        /// cs-comment_check 摘要——文本统计解析出缺注释处数。
        /// </summary>
        [Fact]
        public void CsCommentCheck_TextStats()
        {
            string s = ToolSummaryFormatter.Build("cs-comment_check", "{}", "共 2 处缺少 summary 注释");
            Assert.Equal("C#注释检查 → OK（\"共2处缺注释\"）", s);
        }

        /// <summary>
        /// cs-list 摘要——文本统计解析出成员数。
        /// </summary>
        [Fact]
        public void CsList_TextStats()
        {
            string s = ToolSummaryFormatter.Build("cs-list", "{\"class\":\"A\"}", "共 10 成员");
            Assert.Equal("C#列表 \"A\" → OK（\"共10成员\"）", s);
        }

        /// <summary>
        /// cs-patch 摘要——展示类.成员与行号区间。
        /// </summary>
        [Fact]
        public void CsPatch_ShowsClassMemberRange()
        {
            string s = ToolSummaryFormatter.Build("cs-patch", "{\"class\":\"A\",\"method\":\"B\",\"start_line\":\"3\",\"end_line\":\"5\"}", "OK");
            Assert.Equal("C#修改 \"A\".\"B\" L\"3\"~\"5\" → OK", s);
        }

        // ── ConfigCat / MauCat / PsCat ──

        /// <summary>
        /// config-set 摘要——展示配置键与值。
        /// </summary>
        [Fact]
        public void ConfigSet_ShowsKeyValue()
        {
            string s = ToolSummaryFormatter.Build("config-set", "{\"key\":\"ui.chat_font_size\",\"value\":\"16\"}", "OK");
            Assert.Equal("设置配置 \"ui.chat_font_size\"=\"16\" → \"OK\"", s);
        }

        /// <summary>
        /// mau-verify 摘要——展示目标文件与结果。
        /// </summary>
        [Fact]
        public void MauVerify_ShowsFile()
        {
            string s = ToolSummaryFormatter.Build("mau-verify", "{\"file\":\"a.mau\"}", "MAU_VERIFY_OK");
            Assert.Equal("Mau验证 \"a.mau\" → \"MAU_VERIFY_OK\"", s);
        }

        /// <summary>
        /// powershell 摘要——展示命令文本。
        /// </summary>
        [Fact]
        public void Powershell_ShowsCommand()
        {
            string s = ToolSummaryFormatter.Build("powershell", "{\"command\":\"dir CatTemp\"}", "hello");
            Assert.Equal("执行命令 \"dir CatTemp\" → \"hello\"", s);
        }

        /// <summary>
        /// web-search 摘要——展示查询词与多行结果统计。
        /// </summary>
        [Fact]
        public void WebSearch_ShowsQuery()
        {
            string s = ToolSummaryFormatter.Build("web-search", "{\"query\":\"Mau 语法\"}", "结果1\n结果2");
            Assert.Equal("搜索网页 \"Mau 语法\" → \"结果1（2行 · 7 B）\"", s);
        }

        /// <summary>
        /// temp-exec 摘要——展示临时工具 Key。
        /// </summary>
        [Fact]
        public void TempExec_ShowsKey()
        {
            string s = ToolSummaryFormatter.Build("temp-exec", "{\"key\":\"mykey\"}", "done");
            Assert.Equal("临时执行 \"mykey\" → \"done\"", s);
        }

        // ── 内置工具 ──

        /// <summary>
        /// Note 摘要——展示进度与任务目标。
        /// </summary>
        [Fact]
        public void Note_ShowsProgress()
        {
            string s = ToolSummaryFormatter.Build("Note", "{\"action\":\"set\",\"content\":\"任务1\"}", "返回当前第2/5条 已完成1 待完成4 任务目标：测试任务");
            Assert.Equal("任务追踪 → 第\"2/5\"条 完成\"1\" 待做\"4\" \"测试任务\"", s);
        }

        /// <summary>
        /// random 摘要——展示取值区间与结果。
        /// </summary>
        [Fact]
        public void Random_ShowsRange()
        {
            string s = ToolSummaryFormatter.Build("random", "{\"min\":1,\"max\":10}", "7");
            Assert.Equal("随机数 \"[1,10)\" → \"7\"", s);
        }

        /// <summary>
        /// time 摘要——展示时间结果。
        /// </summary>
        [Fact]
        public void Time_ShowsResult()
        {
            string s = ToolSummaryFormatter.Build("time", "{}", "2026-09-07 17:00:00");
            Assert.Equal("获取时间 → \"2026-09-07 17:00:00\"", s);
        }

        /// <summary>
        /// 未知工具兜底——展示参数个数与结果。
        /// </summary>
        [Fact]
        public void UnknownTool_ShowsParamsAndResult()
        {
            string s = ToolSummaryFormatter.Build("weird-tool", "{\"x\":\"y\"}", "zzz");
            Assert.Equal("调用工具 \"weird-tool\" 参数=1个 → \"zzz\"", s);
        }

        /// <summary>
        /// 空参数兜底——null 参数不崩溃，输出空占位。
        /// </summary>
        [Fact]
        public void NullArgs_NoCrash()
        {
            string s = ToolSummaryFormatter.Build("text-read", null, null);
            Assert.Equal("读取文件 \"\" → \"空\"", s);
        }

        /// <summary>
        /// 单行化铁律——结果与参数中的换行不进入摘要。
        /// </summary>
        [Fact]
        public void MultiLine_SingleLined()
        {
            string s = ToolSummaryFormatter.Build("text-read", "{\"path\":\"a\\nb.txt\"}", "x");
            Assert.DoesNotContain("\n", s);
            Assert.DoesNotContain("\r", s);
        }

        /// <summary>
        /// 长路径截断——保留文件名尾部且格式完整。
        /// </summary>
        [Fact]
        public void LongPath_Truncated()
        {
            string longPath = "C:\\very\\long\\directory\\path\\that\\exceeds\\fifty\\five\\characters\\for\\sure\\file.txt";
            string s = ToolSummaryFormatter.Build("text-read", "{\"path\":\"" + longPath.Replace("\\", "\\\\") + "\"}", "x");
            Assert.Contains("file.txt", s);
            Assert.StartsWith("读取文件", s);
        }
        /// <summary>
        /// info 摘要——分类 JSON 块解析出版本 / 猫 / 本地对话端点。
        /// </summary>
        [Fact]
        public void Info_JsonBlock_ShowsVersionCatAndEndpoint()
        {
            string json = "{\n  \"ok\": true,\n  \"tool\": \"info\",\n  \"cat\": \"cat-abc\",\n  \"version\": { \"version\": \"1.03.017\", \"build\": \"2026-09-18 20:00:00\" },\n  \"endpoint\": { \"chat\": \"http://127.0.0.1:8085\", \"panel\": \"http://127.0.0.1:8080\" }\n}";
            string s = ToolSummaryFormatter.Build("info", "{}", json);
            Assert.Equal("环境信息 → v1.03.017 · 猫 cat-abc · http://127.0.0.1:8085", s);
        }
        /// <summary>
        /// info 摘要——非分类 JSON 退回原文首行，失败可见（不静默）。
        /// </summary>
        [Fact]
        public void Info_NonJson_FallsBackToFirstLine()
        {
            string s = ToolSummaryFormatter.Build("info", "{}", "CH4 | 环境信息不可用");
            Assert.Equal("环境信息 → \"CH4 | 环境信息不可用\"", s);
        }
        /// <summary>
        /// 元数据头剥离——首行单行 JSON 头 + 正文：头被剥离，正文进入摘要。
        /// </summary>
        [Fact]
        public void MetaHead_SingleLineHeader_Stripped()
        {
            string result = "{\"ok\":true,\"tool\":\"text-read\"}\nCH4 | 正文首行\n第二行";
            string s = ToolSummaryFormatter.Build("text-read", "{\"path\":\"a.md\"}", result);
            Assert.Contains("CH4 | 正文首行", s);
            Assert.DoesNotContain("tool", s);
        }
        /// <summary>
        /// 元数据头剥离——多行 JSON 正文（首行仅 "{"）不是头：正文整体保留，不剥不告警（A75 回归）。
        /// </summary>
        [Fact]
        public void MetaHead_MultiLineJsonBody_NotStripped()
        {
            string result = "{\n  \"ok\": true\n}";
            string s = ToolSummaryFormatter.Build("text-read", "{\"path\":\"a.md\"}", result);
            Assert.Contains("{（3行", s);
        }
    }
}
