using System;
using System.Threading;

namespace Mau.Runtime
{
    /// <summary>
    /// 线程归属守卫——绑定创建线程，调度类入口全部经过硬检查
    /// </summary>
    public sealed class ThreadGuard
    {
        /// <summary>
        /// 创建时的线程 ID
        /// </summary>
        private readonly int _ownerThreadId;

        /// <summary>
        /// 当前调用是否来自宿主主线程
        /// </summary>
        public bool IsMainThread
        {
            get { return Thread.CurrentThread.ManagedThreadId == _ownerThreadId; }
        }

        /// <summary>
        /// 绑定当前线程为宿主主线程
        /// </summary>
        public ThreadGuard()
        {
            _ownerThreadId = Thread.CurrentThread.ManagedThreadId;
        }

        /// <summary>
        /// 要求当前调用位于宿主主线程
        /// </summary>
        /// <param name="operation">操作名称</param>
        public void AssertMainThread(string operation)
        {
            if (!IsMainThread)
            {
                throw new InvalidOperationException(operation + " 只能由宿主主线程调用。请使用 Inbox 提交后台结果。");
            }
        }
    }
}
