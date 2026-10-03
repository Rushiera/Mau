using CH4;
using Xunit;

namespace CatHome4.Core.Tests
{
    /// <summary>
    /// ErrorNote 测试——A69 视图层报错中文注释。
    /// 覆盖：无报错原样 / 消息句式映射 / 上游错误类型 / .NET 异常类名兜底 / 中文消息跳过 / 未命中不硬猜 / 多行 / CRLF 保真 / 复合段 / 空消息。
    /// </summary>
    public class ErrorNoteTests
    {
        /// <summary>
        /// 无报错串——原样返回。
        /// </summary>
        [Fact]
        public void Apply_NoErrorMarker_Unchanged()
        {
            string text = "正常结果\n第二行";
            Assert.Equal(text, ErrorNote.Apply(text));
        }

        /// <summary>
        /// 空文本与 null——原样返回。
        /// </summary>
        [Fact]
        public void Apply_EmptyText_Unchanged()
        {
            Assert.Equal("", ErrorNote.Apply(""));
            Assert.Null(ErrorNote.Apply(null));
        }

        /// <summary>
        /// .NET 异常句式——文件被其他进程占用（句式优先于异常类名）。
        /// </summary>
        [Fact]
        public void Apply_FileLockedPattern_Annotated()
        {
            string line = "ERR|IOException|The process cannot access the file 'D:\\a.txt' because it is being used by another process.";
            Assert.Equal(line + "（文件被其他进程占用）", ErrorNote.Apply(line));
        }

        /// <summary>
        /// .NET 异常句式——路径访问被拒绝。
        /// </summary>
        [Fact]
        public void Apply_AccessDeniedPattern_Annotated()
        {
            string line = "ERR|IOException|Access to the path 'D:\\x' is denied.";
            Assert.Equal(line + "（路径访问被拒绝——权限不足）", ErrorNote.Apply(line));
        }

        /// <summary>
        /// 上游错误类型——模型用量超限。
        /// </summary>
        [Fact]
        public void Apply_UpstreamUsageLimit_Annotated()
        {
            string line = "ERR|GoUsageLimitError|Weekly usage limit reached. Resets in 10hr 5min.";
            Assert.Equal(line + "（模型用量已达上限——额度耗尽，需等待重置或启用余额）", ErrorNote.Apply(line));
        }

        /// <summary>
        /// .NET 异常类名兜底——消息未命中句式时按类型名映射。
        /// </summary>
        [Fact]
        public void Apply_ExceptionTypeFallback_Annotated()
        {
            string line = "ERR|UnauthorizedAccessException|Opq rst xyz.";
            Assert.Equal(line + "（拒绝访问——权限不足）", ErrorNote.Apply(line));
        }

        /// <summary>
        /// 复合段——宿主传输码 + 异常类名 + 英文消息。
        /// </summary>
        [Fact]
        public void Apply_CompoundTransport_Annotated()
        {
            string line = "ERR|TRANSPORT|HttpRequestException|The SSL connection could not be established.";
            Assert.Equal(line + "（网络传输失败）", ErrorNote.Apply(line));
        }

        /// <summary>
        /// 重试前缀行——ERR| 不在行首同样注释。
        /// </summary>
        [Fact]
        public void Apply_RetryPrefixed_Annotated()
        {
            string line = "RETRY|1/3|ERR|TRANSPORT|HttpRequestException|The SSL connection could not be established.";
            Assert.Equal(line + "（网络传输失败）", ErrorNote.Apply(line));
        }

        /// <summary>
        /// 中文消息——不加冗余注释。
        /// </summary>
        [Fact]
        public void Apply_ChineseMessage_Unchanged()
        {
            string line = "ERR|OA_TIMEOUT|工单超时（时限内无回执）: text-grep——超时 ≠ 终止：只失去回执，宿主不中断已认领的执行";
            Assert.Equal(line, ErrorNote.Apply(line));
        }

        /// <summary>
        /// 未命中映射的英文消息——保持原样（不硬猜）。
        /// </summary>
        [Fact]
        public void Apply_UnmappedEnglish_Unchanged()
        {
            string line = "ERR|FOO_BAR|quux zork blorp";
            Assert.Equal(line, ErrorNote.Apply(line));
        }

        /// <summary>
        /// 纯数字消息——无英文字母，不进映射。
        /// </summary>
        [Fact]
        public void Apply_NoAsciiLetter_Unchanged()
        {
            string line = "ERR|E_1|12345";
            Assert.Equal(line, ErrorNote.Apply(line));
        }

        /// <summary>
        /// 无消息段（三段串缺尾）——原样返回。
        /// </summary>
        [Fact]
        public void Apply_EmptyMessage_Unchanged()
        {
            string line = "ERR|IOException|";
            Assert.Equal(line, ErrorNote.Apply(line));
        }

        /// <summary>
        /// 多行——只有报错行被注释，其余行原样。
        /// </summary>
        [Fact]
        public void Apply_MultiLine_OnlyErrorLineAnnotated()
        {
            string text = "a.txt\nERR|HTTP_429|rate_limit_exceeded|Too Many Requests\nb.txt";
            string expected = "a.txt\nERR|HTTP_429|rate_limit_exceeded|Too Many Requests（请求过于频繁——已限速）\nb.txt";
            Assert.Equal(expected, ErrorNote.Apply(text));
        }

        /// <summary>
        /// CRLF 换行保真——注释行与非注释行的行尾均不改变。
        /// </summary>
        [Fact]
        public void Apply_CrlfPreserved()
        {
            string text = "ERR|HTTP_500|server_error|Internal Server Error\r\nok";
            string expected = "ERR|HTTP_500|server_error|Internal Server Error（上游服务异常——可重试）\r\nok";
            Assert.Equal(expected, ErrorNote.Apply(text));
        }
    }
}
