using System.Text.Json;
using CH4;
using Xunit;

namespace CatHome4.Core.Tests
{
    /// <summary>
    /// 工具参数实体解码测试（A133）——锚点面解码 / 内容面豁免 / 无实体快路径 / 非法 JSON 透传。
    /// 规格：宿主参数面统一解码（落点 ParseToolCalls）；豁免名单见 ChatSession.EntityDecodeExemptArgs。
    /// </summary>
    public sealed class ToolArgEntityDecodeTests
    {
        /// <summary>
        /// 锚点面（标识符类参数）解码——等价性断言取解析后的值（不绑 JSON 字符转义形态）
        /// </summary>
        [Fact]
        public void AnchorArg_Decoded()
        {
            string outJson = ChatSession.DecodeToolArgEntities("text-read_between", "{\"path\":\"a.md\",\"str1\":\"&lt;PropertyGroup&gt;\"}");
            using (JsonDocument doc = JsonDocument.Parse(outJson))
            {
                Assert.Equal("<PropertyGroup>", doc.RootElement.GetProperty("str1").GetString());
                Assert.Equal("a.md", doc.RootElement.GetProperty("path").GetString());
            }
        }

        /// <summary>
        /// 内容面参数豁免——content 承载「写什么存什么」的正文语义，原样保留（同一引用）
        /// </summary>
        [Fact]
        public void ContentArg_Exempt()
        {
            string inJson = "{\"path\":\"a.md\",\"content\":\"&lt;b&gt;\"}";
            Assert.Same(inJson, ChatSession.DecodeToolArgEntities("text-write", inJson));
        }

        /// <summary>
        /// 无实体快路径——参数 JSON 不含 &amp; 时原样返回（零解析开销）
        /// </summary>
        [Fact]
        public void NoEntity_FastPath_SameReference()
        {
            string inJson = "{\"path\":\"a.md\",\"str1\":\"abc\"}";
            Assert.Same(inJson, ChatSession.DecodeToolArgEntities("text-read", inJson));
        }

        /// <summary>
        /// 非法参数 JSON——原样透传（下游按 BAD_ARGS 出声，本入口不抢报）
        /// </summary>
        [Fact]
        public void InvalidJson_PassedThrough()
        {
            string inJson = "{not json &lt;";
            Assert.Same(inJson, ChatSession.DecodeToolArgEntities("text-read", inJson));
        }
    }
}
