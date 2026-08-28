using System;

namespace Mau.Runtime
{
    /// <summary>
    /// Mau 调试记录——翻译器在关键位置自动注入的运行时日志
    /// </summary>
    public readonly struct MauDebug
    {
        /// <summary>
        /// 事件发生帧号
        /// </summary>
        public readonly long Frame;

        /// <summary>
        /// 变迁名——T_ 前缀
        /// </summary>
        public readonly string TransitionName;

        /// <summary>
        /// 阶段——Fired/Ok/Error/Timeout
        /// </summary>
        public readonly string Phase;

        /// <summary>
        /// 调试消息——用户在 Mau 语句中声明的文本，含插值展开
        /// </summary>
        public readonly string Message;

        /// <summary>
        /// 构造调试记录
        /// </summary>
        /// <param name="frame">帧号</param>
        /// <param name="transitionName">变迁名</param>
        /// <param name="phase">阶段</param>
        /// <param name="message">消息</param>
        public MauDebug(long frame, string transitionName, string phase, string message)
        {
            Frame = frame;
            TransitionName = transitionName;
            Phase = phase;
            Message = message;
        }
    }
}
