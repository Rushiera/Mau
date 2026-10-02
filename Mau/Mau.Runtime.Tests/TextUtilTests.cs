using Xunit;

namespace Mau.Runtime.Tests
{
    /// <summary>
    /// 文本工具统一实现测试——A133 实体解码（命名实体 / 数字实体 / 逃生口 / 未知名 / 快路径）。
    /// </summary>
    public sealed class TextUtilTests
    {
        /// <summary>
        /// 命名实体——lt / gt / amp / quot / apos 逐项还原
        /// </summary>
        [Fact]
        public void DecodeEntities_NamedEntities()
        {
            Assert.Equal("<PropertyGroup>", TextUtil.DecodeEntities("&lt;PropertyGroup&gt;"));
            Assert.Equal("a&b", TextUtil.DecodeEntities("a&amp;b"));
            Assert.Equal("\"x\"", TextUtil.DecodeEntities("&quot;x&quot;"));
            Assert.Equal("'y'", TextUtil.DecodeEntities("&apos;y&apos;"));
        }

        /// <summary>
        /// 数字实体——十进制与十六进制同解；零值 / 越界码点原样保留
        /// </summary>
        [Fact]
        public void DecodeEntities_NumericEntities()
        {
            Assert.Equal("a'b", TextUtil.DecodeEntities("a&#39;b"));
            Assert.Equal("a'b", TextUtil.DecodeEntities("a&#x27;b"));
            Assert.Equal("&#0;", TextUtil.DecodeEntities("&#0;"));
        }

        /// <summary>
        /// 逃生口——双写形态一次解码得单写实体文本（只解一层，不递归）
        /// </summary>
        [Fact]
        public void DecodeEntities_EscapeHatch_NotRecursive()
        {
            Assert.Equal("&lt;", TextUtil.DecodeEntities("&amp;lt;"));
        }

        /// <summary>
        /// 未知名 / 缺分号 / 超长体——原样保留（不猜不改）
        /// </summary>
        [Fact]
        public void DecodeEntities_UnknownKeptAsIs()
        {
            Assert.Equal("&nbsp;x", TextUtil.DecodeEntities("&nbsp;x"));
            Assert.Equal("a&lt b", TextUtil.DecodeEntities("a&lt b"));
            Assert.Equal("&abcdefghijkl;", TextUtil.DecodeEntities("&abcdefghijkl;"));
        }

        /// <summary>
        /// 无实体快路径——原样返回（同一引用，零拷贝）
        /// </summary>
        [Fact]
        public void DecodeEntities_FastPath_SameReference()
        {
            string plain = "plain text";
            Assert.Same(plain, TextUtil.DecodeEntities(plain));
        }

        /// <summary>
        /// JSON 转义——标准八种转义 + 控制字符兜底（U+0008 / U+000C / U+0001 不得裸出——非法 JSON）
        /// </summary>
        [Fact]
        public void JsonEscape_StandardAndControlChars()
        {
            Assert.Equal("a\\\"b", TextUtil.JsonEscape("a\"b"));
            Assert.Equal("a\\\\b", TextUtil.JsonEscape("a\\b"));
            Assert.Equal("a\\nb", TextUtil.JsonEscape("a\nb"));
            Assert.Equal("a\\rb", TextUtil.JsonEscape("a\rb"));
            Assert.Equal("a\\tb", TextUtil.JsonEscape("a\tb"));
            Assert.Equal("a\\bb", TextUtil.JsonEscape("a\bb"));
            Assert.Equal("a\\fb", TextUtil.JsonEscape("a\fb"));
            Assert.Equal("\\u0001", TextUtil.JsonEscape("\u0001"));
        }

        /// <summary>
        /// JSON 还原——与 JsonEscape 往返一致；未识别序列原样保留（不吞反斜杠）；斜杠转义可还原
        /// </summary>
        [Fact]
        public void JsonUnescape_RoundTripAndUnknownKept()
        {
            string source = "a\"b\\c\nd\te\bf\u0001中文";
            Assert.Equal(source, TextUtil.JsonUnescape(TextUtil.JsonEscape(source)));
            Assert.Equal("a\\qb", TextUtil.JsonUnescape("a\\qb"));
            Assert.Equal("/x", TextUtil.JsonUnescape("\\/x"));
        }
    }
}
