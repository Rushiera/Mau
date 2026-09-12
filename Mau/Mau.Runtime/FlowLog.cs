using System;

namespace Mau.Runtime
{
    /// <summary>
    /// 环形日志缓冲——生成物持有，200 条上限，自动覆盖最旧记录
    /// </summary>
    public sealed class FlowLog
    {
        /// <summary>
        /// 环形缓冲——定长数组，自动覆盖最旧记录
        /// </summary>
        private readonly MauDebug[] _buffer;

        /// <summary>
        /// 写入头——环形游标
        /// </summary>
        private int _head;

        /// <summary>
        /// 当前有效条数
        /// </summary>
        private int _count;
        /// <summary>环形缓冲容量——200 条上限（R1-P3-06：魔法数字集中）</summary>
        private const int BufferCapacity = 200;

        /// <summary>
        /// 当前日志条数
        /// </summary>
        public int Count
        {
            get { return _count; }
        }

        /// <summary>
        /// 构造环形日志缓冲——200 条上限
        /// </summary>
        public FlowLog()
        {
            _buffer = new MauDebug[BufferCapacity];
            _head = 0;
            _count = 0;
        }

        /// <summary>
        /// 追加一条日志——环形覆盖最旧记录
        /// </summary>
        /// <param name="entry">调试记录</param>
        public void Add(MauDebug entry)
        {
            _buffer[_head] = entry;
            _head = (_head + 1) % BufferCapacity;
            if (_count < BufferCapacity)
            {
                _count = _count + 1;
            }
        }

        /// <summary>
        /// 获取全量日志——时间顺序（旧→新）
        /// </summary>
        /// <returns>日志数组</returns>
        public MauDebug[] GetAll()
        {
            MauDebug[] result = new MauDebug[_count];
            if (_count == 0)
            {
                return result;
            }
            if (_count < BufferCapacity)
            {
                for (int i = 0; i < _count; i = i + 1)
                {
                    result[i] = _buffer[i];
                }
            }
            else
            {
                for (int i = 0; i < BufferCapacity; i = i + 1)
                {
                    result[i] = _buffer[(_head + i) % BufferCapacity];
                }
            }
            return result;
        }
    }
}
