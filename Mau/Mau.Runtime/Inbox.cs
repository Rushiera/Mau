using System;
using System.Collections.Concurrent;

namespace Mau.Runtime
{
    /// <summary>
    /// 跨线程双缓冲——后台线程只写队列，主 Tick 排空
    /// </summary>
    /// <typeparam name="T">投递项类型</typeparam>
    public sealed class Inbox<T>
    {
        /// <summary>
        /// 线程安全队列——后台 Enqueue，主线程 TryDequeue
        /// </summary>
        private readonly ConcurrentQueue<T> _queue = new ConcurrentQueue<T>();

        /// <summary>
        /// 入队——任意线程调用
        /// </summary>
        /// <param name="item">投递项</param>
        public void Enqueue(T item)
        {
            _queue.Enqueue(item);
        }

        /// <summary>
        /// 排空——主线程调用，处理全部待办
        /// </summary>
        /// <param name="handler">逐项处理器</param>
        /// <returns>处理条数</returns>
        public int Drain(Action<T> handler)
        {
            int count = 0;
#pragma warning disable CS8600
            T item = default!;
            while (_queue.TryDequeue(out item))
            {
                handler(item);
                count = count + 1;
            }
#pragma warning restore CS8600
            return count;
        }

        /// <summary>
        /// 待办数量
        /// </summary>
        public int Count
        {
            get { return _queue.Count; }
        }
    }
}
