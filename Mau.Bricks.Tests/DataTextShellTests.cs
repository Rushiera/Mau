// ═══════════════════════════════════════════════
// 测试: Mau.Bricks.Data + Mau.Bricks.Text + Mau.Bricks.Shell
// 引用: Mau.Bricks.Tests → Mau.Bricks.Data / Mau.Bricks.Text / Mau.Bricks.Shell
// 原理: CH3 测试随迁改写——直接调用静态积木方法验证
// 常用: 积木库移植正确性回归
// ═══════════════════════════════════════════════
using System;
using System.Collections.Generic;
using Xunit;

namespace Mau.Bricks.Tests
{
    /// <summary>
    /// 数据积木测试——Snapshot 分节编码/解码 + 值盒存储
    /// </summary>
    public sealed class DataBrickTests
    {
        /// <summary>
        /// Snapshot 编码/解码往返
        /// </summary>
        [Fact]
        public void SnapshotEncodeDecodeRoundTrip()
        {
            Dictionary<string, List<string>> sections = new Dictionary<string, List<string>>();
            sections["Core"] = new List<string> { "Frame=1", "State=Running" };
            sections["OA"] = new List<string> { "Open=2", "Work=1" };

            string encoded = DataBrick.SnapshotEncode(sections);
            Dictionary<string, List<string>> decoded = DataBrick.SnapshotDecode(encoded);

            Assert.Equal(2, decoded.Count);
            Assert.Equal("Frame=1", decoded["Core"][0]);
            Assert.Equal("Work=1", decoded["OA"][1]);
        }

        /// <summary>
        /// 空字典编码返回空串，空串解码返回空字典
        /// </summary>
        [Fact]
        public void SnapshotEmptyRoundTrip()
        {
            Assert.Equal("", DataBrick.SnapshotEncode(null));
            Assert.Empty(DataBrick.SnapshotDecode(""));
            Assert.Empty(DataBrick.SnapshotDecode(null));
        }

        /// <summary>
        /// 标题形态行被转义保护
        /// </summary>
        [Fact]
        public void SnapshotEscapesHeaderLikeLines()
        {
            Dictionary<string, List<string>> sections = new Dictionary<string, List<string>>();
            sections["S"] = new List<string> { "[LooksLikeSection]", "plain" };

            string encoded = DataBrick.SnapshotEncode(sections);
            Dictionary<string, List<string>> decoded = DataBrick.SnapshotDecode(encoded);

            Assert.Equal("[LooksLikeSection]", decoded["S"][0]);
        }

        /// <summary>
        /// 重复节名解码抛 FormatException
        /// </summary>
        [Fact]
        public void SnapshotRejectsDuplicateSections()
        {
            Assert.Throws<FormatException>(delegate
            {
                DataBrick.SnapshotDecode("[A]\nx\n[A]\ny");
            });
        }

        /// <summary>
        /// 值盒单值写入/读取 + 作用域隔离
        /// </summary>
        [Fact]
        public void BoxSetGetIsolatedByScope()
        {
            BoxBrick.Clear("test-a");
            BoxBrick.Clear("test-b");

            Assert.True(BoxBrick.Set("test-a", "count", 5));
            Assert.True(BoxBrick.Get("test-a", "count", 0, out int valueA));
            Assert.True(BoxBrick.Get("test-b", "count", 0, out int valueB));

            Assert.Equal(5, valueA);
            Assert.Equal(0, valueB);
        }

        /// <summary>
        /// 值盒数据包 JSON 写入/读取
        /// </summary>
        [Fact]
        public void BoxDicRoundTrip()
        {
            BoxBrick.Clear("test-dic");

            Assert.True(BoxBrick.SetDic("test-dic", "tools", "{\"io\":14,\"aux\":2}"));
            Assert.True(BoxBrick.GetDic("test-dic", "tools", out string json));
            Assert.Contains("\"io\":14", json);

            Assert.True(BoxBrick.GetDic("test-dic", "missing", out string missing));
            Assert.Equal("{}", missing);
        }
    }

    /// <summary>
    /// 文本积木测试——Markdown 解析
    /// </summary>
    public sealed class TextBrickTests
    {
        /// <summary>
        /// 标题/列表/代码块/表格解析
        /// </summary>
        [Fact]
        public void MdParseHandlesCommonBlocks()
        {
            string md = "# 标题\n\n- 项目A\n- 项目B\n\n```\ncode\n```\n\n| A | B |\n|---|---|\n| 1 | 2 |\n";

            Assert.True(TextBrick.MdParse(md, out List<MarkdownPart> parts));
            Assert.NotEmpty(parts);

            bool foundH1 = false;
            bool foundCode = false;
            bool foundTable = false;
            for (int i = 0; i < parts.Count; i = i + 1)
            {
                if (parts[i].ColorType == MarkdownColor.H1)
                {
                    foundH1 = true;
                }
                if (parts[i].ColorType == MarkdownColor.CodeBlock)
                {
                    foundCode = true;
                }
                if (parts[i].ColorType == MarkdownColor.Table)
                {
                    foundTable = true;
                }
            }
            Assert.True(foundH1);
            Assert.True(foundCode);
            Assert.True(foundTable);
        }

        /// <summary>
        /// 空文本返回空列表
        /// </summary>
        [Fact]
        public void MdParseEmptyReturnsEmpty()
        {
            Assert.True(TextBrick.MdParse(null, out List<MarkdownPart> parts));
            Assert.Empty(parts);
        }
    }

    /// <summary>
    /// Shell 积木测试——只验证配置与参数校验，不实际执行命令
    /// </summary>
    public sealed class ShellBrickTests
    {
        /// <summary>
        /// 非法命令返回错误输出
        /// </summary>
        [Fact]
        public void ShellExecRejectsInvalidCommand()
        {
            bool ok = ShellBrick.Exec("", 20, out string output);
            Assert.False(ok);
            Assert.StartsWith("ERR|INVALID_COMMAND", output);
        }
    }
}
