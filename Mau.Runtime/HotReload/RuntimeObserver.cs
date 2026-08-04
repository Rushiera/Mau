using System;

namespace Mau.Runtime
{
    /// <summary>
    /// 运行时观察器——每 Tick 轮询 FlowHost，统合状态推送到 Transport
    /// 由宿主（Mau.Cli serve / CH4）每帧调用 Tick()
    /// </summary>
    public sealed class RuntimeObserver
    {
        private readonly FlowHost _host;
        private IObserverTransport? _transport;
        private IOA? _oa;
        private ICommandBus? _commandBus;
        private IdAllocator? _idAllocator;
        private ThreadGuard? _threadGuard;
        private long _frame;
        private AggregatedSnapshot? _latestSnapshot;

        /// <summary>
        /// 当前帧号
        /// </summary>
        public long Frame
        {
            get { return _frame; }
        }

        /// <summary>
        /// 构造观察器
        /// </summary>
        /// <param name="host">Flow 宿主</param>
        public RuntimeObserver(FlowHost host)
        {
            _host = host;
            _transport = null;
            _oa = null;
            _commandBus = null;
            _idAllocator = null;
            _threadGuard = null;
            _frame = 0;
        }

        /// <summary>
        /// 绑定传输层
        /// </summary>
        /// <param name="transport">传输实现</param>
        public void Bind(IObserverTransport transport)
        {
            _transport = transport;
        }

        /// <summary>
        /// 绑定宿主机制——OA/Command/ID/线程守卫的透明度暴露
        /// </summary>
        /// <param name="oa">OA 工单机制，可为 null</param>
        /// <param name="commandBus">指令总线，可为 null</param>
        /// <param name="idAllocator">ID 分配器，可为 null</param>
        /// <param name="threadGuard">线程守卫，可为 null</param>
        public void BindHostMechanisms(IOA? oa, ICommandBus? commandBus,
            IdAllocator? idAllocator, ThreadGuard? threadGuard)
        {
            _oa = oa;
            _commandBus = commandBus;
            _idAllocator = idAllocator;
            _threadGuard = threadGuard;
        }

        /// <summary>
        /// 获取最近一次统合快照——供宿主控制台/Cli 查询
        /// </summary>
        /// <returns>最近快照，未 Tick 过返回 null</returns>
        public AggregatedSnapshot? GetLatestSnapshot()
        {
            return _latestSnapshot;
        }

        /// <summary>
        /// 每帧轮询并推送
        /// </summary>
        public void Tick()
{
            _frame = _frame + 1;

            System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();

            IObserverTransport? transport = _transport;
            if (transport == null)
            {
                return;
            }

            FlowHandle[] handles = _host.Handles as FlowHandle[] ?? Array.Empty<FlowHandle>();
            System.Collections.Generic.List<SystemEvent> sysEvents = new System.Collections.Generic.List<SystemEvent>();
            FlowSnapshotEntry[] entries = new FlowSnapshotEntry[handles.Length];
            for (int i = 0; i < handles.Length; i = i + 1)
            {
                FlowHandle h = handles[i];
                IObservableFlow flow = h.Flow;
                entries[i] = new FlowSnapshotEntry
                {
                    FlowName = flow.GetType().Name,
                    Status = flow.GetStatus(),
                    Logs = flow.GetLogs()
                };

                if (h.IsFaulted)
                {
                    sysEvents.Add(new SystemEvent
                    {
                        HostFrame = _frame,
                        Timestamp = DateTime.UtcNow,
                        Level = "Error",
                        Source = entries[i].FlowName,
                        Message = "Flow 故障已隔离: " + h.FaultReason
                    });
                }
            }

            sw.Stop();
            long elapsedMs = sw.ElapsedMilliseconds;
            if (elapsedMs > 200)
            {
                sysEvents.Add(new SystemEvent
                {
                    HostFrame = _frame,
                    Timestamp = DateTime.UtcNow,
                    Level = "Error",
                    Source = "Observer",
                    Message = "帧超时 " + elapsedMs.ToString() + "ms"
                });
            }
            else if (elapsedMs > 50)
            {
                sysEvents.Add(new SystemEvent
                {
                    HostFrame = _frame,
                    Timestamp = DateTime.UtcNow,
                    Level = "Warn",
                    Source = "Observer",
                    Message = "帧耗时 " + elapsedMs.ToString() + "ms"
                });
            }

            AggregatedSnapshot snapshot = new AggregatedSnapshot
            {
                Timestamp = DateTime.UtcNow,
                HostFrame = _frame,
                Flows = entries,
                SystemEvents = sysEvents.ToArray(),
                Host = BuildHostSnapshot()
            };

            _latestSnapshot = snapshot;

            try
            {
                transport.Push(snapshot);
            }
            catch
            {
            }
        }

        /// <summary>
        /// 构建宿主机制快照——OA/Command/ID/线程守卫统一截面
        /// </summary>
        /// <returns>宿主快照，未绑定任何机制时为 null</returns>
        private HostSnapshot? BuildHostSnapshot()
        {
            if (_oa == null && _commandBus == null && _idAllocator == null
                && _threadGuard == null)
            {
                return null;
            }
            HostSnapshot host = new HostSnapshot();
            host.Frame = _frame;
            if (_threadGuard != null)
            {
                host.IsMainThread = _threadGuard.IsMainThread;
            }
            if (_oa != null)
            {
                host.OA = _oa.GetSnapshot();
            }
            if (_commandBus != null)
            {
                host.Command = _commandBus.GetSnapshot();
            }
            if (_idAllocator != null)
            {
                host.IdTypeCounts = _idAllocator.GetTypeCountSnapshot();
                host.NextId = _idAllocator.NextId;
            }
            return host;
        }
    }
}
