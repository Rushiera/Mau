using System;

namespace Mau.Runtime
{
    /// <summary>
    /// 确定性时钟——帧号来源，测试可注入
    /// </summary>
    public interface IClock
    {
        /// <summary>
        /// 当前帧号
        /// </summary>
        long Frame { get; }
    }

    /// <summary>
    /// 帧时钟——宿主每帧调用 Advance 推进
    /// </summary>
    public sealed class FrameClock : IClock
    {
        /// <summary>
        /// 当前帧号
        /// </summary>
        private long _frame;

        /// <summary>
        /// 当前帧号
        /// </summary>
        public long Frame
        {
            get { return _frame; }
        }

        /// <summary>
        /// 推进一帧
        /// </summary>
        public void Advance()
        {
            _frame = _frame + 1;
        }
    }

    /// <summary>
    /// 固定时钟——测试注入，帧号可任意设置
    /// </summary>
    public sealed class FixedClock : IClock
    {
        /// <summary>
        /// 当前帧号
        /// </summary>
        private long _frame;

        /// <summary>
        /// 当前帧号
        /// </summary>
        public long Frame
        {
            get { return _frame; }
        }

        /// <summary>
        /// 设置帧号
        /// </summary>
        /// <param name="frame">目标帧号</param>
        public void SetFrame(long frame)
        {
            _frame = frame;
        }
    }
}
