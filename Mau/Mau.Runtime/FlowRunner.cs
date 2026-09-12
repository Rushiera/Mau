using System;
using System.Collections.Generic;

namespace Mau.Runtime
{
    /// <summary>
    /// 帧序宿主——实体注册 + 帧序编排（冻结 → 排空 → 分发 → 驱动 → 结算）+ Inbox 桥 + 透明化。
    /// v3 纯化：产品语义零残留（v2 的 Dog 自动回收/dog·pet 分类随产品组件退役）。
    /// 数字电路映射：Tick = 时钟脉冲；帧序 = 组合逻辑时序；Inbox = 跨线程回投通道。
    /// </summary>
    public sealed class FlowRunner
    {
        /// <summary>
        /// 线程归属守卫——构造绑定宿主主线程
        /// </summary>
        private readonly ThreadGuard _guard;

        /// <summary>
        /// 实体注册表——Flow 注册/查找/回收
        /// </summary>
        private readonly FlowRegistry _registry;

        /// <summary>
        /// OA 工单平台——超时结算
        /// </summary>
        private readonly IOA _oa;

        /// <summary>
        /// 指令总线——输入冻结 + 邮件分发
        /// </summary>
        private readonly ICommandBus _cmd;

        /// <summary>
        /// 主线程 Inbox——后台回调投递，Tick 开头排空
        /// </summary>
        private readonly Inbox<Action> _mainInbox = new Inbox<Action>();

        /// <summary>
        /// 传感器壳协程列表——主动传感器独立起点（Pet 推动形态：与 Flow 帧驱动分离的循环实体）
        /// </summary>
        private readonly List<SensorLoopEntry> _sensorLoops = new List<SensorLoopEntry>();

        /// <summary>
        /// 指令分发钩子——宿主注册（id, mail），本层不感知产品类型
        /// </summary>
        private Action<long, CommandPack>? _commandDispatch;

        /// <summary>
        /// 内部帧号——每 Tick 自增
        /// </summary>
        private long _frame;

        /// <summary>
        /// 全局当前帧号——每 Tick 更新（日志/积木静态读取；0=未驱动）
        /// </summary>
        public static long GlobalFrame;

        /// <summary>
        /// 是否已初始化
        /// </summary>
        private bool _inited;

        /// <summary>
        /// 审计存储——宿主注入后机制事件写入（null = 不审计）
        /// </summary>
        public AuditStore? Audit
        {
            get;
            set;
        }

        /// <summary>
        /// 构造宿主——注入全部机制
        /// </summary>
        /// <param name="guard">线程归属守卫</param>
        /// <param name="oa">OA 工单平台</param>
        /// <param name="cmd">指令总线</param>
        /// <param name="ids">全局 ID 分配器</param>
        public FlowRunner(ThreadGuard guard, IOA oa, ICommandBus cmd, IdAllocator ids)
        {
            if (guard == null)
            {
                throw new ArgumentNullException("guard");
            }
            if (oa == null)
            {
                throw new ArgumentNullException("oa");
            }
            if (cmd == null)
            {
                throw new ArgumentNullException("cmd");
            }
            if (ids == null)
            {
                throw new ArgumentNullException("ids");
            }
            _guard = guard;
            _oa = oa;
            _cmd = cmd;
            _registry = new FlowRegistry(guard, ids);
            _inited = true;
        }

        /// <summary>
        /// 实体注册表——透明化暴露
        /// </summary>
        public FlowRegistry Registry
        {
            get
            {
                _guard.AssertMainThread("FlowRunner.Registry");
                return _registry;
            }
        }

        /// <summary>
        /// 指令分发钩子——宿主每帧取邮件后投递产品逻辑
        /// </summary>
        /// <param name="dispatch">分发委托（实体 ID, 指令邮件）</param>
        public void SetCommandDispatch(Action<long, CommandPack>? dispatch)
        {
            _guard.AssertMainThread("FlowRunner.SetCommandDispatch");
            _commandDispatch = dispatch;
        }

        /// <summary>
        /// 注册实体——分配 ID 并入注册表
        /// </summary>
        /// <param name="flow">Flow 实例</param>
        /// <param name="name">实体名字</param>
        /// <returns>分配的全局 ID</returns>
        public long RegisterFlow(IFlow flow, string name)
        {
            _guard.AssertMainThread("FlowRunner.RegisterFlow");
            EnsureInited();
            long id = _registry.Register(flow, name);
            // 传感器壳协程挂载——语料声明自己是主动传感器即自动入协程列表（ISensorLoop 接口实现）
            if (flow is ISensorLoop loop)
            {
                _sensorLoops.Add(new SensorLoopEntry(id, loop));
            }
            if (Audit != null)
            {
                Audit.Record("FlowRunner", "flow.register", -1, new AuditProp[] {
                    new AuditProp("flowId", id.ToString()),
                    new AuditProp("name", name),
                    new AuditProp("kind", "flow")
                });
            }
            return id;
        }

        /// <summary>
        /// 回收实体——从注册表移除
        /// </summary>
        /// <param name="id">实体 ID</param>
        /// <returns>true=回收成功</returns>
        public bool UnregisterFlow(long id)
        {
            _guard.AssertMainThread("FlowRunner.UnregisterFlow");
            bool ok = _registry.Unregister(id);
            if (ok)
            {
                // [段1] 传感器壳协程清理——主动传感器独立起点随 Flow 卸载
                for (int i = _sensorLoops.Count - 1; i >= 0; i = i - 1)
                {
                    if (_sensorLoops[i].FlowId == id)
                    {
                        _sensorLoops.RemoveAt(i);
                    }
                }
                // [段2] CommandBus 注销——D1 修复：旧 Flow 的 key 挂名不清理，新 Flow 同 key 注册被静默 REJECT
                _cmd.Unregister(id);
                // [段3] DataBox FlowId scope 清理——私有盒孤儿防泄漏（热重载换 ID 后旧盒残留；ClearScope 原子原语）
                DataBox.ClearScope(id.ToString());
            }
            if (Audit != null && ok)
            {
                Audit.Record("FlowRunner", "flow.unregister", -1, new AuditProp[] {
                    new AuditProp("flowId", id.ToString())
                });
            }
            return ok;
        }        /// <summary>
                 /// 按 ID 查找实体
                 /// </summary>
                 /// <param name="id">实体 ID</param>
                 /// <returns>Flow 实例或 null</returns>
        public IFlow? GetFlow(long id)
        {
            _guard.AssertMainThread("FlowRunner.GetFlow");
            return _registry.Get(id);
        }

        /// <summary>
        /// 后台→主线程投递——任意线程调用，回调在下一帧主线程执行
        /// </summary>
        /// <param name="action">待执行回调</param>
        public void PostToMain(Action action)
        {
            if (action == null)
            {
                throw new ArgumentNullException("action");
            }
            _mainInbox.Enqueue(action);
        }

        /// <summary>
        /// 主线程投递并等待完成——任意线程调用；回调经 Inbox 在下一帧主线程执行。
        /// 超时语义：主线程未驱动/已退出时等待至多 timeoutMs 返回 false；回调异常在调用线程重抛。
        /// 适用：外部线程需要同步查询主线程状态（D31 确定性——阻塞即失败带诊断）。
        /// </summary>
        /// <param name="action">待执行回调</param>
        /// <param name="timeoutMs">最长等待毫秒数</param>
        /// <returns>true=已执行完成；false=超时未执行</returns>
        public bool InvokeOnMain(Action action, int timeoutMs)
        {
            if (action == null)
            {
                throw new ArgumentNullException("action");
            }
            System.Threading.ManualResetEventSlim done = new System.Threading.ManualResetEventSlim(false);
            Exception? error = null;
            _mainInbox.Enqueue(delegate ()
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    error = ex;
                }
                finally
                {
                    done.Set();
                }
            });
            bool completed = done.Wait(timeoutMs);
            if (!completed)
            {
                return false;
            }
            if (error != null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
            }
            return true;
        }

        /// <summary>
        /// 主线程投递并等待结果——泛型版本（回调异常在调用线程重抛）
        /// </summary>
        /// <typeparam name="T">结果类型</typeparam>
        /// <param name="action">返回结果的回调</param>
        /// <param name="timeoutMs">最长等待毫秒数</param>
        /// <param name="result">回调结果（超时时为 default）</param>
        /// <returns>true=已执行完成；false=超时未执行</returns>
        public bool InvokeOnMain<T>(Func<T> action, int timeoutMs, out T result)
        {
            if (action == null)
            {
                throw new ArgumentNullException("action");
            }
            result = default!;
            System.Threading.ManualResetEventSlim done = new System.Threading.ManualResetEventSlim(false);
            Exception? error = null;
            T value = default!;
            _mainInbox.Enqueue(delegate ()
            {
                try
                {
                    value = action();
                }
                catch (Exception ex)
                {
                    error = ex;
                }
                finally
                {
                    done.Set();
                }
            });
            bool completed = done.Wait(timeoutMs);
            if (!completed)
            {
                return false;
            }
            if (error != null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
            }
            result = value;
            return true;
        }

        /// <summary>
        /// 每帧驱动——帧序：指令冻结 → Inbox 排空 → 指令分发 → 实体驱动 → OA 结算
        /// </summary>
        public void Tick()
        {
            _guard.AssertMainThread("FlowRunner.Tick");
            EnsureInited();
            _frame = _frame + 1;
            GlobalFrame = _frame;
            // F4 帧号全局盒——宿主/会话经数据面读当前帧（解耦：不依赖 FlowRunner 静态面；未来同类全局参数同模式）
            DataBox.Set<long>("global", "frame", _frame);
            if (Audit != null)
            {
                Audit.TickFrame(_frame);
            }
            // [段0] 指令输入冻结——此前到达的 Set 指令进入可消费池（帧间生效）
            _cmd.BeginTickInput();
            // [段1] Inbox 排空——后台回调在主线程执行（功能隔离：单回调异常不中断帧）
            _mainInbox.Drain(delegate (Action action)
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    RuntimeLog.ErrorOut("[FlowRunner] Inbox 回调异常: " + ex.Message);
                }
            });
            // [段2] 指令分发——每实体取邮件（有指令时调宿主钩子）
            if (_commandDispatch != null)
            {
                long[] ids = _registry.Ids;
                for (int i = 0; i < ids.Length; i++)
                {
                    CommandPack mail = _cmd.GetCommandEmail(ids[i]);
                    if (!mail.HasCommands)
                    {
                        continue;
                    }
                    try
                    {
                        _commandDispatch(ids[i], mail);
                    }
                    catch (Exception ex)
                    {
                        RuntimeLog.ErrorOut("[FlowRunner] 指令分发异常: " + ex.Message);
                    }
                }
            }
            // [段3] 实体帧驱动——帧号统一注入（时钟脉冲外部注入）+ Flow 上下文（对象唯一 ID 运行时通道）
            long[] flowIds = _registry.Ids;
            for (int i = 0; i < flowIds.Length; i++)
            {
                IFlow? flow = _registry.Get(flowIds[i]);
                if (flow != null)
                {
                    FlowContext.SetFlowId(flowIds[i]);
                    try
                    {
                        flow.Tick((int)_frame);
                    }
                    catch (Exception ex)
                    {
                        RuntimeLog.ErrorOut("[FlowRunner] Flow 驱动异常: #" + flowIds[i] + " " + ex.Message);
                    }
                    finally
                    {
                        FlowContext.Clear();
                    }
                }
            }
            // [段3b] 传感器壳协程驱动——主动传感器独立起点（Flow 上下文注入，主线程帧驱动）
            for (int i = 0; i < _sensorLoops.Count; i++)
            {
                FlowContext.SetFlowId(_sensorLoops[i].FlowId);
                try
                {
                    _sensorLoops[i].Loop.TickSensors((int)_frame);
                }
                catch (Exception ex)
                {
                    RuntimeLog.ErrorOut("[FlowRunner] 传感器壳异常: #" + _sensorLoops[i].FlowId + " " + ex.Message);
                }
                finally
                {
                    FlowContext.Clear();
                }
            }
            // [段4] OA 超时结算
            _oa.Tick();
            // [段5] flow.tick 审计——默认关（EnableTickEvents 开启时每帧记录实体数）
            if (Audit != null && Audit.EnableTickEvents)
            {
                Audit.Record("FlowRunner", "flow.tick", -1, new AuditProp[] {
                    new AuditProp("frame", _frame.ToString()),
                    new AuditProp("entities", _registry.Ids.Length.ToString())
                });
            }
        }

        /// <summary>
        /// 关闭——清空注册（实体生命周期由宿主负责）
        /// </summary>
        public void Shutdown()
        {
            _guard.AssertMainThread("FlowRunner.Shutdown");
            if (!_inited)
            {
                return;
            }
            _registry.Clear();
            _inited = false;
        }

        /// <summary>
        /// 透明化快照——帧号 + 实体条目 + OA/Command 域摘要（冻结）
        /// </summary>
        /// <returns>宿主状态快照</returns>
        public HostSnapshot GetStatus()
        {
            _guard.AssertMainThread("FlowRunner.GetStatus");
            EnsureInited();
            HostSnapshot snapshot = new HostSnapshot();
            snapshot.Frame = _frame;
            snapshot.IsMainThread = _guard.IsMainThread;
            snapshot.OA = _oa.GetSnapshot();
            snapshot.Command = _cmd.GetSnapshot();
            snapshot.Flows = _registry.Entries;
            return snapshot;
        }

        /// <summary>
        /// 校验已初始化
        /// </summary>
        /// <exception cref="InvalidOperationException">未初始化</exception>
        private void EnsureInited()
        {
            if (!_inited)
            {
                throw new InvalidOperationException("FlowRunner 已关闭——不可再驱动");
            }
        }
    }

    /// <summary>
    /// 传感器壳协程条目——FlowId（上下文注入）+ 循环实例
    /// </summary>
    internal sealed class SensorLoopEntry
    {
        /// <summary>
        /// 所属 Flow 全局 ID
        /// </summary>
        public long FlowId;

        /// <summary>
        /// 壳循环实例
        /// </summary>
        public ISensorLoop Loop;

        /// <summary>
        /// 构造条目
        /// </summary>
        /// <param name="flowId">Flow ID</param>
        /// <param name="loop">循环实例</param>
        public SensorLoopEntry(long flowId, ISensorLoop loop)
        {
            FlowId = flowId;
            Loop = loop;
        }
    }
}
