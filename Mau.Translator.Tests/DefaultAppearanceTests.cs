using System;
using Mau.Translator;
using Xunit;

namespace Mau.Translator.Tests
{
    /// <summary>
    /// v3 外观层测试——TokenId 双向映射（设计稿 design-mau-v3 §四）
    /// </summary>
    public class DefaultAppearanceTests
    {
        /// <summary>
        /// 外观实例——每次新建（无状态，无需共享夹具）
        /// </summary>
        /// <returns>默认外观</returns>
        private static DefaultAppearance Create()
        {
            return new DefaultAppearance();
        }

        /// <summary>
        /// 全部 14 个符号——Glyph → TryMap 往返一致
        /// </summary>
        [Fact]
        public void Glyph_RoundTrip_AllSymbols()
        {
            DefaultAppearance appearance = Create();
            uint[] all = new uint[]
            {
                Mau.Contracts.TokenIds.Section,
                Mau.Contracts.TokenIds.Declare,
                Mau.Contracts.TokenIds.Eq,
                Mau.Contracts.TokenIds.Colon,
                Mau.Contracts.TokenIds.In,
                Mau.Contracts.TokenIds.Sample,
                Mau.Contracts.TokenIds.Arrow,
                Mau.Contracts.TokenIds.Branch,
                Mau.Contracts.TokenIds.And,
                Mau.Contracts.TokenIds.SetOpen,
                Mau.Contracts.TokenIds.SetClose,
                Mau.Contracts.TokenIds.Sep,
                Mau.Contracts.TokenIds.ParamOpen,
                Mau.Contracts.TokenIds.ParamClose,
            };
            for (int i = 0; i < all.Length; i++)
            {
                string glyph = appearance.Glyph(all[i]);
                Assert.NotEqual("", glyph);
                uint mapped;
                bool ok = appearance.TryMap(glyph, out mapped);
                Assert.True(ok, "TryMap 失败: tokenId=" + all[i] + " glyph=" + glyph);
                Assert.Equal(all[i], mapped);
            }
        }

        /// <summary>
        /// 多字符字形——:= / &lt;- / @ 映射
        /// </summary>
        [Fact]
        public void TryMap_MultiGlyphs()
        {
            DefaultAppearance appearance = Create();
            uint tokenId;
            bool ok1 = appearance.TryMap(":=", out tokenId);
            Assert.True(ok1);
            Assert.Equal(Mau.Contracts.TokenIds.Declare, tokenId);
            uint tokenId2;
            // 显式 ASCII 码点构造 "<-"（60=小于号 45=连字符）——绕过一切传输转义
            string probe2 = ((char)60).ToString() + ((char)45).ToString();
            bool ok2 = appearance.TryMap(probe2, out tokenId2);
            Assert.True(ok2);
            Assert.Equal(Mau.Contracts.TokenIds.In, tokenId2);
            uint tokenId3;
            bool ok3 = appearance.TryMap(">", out tokenId3);
            Assert.True(ok3);
            Assert.Equal(Mau.Contracts.TokenIds.Capture, tokenId3);
        }

        /// <summary>
        /// 非法字符拒绝——不属于外观白名单
        /// </summary>
        [Fact]
        public void TryMap_RejectUnknown()
        {
            DefaultAppearance appearance = Create();
            uint tokenId;
            Assert.False(appearance.TryMap("τ", out tokenId));
            Assert.False(appearance.TryMap("ω", out tokenId));
            Assert.False(appearance.TryMap("∧", out tokenId));
            Assert.False(appearance.TryMap("?", out tokenId));
            Assert.False(appearance.TryMap(";", out tokenId));
            Assert.Equal(0u, tokenId);
        }

        /// <summary>
        /// 值 token 无字形——Name/Num/Str/Word/Eof 返回空串
        /// </summary>
        [Fact]
        public void Glyph_ValueTokens_Empty()
        {
            DefaultAppearance appearance = Create();
            Assert.Equal("", appearance.Glyph(Mau.Contracts.TokenIds.Name));
            Assert.Equal("", appearance.Glyph(Mau.Contracts.TokenIds.Num));
            Assert.Equal("", appearance.Glyph(Mau.Contracts.TokenIds.Str));
            Assert.Equal("", appearance.Glyph(Mau.Contracts.TokenIds.Word));
            Assert.Equal("", appearance.Glyph(Mau.Contracts.TokenIds.Eof));
        }

        /// <summary>
        /// 外观名——诊断携带 "v3-default"
        /// </summary>
        [Fact]
        public void AppearanceName_IsV3Default()
        {
            DefaultAppearance appearance = Create();
            Assert.Equal("v3-default", appearance.AppearanceName);
        }

        /// <summary>
        /// Command 传感器符号——⇚ 映射 CmdIn
        /// </summary>
        [Fact]
        public void TryMap_CmdIn()
        {
            DefaultAppearance appearance = Create();
            uint tokenId;
            Assert.True(appearance.TryMap("⇚", out tokenId));
            Assert.Equal(Mau.Contracts.TokenIds.CmdIn, tokenId);
            Assert.Equal("⇚", appearance.Glyph(Mau.Contracts.TokenIds.CmdIn));
        }
    }
}
