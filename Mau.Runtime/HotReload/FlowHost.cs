using System;
using System.Collections.Generic;

namespace Mau.Runtime
{
    /// <summary>
    /// 生成物宿主——管理活跃 FlowHandle，支持热替换
    /// </summary>
    public sealed class FlowHost : IDisposable
    {
        private readonly List<FlowHandle> _handles;
        private readonly object _lock;

        /// <summary>
        /// 当前活跃的生成物句柄列表——只读快照，迭代期间安全
        /// </summary>
        public IReadOnlyList<FlowHandle> Handles
        {
            get
            {
                lock (_lock)
                {
                    return _handles.ToArray();
                }
            }
        }

        /// <summary>
        /// 构造空宿主
        /// </summary>
        public FlowHost()
        {
            _handles = new List<FlowHandle>();
            _lock = new object();
        }

        /// <summary>
        /// 加载 DLL 并注册到活跃列表
        /// </summary>
        /// <param name="dllPath">FL_xxx.dll 完整路径</param>
        /// <returns>加载句柄</returns>
        public FlowHandle Load(string dllPath)
        {
            FlowHandle handle = FlowHandle.Load(dllPath);
            lock (_lock)
            {
                _handles.Add(handle);
            }
            return handle;
        }

        /// <summary>
        /// 热替换——新 DLL 验证通过后原子替换第一个匹配的旧 Handle
        /// </summary>
        /// <param name="dllPath">新 DLL 路径</param>
        /// <returns>新 Handle，替换失败返回 null</returns>
        public FlowHandle? Replace(string dllPath)
        {
            FlowHandle newHandle;
            try
            {
                newHandle = FlowHandle.Load(dllPath);
                // 最小验证：GetStatus 不抛异常
                newHandle.Flow.GetStatus();
            }
            catch
            {
                return null;
            }

            lock (_lock)
            {
                // 卸载第一个旧 Handle
                if (_handles.Count > 0)
                {
                    FlowHandle old = _handles[0];
                    _handles.RemoveAt(0);
                    old.TryUnload(3);
                }
                _handles.Insert(0, newHandle);
            }
            return newHandle;
        }

        /// <summary>
        /// 卸载所有活跃 Handle
        /// </summary>
        public void UnloadAll()
        {
            lock (_lock)
            {
                for (int i = _handles.Count - 1; i >= 0; i = i - 1)
                {
                    _handles[i].TryUnload(3);
                }
                _handles.Clear();
            }
        }

        /// <summary>
        /// 对所有活跃 Flow 执行一次 Tick
        /// </summary>
        public void TickAll()
{
            FlowHandle[] snapshot;
            lock (_lock)
            {
                snapshot = _handles.ToArray();
            }
            for (int i = 0; i < snapshot.Length; i = i + 1)
            {
                FlowHandle h = snapshot[i];
                if (h.IsFaulted)
                {
                    continue;
                }
                try
                {
                    h.Flow.Tick();
                }
                catch (Exception ex)
                {
                    h.MarkFaulted(ex.ToString());
                }
            }
        }
        /// <summary>
        /// 活跃 Flow 数量
        /// </summary>
        public int Count
        {
            get
            {
                lock (_lock)
                {
                    return _handles.Count;
                }
            }
        }

        /// <summary>
        /// 释放——卸载全部
        /// </summary>
        public void Dispose()
        {
            UnloadAll();
        }
    }
}
