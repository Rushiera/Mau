namespace Mau.Runtime
{
    /// <summary>
    /// 由 Markdown 解析器产生的独立表现片段——纯数据，与渲染框架无关（契约类型）
    /// </summary>
    public struct MarkdownPart
    {
        /// <summary>
        /// 片段正文
        /// </summary>
        public string Text;

        /// <summary>
        /// 相对基础字号的增量
        /// </summary>
        public int FontDelta;

        /// <summary>
        /// 是否使用粗体
        /// </summary>
        public bool Bold;

        /// <summary>
        /// 是否使用斜体
        /// </summary>
        public bool Italic;

        /// <summary>
        /// 是否使用等宽字体
        /// </summary>
        public bool Mono;

        /// <summary>
        /// 片段的有限语义颜色
        /// </summary>
        public MarkdownColor ColorType;
    }

    /// <summary>
    /// Markdown 表现片段的有限语义颜色
    /// </summary>
    public enum MarkdownColor
    {
        /// <summary>
        /// 普通正文
        /// </summary>
        Text = 0,

        /// <summary>
        /// 一级标题
        /// </summary>
        H1 = 1,

        /// <summary>
        /// 二级标题
        /// </summary>
        H2 = 2,

        /// <summary>
        /// 行内代码
        /// </summary>
        Code = 3,

        /// <summary>
        /// 代码块
        /// </summary>
        CodeBlock = 4,

        /// <summary>
        /// 引用文本
        /// </summary>
        Quote = 5,

        /// <summary>
        /// 表格文本
        /// </summary>
        Table = 6,

        /// <summary>
        /// 字符画
        /// </summary>
        AsciiArt = 7
    }
}
