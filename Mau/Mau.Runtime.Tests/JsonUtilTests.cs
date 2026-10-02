using System.Text.Json;
using Xunit;

namespace Mau.Runtime.Tests
{
    /// <summary>
    /// JSON 构造入口测试（A137）——Str 值面转义 / Scalar 类型分派 / Object / Array 产出可解析。
    /// </summary>
    public sealed class JsonUtilTests
    {
        /// <summary>
        /// Object——字符串转义 / 数值直出 / 布尔直出 / null；产出为合法 JSON
        /// </summary>
        [Fact]
        public void Object_ScalarDispatch_Parseable()
        {
            string json = JsonUtil.Object(("name", "a\"b\\c"), ("n", 3), ("flag", true), ("none", null));
            using (JsonDocument doc = JsonDocument.Parse(json))
            {
                JsonElement root = doc.RootElement;
                Assert.Equal("a\"b\\c", root.GetProperty("name").GetString());
                Assert.Equal(3, root.GetProperty("n").GetInt32());
                Assert.True(root.GetProperty("flag").GetBoolean());
                Assert.Equal(JsonValueKind.Null, root.GetProperty("none").ValueKind);
            }
        }

        /// <summary>
        /// 控制字符经 Str 转义后仍构成合法 JSON（JsonEscape 实装面——U+0001 不得裸出）
        /// </summary>
        [Fact]
        public void Str_ControlChar_ValidJson()
        {
            string json = JsonUtil.Object(("v", "a\u0001b"));
            using (JsonDocument doc = JsonDocument.Parse(json))
            {
                Assert.Equal("a\u0001b", doc.RootElement.GetProperty("v").GetString());
            }
        }

        /// <summary>
        /// Array——元素按类型分派（字符串转义 / 数值直出 / 布尔直出）
        /// </summary>
        [Fact]
        public void Array_Scalars_Parseable()
        {
            string json = JsonUtil.Array("x\"y", 1, false);
            using (JsonDocument doc = JsonDocument.Parse(json))
            {
                Assert.Equal("x\"y", doc.RootElement[0].GetString());
                Assert.Equal(1, doc.RootElement[1].GetInt32());
                Assert.False(doc.RootElement[2].GetBoolean());
            }
        }

        /// <summary>
        /// Raw——片段值直出（不二次转义）：Object 产出的片段可嵌套为值
        /// </summary>
        [Fact]
        public void Raw_FragmentNotEscaped()
        {
            string inner = JsonUtil.Object(("a", 1));
            string outer = JsonUtil.Object(("inner", JsonUtil.Raw(inner)), ("s", "x\"y"));
            using (JsonDocument doc = JsonDocument.Parse(outer))
            {
                Assert.Equal(1, doc.RootElement.GetProperty("inner").GetProperty("a").GetInt32());
                Assert.Equal("x\"y", doc.RootElement.GetProperty("s").GetString());
            }
        }

        /// <summary>
        /// Parse——合法文本返回文档（error 空）；非法文本返回 null 并带回失败原因（出声 L2）
        /// </summary>
        [Fact]
        public void Parse_NullOnInvalid()
        {
            string error;
            using (JsonDocument? ok = JsonUtil.Parse("{\"a\":1}", "test", out error))
            {
                Assert.NotNull(ok);
                Assert.Equal("", error);
            }
            string error2;
            using (JsonDocument? bad = JsonUtil.Parse("{oops", "test", out error2))
            {
                Assert.Null(bad);
                Assert.True(error2.Length > 0);
            }
        }
    }
}
