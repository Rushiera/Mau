namespace Mau.Translator
{
    /// <summary>
    /// Mau 诊断——构筑期错误，格式：文件名.mau:行号: 错误码: 消息
    /// </summary>
    public sealed class MauDiagnostic
    {
        /// <summary>
        /// 错误码——E001-E011 验证项，E101+ 语法项
        /// </summary>
        public string Code;

        /// <summary>
        /// 行号——1-based
        /// </summary>
        public int Line;

        /// <summary>
        /// 错误消息
        /// </summary>
        public string Message;

        /// <summary>
        /// 构造诊断
        /// </summary>
        /// <param name="code">错误码</param>
        /// <param name="line">行号</param>
        /// <param name="message">错误消息</param>
        public MauDiagnostic(string code, int line, string message)
        {
            Code = code;
            Line = line;
            Message = message;
        }
    }
}
