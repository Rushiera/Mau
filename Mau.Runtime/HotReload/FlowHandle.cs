using System;
using System.IO;
using System.Reflection;

namespace Mau.Runtime
{
    /// <summary>
    /// 生成物加载句柄——持有 ALC、实例、WeakReference
    /// </summary>
    public sealed class FlowHandle : IDisposable
    {
        private FlowALC? _alc;
        private IObservableFlow? _flow;
        private WeakReference _alcRef;
        private bool _disposed;
        private bool _isFaulted;
        private string _faultReason;

        /// <summary>
        /// Flow 是否因异常而隔离——Tick 不会再被调用
        /// </summary>
        public bool IsFaulted
        {
            get { return _isFaulted; }
        }

        /// <summary>
        /// 故障原因——IsFaulted 为 true 时有值
        /// </summary>
        public string FaultReason
        {
            get { return _faultReason; }
        }

        /// <summary>
        /// 标记为故障——后续 Tick 将被跳过
        /// </summary>
        /// <param name="reason">故障原因</param>
        public void MarkFaulted(string reason)
        {
            _isFaulted = true;
            _faultReason = reason;
        }

        /// <summary>
        /// 生成物实例
        /// </summary>
        public IObservableFlow Flow
        {
            get
            {
                if (_disposed)
                {
                    throw new ObjectDisposedException("FlowHandle");
                }
                return _flow!;
            }
        }

        private FlowHandle(FlowALC alc, IObservableFlow flow)
        {
            _alc = alc;
            _flow = flow;
            _alcRef = new WeakReference(alc);
            _disposed = false;
            _isFaulted = false;
            _faultReason = "";
        }

        /// <summary>
        /// 从 DLL 路径加载生成物
        /// </summary>
        /// <param name="dllPath">FL_xxx.dll 的完整路径</param>
        /// <returns>加载句柄</returns>
        /// <exception cref="FileNotFoundException">DLL 不存在</exception>
        /// <exception cref="InvalidOperationException">DLL 中未找到 IObservableFlow 实现</exception>
        public static FlowHandle Load(string dllPath)
        {
            if (!File.Exists(dllPath))
            {
                throw new FileNotFoundException("生成物 DLL 不存在: " + dllPath);
            }

            string alcName = "Flow_" + Path.GetFileNameWithoutExtension(dllPath);
            FlowALC alc = new FlowALC(alcName);

            Assembly asm;
            try
            {
                asm = alc.LoadFromAssemblyPath(dllPath);
            }
            catch (BadImageFormatException)
            {
                throw new BadImageFormatException("DLL 不是有效的 .NET 程序集: " + dllPath);
            }

            Type[] types = asm.GetExportedTypes();
            Type? flowType = null;
            for (int i = 0; i < types.Length; i = i + 1)
            {
                if (typeof(IObservableFlow).IsAssignableFrom(types[i]) && !types[i].IsAbstract)
                {
                    flowType = types[i];
                    break;
                }
            }

            if (flowType == null)
            {
                alc.Unload();
                throw new InvalidOperationException("DLL 中未找到 IObservableFlow 的实现类: " + dllPath);
            }

            object? instance = Activator.CreateInstance(flowType);
            if (instance == null)
            {
                alc.Unload();
                throw new InvalidOperationException("无法实例化生成物类型: " + flowType.FullName);
            }

            IObservableFlow flow = (IObservableFlow)instance;
            return new FlowHandle(alc, flow);
        }

        /// <summary>
        /// 尝试卸载 ALC 并确认 GC 回收
        /// </summary>
        /// <param name="gcAttempts">GC 尝试次数，默认 3</param>
        /// <returns>true=回收成功 / false=泄漏</returns>
        public bool TryUnload(int gcAttempts = 3)
        {
            if (_disposed)
            {
                return true;
            }

            _flow = null;
            FlowALC? alc = _alc;
            _alc = null;

            if (alc != null)
            {
                alc.Unload();
            }
            alc = null;

            for (int i = 0; i < gcAttempts; i = i + 1)
            {
                GC.Collect(2, GCCollectionMode.Forced, true, true);
                GC.WaitForPendingFinalizers();
                GC.Collect(2, GCCollectionMode.Forced, true, true);

                if (!_alcRef.IsAlive)
                {
                    _disposed = true;
                    return true;
                }
            }

            _disposed = true;
            return false;
        }

        /// <summary>
        /// 释放——卸载 ALC
        /// </summary>
        public void Dispose()
        {
            if (!_disposed)
            {
                TryUnload();
            }
        }
    }
}
