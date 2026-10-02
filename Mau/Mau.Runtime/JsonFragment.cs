namespace Mau.Runtime
{
    /// <summary>
    /// JSON 片段包装——已是合法 JSON 的文本（对象 / 数组 / 已序列化载荷）。
    /// 用途：JsonUtil.Object / Array 的值位要嵌入「片段」而非「字符串」时显式标注——
    /// Scalar 识别本类型后直出（不再转义）。契约 = 构造方保证文本是合法 JSON（本类型即该承诺的载体）。
    /// </summary>
    public sealed class JsonFragment
    {
        /// <summary>片段文本（合法 JSON）</summary>
        public string Json { get; }

        /// <summary>
        /// 构造——文本须为合法 JSON（不做二次校验：本类型本身就是「我知道这是片段」的显式承诺）。
        /// </summary>
        /// <param name="json">JSON 片段（对象 / 数组 / 字面量；null → "null"）</param>
        public JsonFragment(string json)
        {
            if (json == null)
            {
                Json = "null";
                return;
            }
            Json = json;
        }
    }
}
