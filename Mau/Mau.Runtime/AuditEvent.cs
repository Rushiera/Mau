using System;

namespace Mau.Runtime
{
    /// <summary>
    /// 审计事件——时序留痕的原子单元（MD 格式化的数据源）。
    /// 标题行：## E{seq} | F{全局帧号} | {时间} | {来源} | {类别}
    /// </summary>
    public sealed class AuditEvent
    {
        /// <summary>
        /// 全局事件序号——会话内单调递增（E 序列，lock 内分配）
        /// </summary>
        public long Seq { get; }

        /// <summary>
        /// 全局帧号——宿主帧坐标（FlowRunner 驱动，跨机制可比较）
        /// </summary>
        public long Frame { get; }

        /// <summary>
        /// 事件时间——HH:mm:ss
        /// </summary>
        public string Time { get; }

        /// <summary>
        /// 来源——CommandBus / OA / FlowRunner / ConfigStore 等
        /// </summary>
        public string Source { get; }

        /// <summary>
        /// 类别——cmd.set / oa.post / trace.* / cfg.load / log.* 等
        /// </summary>
        public string Category { get; }

        /// <summary>
        /// 属性键值数组——事件详情（密钥永不进事件，只记标记）
        /// </summary>
        public AuditProp[] Props { get; }

        /// <summary>
        /// 是否落盘——trace/完整载荷为 false（默认 true，L0-L1 类事件）
        /// </summary>
        public bool Persistable { get; }

        /// <summary>
        /// 构造审计事件——由 AuditStore.Record 统一分配 Seq
        /// </summary>
        /// <param name="seq">全局事件序号</param>
        /// <param name="frame">全局帧号</param>
        /// <param name="time">事件时间 HH:mm:ss</param>
        /// <param name="source">来源</param>
        /// <param name="category">类别</param>
        /// <param name="props">属性键值数组</param>
        /// <param name="persistable">是否落盘</param>
        public AuditEvent(long seq, long frame, string time, string source, string category, AuditProp[] props, bool persistable)
        {
            Seq = seq;
            Frame = frame;
            Time = time;
            Source = source;
            Category = category;
            Props = props;
            Persistable = persistable;
        }
    }

    /// <summary>
    /// 审计属性——事件键值对（MD 属性行 - key: value）
    /// </summary>
    public sealed class AuditProp
    {
        /// <summary>
        /// 属性键
        /// </summary>
        public string Key { get; }

        /// <summary>
        /// 属性值——已摘要/标记化（埋点侧负责，密钥只记 configured/missing 标记）
        /// </summary>
        public string Value { get; }

        /// <summary>
        /// 构造审计属性
        /// </summary>
        /// <param name="key">属性键</param>
        /// <param name="value">属性值</param>
        public AuditProp(string key, string value)
        {
            Key = key;
            Value = value;
        }
    }
}
