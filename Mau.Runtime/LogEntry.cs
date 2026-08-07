namespace Mau.Runtime
{
    /// <summary>
    /// 日志条目——总账式结构化日志单元（log.* 共享）
    /// </summary>
    public struct LogEntry
    {
        /// <summary>
        /// 时间戳
        /// </summary>
        public string Time;

        /// <summary>
        /// 模块名
        /// </summary>
        public string Module;

        /// <summary>
        /// 级别——0=INFO 2=WARN 3=ERROR
        /// </summary>
        public int Level;

        /// <summary>
        /// 消息
        /// </summary>
        public string Message;
    }
}
