using System;
using System.Collections.Generic;
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
/// 来源 DLL 路径——热重载按 dll 粒度匹配（D7 单 dll = 单 Flow 组）
/// </summary>
private string _sourceDll;
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
/// <summary>
/// 来源 DLL 路径——热重载按 dll 粒度匹配（D7 单 dll = 单 Flow 组）
/// </summary>
public string SourceDll
{
    get
    {
        return _sourceDll;
    }
}
        private FlowHandle(FlowALC alc, IObservableFlow flow, string sourceDll)
        {
            _alc = alc;
            _flow = flow;
            _sourceDll = sourceDll;
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
                asm = alc.LoadShared(dllPath);
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
            return new FlowHandle(alc, flow, Path.GetFullPath(dllPath));
        }

        /// <summary>
        /// 全量加载 DLL（组模式）——dll 内全部 IObservableFlow 实现各一个独立实例（每实例独立 ALC）
        /// </summary>
        /// <param name="dllPath">FL_xxx.dll 完整路径</param>
        /// <returns>句柄数组（与 dll 内实现一一对应）</returns>
        /// <exception cref="FileNotFoundException">DLL 不存在</exception>
        /// <exception cref="InvalidOperationException">DLL 内未找到 IObservableFlow 实现</exception>
        public static FlowHandle[] LoadAll(string dllPath)
        {
            if (!File.Exists(dllPath))
            {
                throw new FileNotFoundException("口袋 DLL 不存在: " + dllPath);
            }

            // [段1] 探测类型清单——独立探针 ALC，卸载后正式加载
            string baseName = Path.GetFileNameWithoutExtension(dllPath);
            FlowALC probe = new FlowALC("FlowProbe_" + baseName + "_" + Guid.NewGuid().ToString("N"));
            List<Type> flowTypes = new List<Type>();
            try
            {
                Assembly asm = probe.LoadShared(dllPath);
                Type[] types = asm.GetExportedTypes();
                for (int i = 0; i < types.Length; i = i + 1)
                {
                    if (typeof(IObservableFlow).IsAssignableFrom(types[i]) && !types[i].IsAbstract)
                    {
                        flowTypes.Add(types[i]);
                    }
                }
            }
            finally
            {
                probe.Unload();
            }
            if (flowTypes.Count == 0)
            {
                throw new InvalidOperationException("DLL 内未找到 IObservableFlow 实现: " + dllPath);
            }

            // [段2] 每类型独立 ALC 实例化
            FlowHandle[] handles = new FlowHandle[flowTypes.Count];
            for (int i = 0; i < flowTypes.Count; i = i + 1)
            {
                FlowALC alc = new FlowALC("Flow_" + baseName + "_" + i.ToString());
                Assembly asm = alc.LoadShared(dllPath);
                string? fullName = flowTypes[i].FullName;
                if (fullName == null)
                {
                    alc.Unload();
                    continue;
                }
                Type? type = asm.GetType(fullName);
                if (type == null)
                {
                    alc.Unload();
                    throw new InvalidOperationException("类型加载失败: " + fullName);
                }
                object? instance = Activator.CreateInstance(type);
                if (instance == null)
                {
                    alc.Unload();
                    throw new InvalidOperationException("无法实例化生成流程: " + type.FullName);
                }
                handles[i] = new FlowHandle(alc, (IObservableFlow)instance, Path.GetFullPath(dllPath));
            }
            return handles;
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
