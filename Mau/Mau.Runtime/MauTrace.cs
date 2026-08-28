using System;

namespace Mau.Runtime
{
    /// <summary>
    /// trace 事件类型——可检测注入点
    /// </summary>
    public enum MauTraceKind
    {
        /// <summary>
        /// 变迁触发
        /// </summary>
        TransitionFired,

        /// <summary>
        /// 命题注册
        /// </summary>
        PropositionSet,

        /// <summary>
        /// 配额获取
        /// </summary>
        QuotaAcquired,

        /// <summary>
        /// 时限耗尽
        /// </summary>
        TimeoutExpired
    }

    /// <summary>
    /// trace 事件——关键时序的可检测记录
    /// </summary>
    public readonly struct MauTrace
    {
        /// <summary>
        /// 事件类型
        /// </summary>
        public readonly MauTraceKind Kind;

        /// <summary>
        /// 变迁/命题/资源名
        /// </summary>
        public readonly string Name;

        /// <summary>
        /// 事件发生帧号
        /// </summary>
        public readonly long Frame;

        /// <summary>
        /// 构造 trace 事件
        /// </summary>
        /// <param name="kind">事件类型</param>
        /// <param name="name">事件名</param>
        /// <param name="frame">帧号</param>
        public MauTrace(MauTraceKind kind, string name, long frame)
        {
            Kind = kind;
            Name = name;
            Frame = frame;
        }
    }

    /// <summary>
    /// trace 发布器——生成物持有，宿主订阅
    /// </summary>
    public sealed class MauTraceHub
    {
        /// <summary>
        /// 当前帧号——发布时携带
        /// </summary>
        private long _frame;

        /// <summary>
        /// trace 事件流——宿主订阅
        /// </summary>
        public event Action<MauTrace>? TracePublished;

        /// <summary>
        /// 发布 trace
        /// </summary>
        /// <param name="kind">事件类型</param>
        /// <param name="name">事件名</param>
        public void Publish(MauTraceKind kind, string name)
        {
            Action<MauTrace>? handler = TracePublished;
            if (handler != null)
            {
                handler(new MauTrace(kind, name, _frame));
            }
        }

        /// <summary>
        /// 设置帧号——宿主每帧调用
        /// </summary>
        /// <param name="frame">当前帧号</param>
        public void SetFrame(long frame)
        {
            _frame = frame;
        }
    }
}
