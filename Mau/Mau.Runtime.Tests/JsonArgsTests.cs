using Xunit;

namespace Mau.Runtime.Tests
{
    /// <summary>
    /// 工具参数面入口测试（A138）——校验三段（未知键 / 必填 / 枚举）+ 取值口径 + 解析失败路径。
    /// </summary>
    public sealed class JsonArgsTests
    {
        /// <summary>
        /// 未知键拦截——支持面以外的键一律 ERR|BAD_ARGS（错误文本与积木样板逐字一致）
        /// </summary>
        [Fact]
        public void Validate_UnknownKey_Rejected()
        {
            string err = JsonArgs.Validate("{\"path\":\"a\",\"other\":1}", "path", "path", "", "");
            Assert.Equal("ERR|BAD_ARGS|未知参数: other（支持 path）", err);
        }

        /// <summary>
        /// catId 保留键放行——宿主注入参数不在支持面内也不报错
        /// </summary>
        [Fact]
        public void Validate_CatId_Allowed()
        {
            Assert.Equal("", JsonArgs.Validate("{\"path\":\"a\",\"catId\":\"x\"}", "path", "path", "", ""));
        }

        /// <summary>
        /// 必填校验——缺失 / 空字符串均视同缺值
        /// </summary>
        [Fact]
        public void Validate_RequiredMissing_Rejected()
        {
            Assert.Equal("ERR|BAD_ARGS|缺参数 path（必填：path）", JsonArgs.Validate("{}", "path", "path", "", ""));
            Assert.Equal("ERR|BAD_ARGS|缺参数 path（必填：path）", JsonArgs.Validate("{\"path\":\"\"}", "path", "path", "", ""));
        }

        /// <summary>
        /// 枚举校验——合法值与空值放行，非法值报错
        /// </summary>
        [Fact]
        public void Validate_Enum()
        {
            Assert.Equal("", JsonArgs.Validate("{\"action\":\"list\"}", "action", "action", "action", "list|new|select|close"));
            Assert.Equal("", JsonArgs.Validate("{}", "action", "", "action", "list|new"));
            Assert.Equal("ERR|BAD_ARGS|action 非法值: xx（list|new）", JsonArgs.Validate("{\"action\":\"xx\"}", "action", "", "action", "list|new"));
        }

        /// <summary>
        /// 取值口径——字符串直取 / 非字符串取 JSON 原文 / 缺失空串 / 解析失败或空输入 §PARSE_FAIL§
        /// </summary>
        [Fact]
        public void Get_Shapes()
        {
            Assert.Equal("a", JsonArgs.Get("{\"k\":\"a\"}", "k"));
            Assert.Equal("3", JsonArgs.Get("{\"k\":3}", "k"));
            Assert.Equal("", JsonArgs.Get("{\"k\":\"a\"}", "missing"));
            Assert.Equal("§PARSE_FAIL§", JsonArgs.Get("{not json", "k"));
            Assert.Equal("§PARSE_FAIL§", JsonArgs.Get("", "k"));
        }
    }
}
