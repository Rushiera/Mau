// ═══ S09Attrs 生成物 — Mau v2.0 翻译器 ═══
// 生成物由翻译器确定性输出——手工修改无效，改 .mau 后重新生成
using System;
using System.Collections.Generic;
using Mau.Runtime;

namespace Mau.Generated
{
    /// <summary>
    /// 生成物——S09Attrs 受控系统
    /// </summary>
    public sealed class S09Attrs
    {
        private enum S_Run_State { Idle, Done, Failed }
        private S_Run_State _S_Run_State;

        // [命题]
        private bool P_Go;

        // [控制律 Cube]
        private readonly Cube T_Work_Cube = new Cube(10);

        // [worker 汇合 T_Work——∥ 后台执行 + ⋈ Inbox 回投]
        private readonly Inbox<T_Work_WorkerResult> _T_Work_Inbox = new Inbox<T_Work_WorkerResult>();
        private bool _T_Work_Busy;
        private bool _T_Work_TimedOut;

        /// <summary>
        /// worker 结果载荷——T_Work 后台执行结果（Ok + out 端口回投）
        /// </summary>
        private sealed class T_Work_WorkerResult
        {
            /// <summary>执行成功</summary>
            public bool Ok;
        }

        /// <summary>
        /// 构造——初始状态置位
        /// </summary>
        public S09Attrs()
        {
            S_Run_Enter_Idle();
        }

        /// <summary>
        /// 进入 S_Run.Idle——单值赋值（互斥由类型系统保证）
        /// </summary>
        private void S_Run_Enter_Idle()
        {
            _S_Run_State = S_Run_State.Idle;
        }

        /// <summary>
        /// 进入 S_Run.Done——单值赋值（互斥由类型系统保证）
        /// </summary>
        private void S_Run_Enter_Done()
        {
            _S_Run_State = S_Run_State.Done;
        }

        /// <summary>
        /// 进入 S_Run.Failed——单值赋值（互斥由类型系统保证）
        /// </summary>
        private void S_Run_Enter_Failed()
        {
            _S_Run_State = S_Run_State.Failed;
        }

        /// <summary>
        /// S_Run 是否处于 Idle
        /// </summary>
        public bool IsRunIdle() { return _S_Run_State == S_Run_State.Idle; }

        /// <summary>
        /// S_Run 是否处于 Done
        /// </summary>
        public bool IsRunDone() { return _S_Run_State == S_Run_State.Done; }

        /// <summary>
        /// S_Run 是否处于 Failed
        /// </summary>
        public bool IsRunFailed() { return _S_Run_State == S_Run_State.Failed; }

        /// <summary>
        /// S_Run 当前状态名
        /// </summary>
        public string GetRunState()
        {
            switch (_S_Run_State)
            {
                case S_Run_State.Idle: return "Idle";
                case S_Run_State.Done: return "Done";
                case S_Run_State.Failed: return "Failed";
                default: return "Unknown";
            }
        }

        /// <summary>
        /// 帧驱动——测量采样 + 控制律守卫（声明顺序）
        /// </summary>
        /// <param name="frame">全局帧号</param>
        public void Tick(int frame)
        {
            T_Work_Execute(frame);
        }

        /// <summary>
        /// T_Work 控制律（∥ worker）——主线程守卫 + 后台操作 + ⋈ 汇合应用
        /// </summary>
        /// <param name="frame">全局帧号</param>
        private void T_Work_Execute(int frame)
        {
            // [段1] 条件守卫——Busy 门防重入（worker 执行中不重复触发）
            if ((P_Go) && !_T_Work_Busy)
            {
                // [段2] 信号消费——触发即清除
                P_Go = false;
                // [段4] 输入冻结——f_ 局部快照（后台线程不读主线程字段）
                string f_src = _src;
                string f_dst = _dst;
                AuditBrick("invoke", "T_Work", "file.convert", frame);
                // [段5] worker 后台执行（∥——Task.Run + Inbox 回投）
                _T_Work_Busy = true;
                _T_Work_TimedOut = false;
                T_Work_Cube.Start();
                System.Threading.Tasks.Task.Run(delegate ()
                {
                    bool ok = false;
                    try
                    {
                        ok = Mau.Bricks.BRIK_FILE_001.Convert(f_src, f_dst);
                    }
                    catch (Exception ex)
                    {
                        ok = false;
                    }
                    T_Work_WorkerResult r0 = new T_Work_WorkerResult();
                    r0.Ok = ok;
                    _T_Work_Inbox.Enqueue(r0);
                });
            }
            // [段6] τ 时限——worker 运行期间 Cube 步进 + 耗尽 → 失败后置
            else if (_T_Work_Busy && T_Work_Cube.IsRunning())
            {
                T_Work_Cube.TickFrame();
                if (T_Work_Cube.IsExpired())
                {
                    _T_Work_TimedOut = true;
                    if (LogStore.AllLog != null) { LogStore.Add("LAW", 0, "T_Work | 超时 | frame=" + frame, ""); }
                    S_Run_Enter_Failed();
                    T_Work_Cube.Reset();
                }
            }
            // [段7] inbox 汇合（⋈）——后台结果主线程应用
            _T_Work_Inbox.Drain(delegate (T_Work_WorkerResult r)
            {
                _T_Work_Busy = false;
                string stage = "ok";
                if (!r.Ok)
                {
                    stage = "error";
                }
                AuditBrick(stage, "T_Work", "file.convert", frame);
                if (LogStore.AllLog != null) { LogStore.Add("LAW", 0, "T_Work | " + stage + " | frame=" + frame, ""); }
                if (_T_Work_TimedOut)
                {
                    // 超时后到达——结果丢弃（超时分支已应用失败后置与资源释放）
                    return;
                }
                T_Work_Cube.Complete();
                if (r.Ok)
                {
                    S_Run_Enter_Done();
                }
                else
                {
                    S_Run_Enter_Failed();
                }
            });
        }

        /// <summary>
        /// 外部投递——P_Go
        /// </summary>
        public void FireGo()
        {
            P_Go = true;
        }

        /// <summary>
        /// 积木调用审计——brick.invoke/ok/error（自动审计埋点，观测不改变系统）
        /// </summary>
        /// <param name="stage">阶段——invoke/ok/error</param>
        /// <param name="law">控制律名</param>
        /// <param name="brick">积木名</param>
        /// <param name="frame">全局帧号</param>
        private void AuditBrick(string stage, string law, string brick, int frame)
        {
            if (AuditStore.Default != null)
            {
                AuditStore.Default.Record("Flow", "brick." + stage, frame, new AuditProp[] {
                    new AuditProp("flow", this.GetType().Name),
                    new AuditProp("law", law),
                    new AuditProp("brick", brick)
                }, false);
            }
        }

    }
}
// #MAU_CHECKSUM:SHA256:57C6D1038686521F72FD9A52E3BA246B1550C82F51389B2298A7826FC3CCEE36
