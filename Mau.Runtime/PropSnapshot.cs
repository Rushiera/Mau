namespace Mau.Runtime
{
    /// <summary>
    /// 命题快照——RuntimeStatus 的子项
    /// </summary>
    public readonly struct PropSnapshot
    {
        /// <summary>
        /// 命题名——P_ 前缀
        /// </summary>
        public readonly string Name;

        /// <summary>
        /// 命题类型——Condition/Signal/Fact
        /// </summary>
        public readonly string Kind;

        /// <summary>
        /// 当前真值
        /// </summary>
        public readonly bool Value;

        /// <summary>
        /// 构造命题快照
        /// </summary>
        /// <param name="name">命题名</param>
        /// <param name="kind">类型</param>
        /// <param name="value">当前值</param>
        public PropSnapshot(string name, string kind, bool value)
        {
            Name = name;
            Kind = kind;
            Value = value;
        }
    }
}
