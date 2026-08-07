namespace Mau.Runtime
{
    /// <summary>
    /// 通用文本辅助——BRIK 内联文本处理共享（程序级：空值兜底/级别文本）
    /// </summary>
    public static class BrickText
    {
        /// <summary>
        /// 把可空文本规范为空字符串
        /// </summary>
        /// <param name="value">输入</param>
        /// <returns>非空文本</returns>
        public static string SafeText(string? value)
        {
            if (value == null)
            {
                return "";
            }
            return value;
        }

        /// <summary>
        /// 会话 Key 空值兜底——空 Key 归 "" 会话
        /// </summary>
        /// <param name="value">Key</param>
        /// <returns>非空 Key</returns>
        public static string SafeKey(string? value)
        {
            if (value == null)
            {
                return "";
            }
            return value;
        }

        /// <summary>
        /// 日志级别文本——0=INFO 2=WARN 3=ERROR
        /// </summary>
        /// <param name="level">级别数字</param>
        /// <returns>文本</returns>
        public static string LevelText(int level)
        {
            if (level >= 3)
            {
                return "ERROR";
            }
            if (level == 2)
            {
                return "WARN";
            }
            return "INFO";
        }
    }
}
