using System;

namespace Mau.Runtime
{
    /// <summary>
    /// Command 域不含输入正文的必要观察摘要——透明度暴露
    /// </summary>
    public struct CommandSnapshot
    {
        /// <summary>
        /// 注册或待消费集合变化时递增的域版本
        /// </summary>
        public long Version;

        /// <summary>
        /// 当前注册 Owner 数量
        /// </summary>
        public int RegisteredOwnerCount;

        /// <summary>
        /// 当前全局唯一完整 Key 数量
        /// </summary>
        public int RegisteredKeyCount;

        /// <summary>
        /// 等待下一次宿主 Tick 冻结的 Key 数量
        /// </summary>
        public int PendingKeyCount;

        /// <summary>
        /// 当前宿主 Tick 可读取的冻结 Key 数量
        /// </summary>
        public int FrozenKeyCount;

        /// <summary>
        /// 冻结与待冻结输入去重后的 Key 总数
        /// </summary>
        public int TotalInputKeyCount;

        /// <summary>
        /// 当前是否接受新的外部输入
        /// </summary>
        public bool IsAcceptingInput;

        /// <summary>
        /// 生命周期关闭后被拒绝的输入累计数量
        /// </summary>
        public long RejectedInputCount;

        /// <summary>
        /// Ordinal 排序后的注册 Key 独立数组
        /// </summary>
        public string[] RegisteredKeys;

        /// <summary>
        /// 创建字段完整的空摘要
        /// </summary>
        /// <returns>空摘要</returns>
        public static CommandSnapshot Empty()
        {
            CommandSnapshot snapshot = new CommandSnapshot();
            snapshot.RegisteredKeys = Array.Empty<string>();
            return snapshot;
        }
    }
}
