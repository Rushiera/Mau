namespace Mau.Runtime
{
    /// <summary>
    /// 工具规格——OpenAI 兼容 function 定义（tools 数组元素）。
    /// ParametersJson 为 JSON Schema 原样透传（省略原则：未设则请求体不带该字段）。
    /// </summary>
    public sealed class ToolSpec
    {
        /// <summary>
        /// 工具名——wire 声明名下划线（点号会 400，判例）
        /// </summary>
        public string Name;

        /// <summary>
        /// 语义化描述
        /// </summary>
        public string Description;

        /// <summary>
        /// 参数 JSON Schema（原样透传）；空串 = 无参数工具
        /// </summary>
        public string ParametersJson;

        /// <summary>
        /// 建立工具规格
        /// </summary>
        /// <param name="name">工具名（下划线命名）</param>
        /// <param name="description">语义化描述</param>
        /// <param name="parametersJson">参数 JSON Schema</param>
        public ToolSpec(string name, string description, string parametersJson)
        {
            Name = name;
            Description = description;
            ParametersJson = parametersJson;
        }
    }
}
