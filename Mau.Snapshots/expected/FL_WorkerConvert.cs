// ═══ WorkerConvert 生成物 — Mau v2.0 翻译器 ═══
// 生成物由翻译器确定性输出——手工修改无效，改 .mau 后重新生成
using System;
using System.Collections.Generic;
using Mau.Runtime;

namespace Mau.Generated
{
    /// <summary>
    /// 生成物——WorkerConvert 受控系统
    /// </summary>
    public sealed class WorkerConvert
    {
        // [注入字段]
        private string? _input;
        private string? _output;

        /// <summary>
        /// 注入字段设置——input（纯赋值，不置位信号）
        /// </summary>
        /// <param name="value">注入值</param>
        public void SetInput(string? value) { _input = value; }

        /// <summary>
        /// 注入字段设置——output（纯赋值，不置位信号）
        /// </summary>
        /// <param name="value">注入值</param>
        public void SetOutput(string? value) { _output = value; }

        private enum S_Conv_State { Idle, Done, Failed }
        private S_Conv_State _S_Conv_State;

        // [命题]
        private bool P_Input;

        // [控制律 Cube]
        private readonly Cube T_WorkerConvert_Cube = new Cube(300);

        // [worker 汇合 T_WorkerConvert——∥ 后台执行 + ⋈ Inbox 回投]
        private readonly Inbox<T_WorkerConvert_WorkerResult> _T_WorkerConvert_Inbox = new Inbox<T_WorkerConvert_WorkerResult>();
        private bool _T_WorkerConvert_Busy;
        private bool _T_WorkerConvert_TimedOut;

        /// <summary>
        /// worker 结果载荷——T_WorkerConvert 后台执行结果（Ok + out 端口回投）
        /// </summary>
        private sealed class T_WorkerConvert_WorkerResult
        {
            /// <summary>执行成功</summary>
            public bool Ok;
        }

        /// <summary>
        /// 构造——初始状态置位
        /// </summary>
        public WorkerConvert()
        {
            S_Conv_Enter_Idle();
        }

        /// <summary>
        /// 进入 S_Conv.Idle——单值赋值（互斥由类型系统保证）
        /// </summary>
        private void S_Conv_Enter_Idle()
        {
            _S_Conv_State = S_Conv_State.Idle;
        }

        /// <summary>
        /// 进入 S_Conv.Done——单值赋值（互斥由类型系统保证）
        /// </summary>
        private void S_Conv_Enter_Done()
        {
            _S_Conv_State = S_Conv_State.Done;
        }

        /// <summary>
        /// 进入 S_Conv.Failed——单值赋值（互斥由类型系统保证）
        /// </summary>
        private void S_Conv_Enter_Failed()
        {
            _S_Conv_State = S_Conv_State.Failed;
        }

        /// <summary>
        /// S_Conv 是否处于 Idle
        /// </summary>
        public bool IsConvIdle() { return _S_Conv_State == S_Conv_State.Idle; }

        /// <summary>
        /// S_Conv 是否处于 Done
        /// </summary>
        public bool IsConvDone() { return _S_Conv_State == S_Conv_State.Done; }

        /// <summary>
        /// S_Conv 是否处于 Failed
        /// </summary>
        public bool IsConvFailed() { return _S_Conv_State == S_Conv_State.Failed; }

        /// <summary>
        /// S_Conv 当前状态名
        /// </summary>
        public string GetConvState()
        {
            switch (_S_Conv_State)
            {
                case S_Conv_State.Idle: return "Idle";
                case S_Conv_State.Done: return "Done";
                case S_Conv_State.Failed: return "Failed";
                default: return "Unknown";
            }
        }

        /// <summary>
        /// 帧驱动——测量采样 + 控制律守卫（声明顺序）
        /// </summary>
        /// <param name="frame">全局帧号</param>
        public void Tick(int frame)
        {
            T_WorkerConvert_Execute(frame);
        }

        /// <summary>
        /// T_WorkerConvert 控制律（∥ worker）——主线程守卫 + 后台操作 + ⋈ 汇合应用
        /// </summary>
        /// <param name="frame">全局帧号</param>
        private void T_WorkerConvert_Execute(int frame)
        {
            // [段1] 条件守卫——Busy 门防重入（worker 执行中不重复触发）
            if ((P_Input) && !_T_WorkerConvert_Busy)
            {
                // [段2] 信号消费——触发即清除
                P_Input = false;
                // [段4] 输入冻结——f_ 局部快照（后台线程不读主线程字段）
                string? f_input = _input;
                string? f_output = _output;
                AuditBrick("invoke", "T_WorkerConvert", "file.convert", frame);
                // [段5] worker 后台执行（∥——Task.Run + Inbox 回投）
                _T_WorkerConvert_Busy = true;
                _T_WorkerConvert_TimedOut = false;
                T_WorkerConvert_Cube.Start();
                System.Threading.Tasks.Task.Run(delegate ()
                {
                    bool ok = false;
                    try
                    {
                        ok = Mau.Bricks.BRIK_FILE_001.Convert(f_input, f_output);
                    }
                    catch (Exception ex)
                    {
                        ok = false;
                    }
                    T_WorkerConvert_WorkerResult r0 = new T_WorkerConvert_WorkerResult();
                    r0.Ok = ok;
                    _T_WorkerConvert_Inbox.Enqueue(r0);
                });
            }
            // [段6] τ 时限——worker 运行期间 Cube 步进 + 耗尽 → 失败后置
            else if (_T_WorkerConvert_Busy && T_WorkerConvert_Cube.IsRunning())
            {
                T_WorkerConvert_Cube.TickFrame();
                if (T_WorkerConvert_Cube.IsExpired())
                {
                    _T_WorkerConvert_TimedOut = true;
                    S_Conv_Enter_Failed();
                    T_WorkerConvert_Cube.Reset();
                }
            }
            // [段7] inbox 汇合（⋈）——后台结果主线程应用
            _T_WorkerConvert_Inbox.Drain(delegate (T_WorkerConvert_WorkerResult r)
            {
                _T_WorkerConvert_Busy = false;
                string stage = "ok";
                if (!r.Ok)
                {
                    stage = "error";
                }
                AuditBrick(stage, "T_WorkerConvert", "file.convert", frame);
                if (_T_WorkerConvert_TimedOut)
                {
                    // 超时后到达——结果丢弃（超时分支已应用失败后置与资源释放）
                    return;
                }
                T_WorkerConvert_Cube.Complete();
                if (r.Ok)
                {
                    S_Conv_Enter_Done();
                }
                else
                {
                    S_Conv_Enter_Failed();
                }
            });
        }

        /// <summary>
        /// 外部投递——P_Input（带载荷）
        /// </summary>
        public void FireInput(string input, string output)
        {
            _input = input;
            _output = output;
            P_Input = true;
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
// #MAU_CHECKSUM:SHA256:E99A2C43CC4E602624C96A37518A90CB2D9F76E45E2536528C9667DB1F5DCDE1