using System;
using System.Text;

namespace CH4
{
    /// <summary>
    /// 视图层报错中文注释（A69）——真实前文保持原文（错误码 + 英文原文是稳定标识符，不改写），
    /// 仅在「真实前文 → 视图块」投影时对报错串追加「（中文注释）」。
    /// 判据：报错串消息段不含中文且含英文字母才进映射；四级优先 = 消息句式 → 上游错误类型 → .NET 异常类名 → 宿主错误码；
    /// 未命中保持原样（不硬猜）。消费方：SessionViewStore.OnToolResult / ChatSession 工具卡与错误气泡出口。
    /// </summary>
    internal static class ErrorNote
    {
        // ═══════════════════════════════════════════
        // 映射表（键值交替——按序优先，先精确后宽泛）
        // ═══════════════════════════════════════════

        /// <summary>
        /// 消息句式表——英文关键词 → 中文注释（大小写不敏感；顺序即优先级）。
        /// </summary>
        private static readonly string[] MessagePatterns = new string[]
        {
            "being used by another process", "文件被其他进程占用",
            "cannot access the file", "文件无法访问——被占用或权限不足",
            "access to the path", "路径访问被拒绝——权限不足",
            "is denied", "访问被拒绝——权限不足",
            "could not find a part of the path", "路径的一部分不存在",
            "could not find file", "找不到文件",
            "the system cannot find the file", "系统找不到指定文件",
            "the system cannot find the path", "系统找不到指定路径",
            "the directory is not empty", "目录非空",
            "already exists", "目标已存在",
            "is a directory", "目标是一个目录",
            "not of a legal form", "路径格式不合法",
            "path too long", "路径过长",
            "invalid start of a value", "JSON 格式不合法",
            "is an invalid", "格式不合法",
            "has timed out", "操作超时",
            "timed out", "操作超时",
            "actively refused", "连接被拒绝——目标服务未启动",
            "no such host", "域名解析失败",
            "name or service not known", "域名解析失败",
            "underlying connection was closed", "连接被意外关闭",
            "was canceled", "请求已取消",
            "value cannot be null", "参数不能为空",
            "outside the bounds", "索引越界",
            "would be truncated", "数据超长被截断",
            "not enough storage", "磁盘空间不足",
            "not enough space", "磁盘空间不足",
            "no space left", "磁盘空间不足"
        };

        /// <summary>
        /// 错误码表——上游 API 错误类型 / .NET 异常类名 / 宿主错误码 → 中文注释（大小写不敏感；顺序即优先级）。
        /// </summary>
        private static readonly string[] CodePatterns = new string[]
        {
            // 上游 API 错误类型
            "GoUsageLimitError", "模型用量已达上限——额度耗尽，需等待重置或启用余额",
            "insufficient_quota", "账户余额不足",
            "rate_limit_exceeded", "请求过于频繁——已限速",
            "RateLimitError", "请求过于频繁——已限速",
            "invalid_api_key", "API key 无效",
            "AuthenticationError", "鉴权失败——API key 无效或未授权",
            "context_length_exceeded", "上下文长度超出模型上限",
            "invalid_request_error", "请求参数不合法",
            "model_not_found", "模型不存在或无权访问",
            "not_found_error", "资源不存在",
            "overloaded_error", "上游服务过载——可重试",
            "server_error", "上游服务异常——可重试",
            "api_error", "上游服务异常——可重试",
            "APITimeoutError", "上游响应超时",
            "timeout_error", "上游响应超时",
            // 宿主传输码
            "TRANSPORT", "网络传输失败",
            "STREAM_CLOSED", "响应流被提前关闭",
            "LLM_TIMEOUT", "模型响应超时",
            // HTTP 状态码
            "HTTP_401", "未授权——API key 无效",
            "HTTP_402", "余额不足",
            "HTTP_403", "无权访问",
            "HTTP_404", "接口或资源不存在",
            "HTTP_408", "请求超时",
            "HTTP_429", "请求过于频繁——已限速",
            "HTTP_5", "上游服务异常——可重试",
            // .NET 异常类名
            "IOException", "文件读写失败",
            "UnauthorizedAccessException", "拒绝访问——权限不足",
            "FileNotFoundException", "文件不存在",
            "DirectoryNotFoundException", "目录不存在",
            "PathTooLongException", "路径过长",
            "JsonException", "JSON 格式错误",
            "TimeoutException", "操作超时",
            "TaskCanceledException", "任务已取消",
            "OperationCanceledException", "操作已取消",
            "HttpRequestException", "网络请求失败",
            "SocketException", "网络连接失败",
            "Win32Exception", "系统调用失败",
            "ArgumentNullException", "参数为空",
            "ArgumentOutOfRangeException", "参数超出范围",
            "ArgumentException", "参数不合法",
            "InvalidOperationException", "当前状态不允许此操作",
            "PlatformNotSupportedException", "当前平台不支持",
            "NotSupportedException", "不支持的操作",
            "NotImplementedException", "功能未实现",
            "FormatException", "格式不合法",
            "OverflowException", "数值溢出",
            "KeyNotFoundException", "键不存在",
            "NullReferenceException", "空引用——内部缺陷",
            "OutOfMemoryException", "内存不足",
            "InvalidCastException", "类型转换失败",
            "BadImageFormatException", "程序集格式不正确",
            "FileLoadException", "程序集加载失败",
            "TypeInitializationException", "类型初始化失败",
            "MissingMethodException", "成员缺失——版本不匹配",
            "MissingFieldException", "字段缺失——版本不匹配",
            "UriFormatException", "地址格式不合法",
            "DecoderFallbackException", "编码解码失败",
            "EncoderFallbackException", "编码转换失败",
            "AggregateException", "多个内部异常"
        };

        // ═══════════════════════════════════════════
        // 主入口
        // ═══════════════════════════════════════════

        /// <summary>
        /// 对文本中的报错串追加中文注释——无报错串原样返回；换行保真（CRLF 原样保留）。
        /// </summary>
        /// <param name="text">视图文本（工具结果 / 错误文本）</param>
        /// <returns>注释后的文本（未命中映射的行原样）</returns>
        public static string Apply(string text)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf("ERR|", StringComparison.Ordinal) < 0)
            {
                return text;
            }
            StringBuilder sb = new StringBuilder();
            string[] lines = text.Split('\n');
            for (int i = 0; i < lines.Length; i = i + 1)
            {
                if (i > 0)
                {
                    sb.Append('\n');
                }
                string line = lines[i];
                bool cr = line.EndsWith("\r", StringComparison.Ordinal);
                if (cr)
                {
                    line = line.Substring(0, line.Length - 1);
                }
                sb.Append(AnnotateLine(line));
                if (cr)
                {
                    sb.Append('\r');
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// 单行注释——行内含 ERR| 报错串且消息段为英文时，按映射表在行尾追加「（中文注释）」；否则原样返回。
        /// </summary>
        /// <param name="line">单行文本（不含换行符）</param>
        /// <returns>注释后的行</returns>
        private static string AnnotateLine(string line)
        {
            int at = line.IndexOf("ERR|", StringComparison.Ordinal);
            if (at < 0)
            {
                return line;
            }
            string rest = line.Substring(at + 4);
            string message = rest;
            int bar = rest.IndexOf('|');
            if (bar >= 0)
            {
                message = rest.Substring(bar + 1);
            }
            // 判据——消息段不含中文且含英文字母才进映射（中文消息不加冗余注释）
            if (message.Length == 0 || HasCjk(message) || !HasAsciiLetter(message))
            {
                return line;
            }
            // 四级优先——消息句式 → 上游错误类型 / .NET 异常类名 / 宿主错误码
            string note = MatchPattern(MessagePatterns, message);
            if (note.Length == 0)
            {
                note = MatchPattern(CodePatterns, rest);
            }
            if (note.Length == 0)
            {
                return line;
            }
            return line + "（" + note + "）";
        }

        // ═══════════════════════════════════════════
        // 辅助
        // ═══════════════════════════════════════════

        /// <summary>
        /// 表匹配——键值交替表中查首个命中的键（大小写不敏感；顺序即优先级）。
        /// </summary>
        /// <param name="table">键值交替表（[0]=键 [1]=值 …）</param>
        /// <param name="text">被查文本</param>
        /// <returns>命中的中文注释（未命中返回空串）</returns>
        private static string MatchPattern(string[] table, string text)
        {
            if (text.Length == 0)
            {
                return "";
            }
            for (int i = 0; i + 1 < table.Length; i = i + 2)
            {
                if (text.IndexOf(table[i], StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return table[i + 1];
                }
            }
            return "";
        }

        /// <summary>
        /// 是否含中日韩字符或全角标点（判据用——中文消息不加注释）。
        /// </summary>
        /// <param name="text">被查文本</param>
        /// <returns>true=含</returns>
        private static bool HasCjk(string text)
        {
            for (int i = 0; i < text.Length; i = i + 1)
            {
                char c = text[i];
                if (c >= 0x4E00 && c <= 0x9FFF)
                {
                    return true;
                }
                if (c >= 0x3000 && c <= 0x303F)
                {
                    return true;
                }
                if (c >= 0xFF00 && c <= 0xFFEF)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 是否含 ASCII 字母（判据用——纯符号/数字消息不进映射）。
        /// </summary>
        /// <param name="text">被查文本</param>
        /// <returns>true=含</returns>
        private static bool HasAsciiLetter(string text)
        {
            for (int i = 0; i < text.Length; i = i + 1)
            {
                char c = text[i];
                if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z'))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
