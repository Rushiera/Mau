using System;
using System.IO;
using Xunit;

namespace Mau.Runtime.Tests
{
    /// <summary>
    /// 文本算法面测试（Mau.Runtime/Algorithms/TextAlgorithms——A227 归位首批：匹配 / 差异 / 替换 / 含子串）。
    /// </summary>
    public sealed class TextAlgorithmsTests
    {
        /// <summary>
        /// 命中——exact / ignore_case / regex 三态
        /// </summary>
        [Fact]
        public void FindOccurrences_ThreeModes()
        {
            Assert.Equal(new int[] { 1, 3, 5 }, TextAlgorithms.FindOccurrences("A行B行C行", "行", TextMatchMode.Exact));
            Assert.Empty(TextAlgorithms.FindOccurrences("A行B行C行", "行行", TextMatchMode.Exact));
            Assert.Equal(new int[] { 0 }, TextAlgorithms.FindOccurrences("Hello world", "hello", TextMatchMode.IgnoreCase));
            Assert.Empty(TextAlgorithms.FindOccurrences("Hello world", "hello", TextMatchMode.Exact));
            Assert.Equal(new int[] { 0, 3, 6 }, TextAlgorithms.FindOccurrences("v1 v2 v3", "v\\d", TextMatchMode.Regex));
        }

        /// <summary>
        /// 命中跨度——索引 + 长度（regex 取真实匹配长度）
        /// </summary>
        [Fact]
        public void FindMatches_ReportsSpans()
        {
            TextMatchSpan[] spans = TextAlgorithms.FindMatches("v1 v2 v3", "v\\d", TextMatchMode.Regex);
            Assert.Equal(3, spans.Length);
            Assert.Equal(0, spans[0].Index);
            Assert.Equal(2, spans[0].Length);
            Assert.Equal(6, spans[2].Index);
            TextMatchSpan[] lit = TextAlgorithms.FindMatches("A行B行", "行", TextMatchMode.Exact);
            Assert.Equal(2, lit.Length);
            Assert.Equal(1, lit[0].Length);
        }

        /// <summary>
        /// 全部替换——exact / ignore_case / regex 捕获组
        /// </summary>
        [Fact]
        public void ReplaceAll_ThreeModes()
        {
            Assert.Equal("AXBXC", TextAlgorithms.ReplaceAll("A行B行C", "行", "X", TextMatchMode.Exact));
            Assert.Equal("HELLO world", TextAlgorithms.ReplaceAll("Hello world", "hello", "HELLO", TextMatchMode.IgnoreCase));
            Assert.Equal("V1 V2 V3", TextAlgorithms.ReplaceAll("v1 v2 v3", "v(\\d)", "V$1", TextMatchMode.Regex));
        }

        /// <summary>
        /// 差异点——最长公共前缀对齐（未命中锚点的分叉字节位）
        /// </summary>
        [Fact]
        public void LocateBestMatch_ReportsDiffPoint()
        {
            TextDiffPoint diff = TextAlgorithms.LocateBestMatch("第一行\n目标 这里\n第三行", "目标 那里");
            Assert.True(diff.ByteIndex >= 0);
            Assert.True(diff.CommonLength >= 2);
            TextDiffPoint empty = TextAlgorithms.LocateBestMatch("", "x");
            Assert.Equal(0, empty.ByteIndex);
        }

        /// <summary>
        /// 含任一子串——中性 tokens 判定
        /// </summary>
        [Fact]
        public void ContainsAny_DetectsTokens()
        {
            string[] tokens = new string[] { "&lt;", "&amp;" };
            Assert.True(TextAlgorithms.ContainsAny("存的是 &lt;x&gt;", tokens));
            Assert.False(TextAlgorithms.ContainsAny("普通正文", tokens));
            Assert.False(TextAlgorithms.ContainsAny("", tokens));
            Assert.False(TextAlgorithms.ContainsAny("x", new string[0]));
        }

        /// <summary>
        /// 写入风格描述——编码 + BOM / 换行（A148；原 ReplaceAuto 用例迁出保留）
        /// </summary>
        [Fact]
        public void WriteAuto_StyleDescriptions()
        {
            string baseDir = Path.Combine(Path.GetTempPath(), "mau_writeauto_" + Guid.NewGuid().ToString("N"));
            string rw = Path.Combine(baseDir, "rw");
            Directory.CreateDirectory(rw);
            try
            {
                WorkspaceConfig.RootEntry[] entries = new WorkspaceConfig.RootEntry[]
                {
                    new WorkspaceConfig.RootEntry() { Id = "rw", Path = rw, Writable = true }
                };
                FileSystemService fs = new FileSystemService(entries, Path.Combine(rw, "recycle"));
                string p = Path.Combine(rw, "doc.md");
                Assert.Equal("UTF-8 BOM / LF", fs.WriteTextAuto(p, "正文"));
                Assert.Equal("UTF-8 BOM / CRLF", fs.WriteTextAuto(Path.Combine(rw, "doc.mau"), "正文"));
                Assert.Equal("UTF-8 / LF", fs.WriteTextAuto(Path.Combine(rw, "note.txt"), "正文"));
                Assert.Equal("UTF-8 / LF", fs.AppendTextAuto(Path.Combine(rw, "note.txt"), "续行"));
            }
            finally
            {
                if (Directory.Exists(baseDir))
                {
                    Directory.Delete(baseDir, true);
                }
            }
        }
    }
}
