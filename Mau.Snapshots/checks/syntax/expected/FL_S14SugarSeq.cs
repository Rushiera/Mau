// ═══ S14SugarSeq 生成物 — Mau v2.0 翻译器 ═══
// 生成物由翻译器确定性输出——手工修改无效，改 .mau 后重新生成
using System;
using System.Collections.Generic;
using Mau.Runtime;

namespace Mau.Generated
{
    /// <summary>
    /// 生成物——S14SugarSeq 受控系统
    /// </summary>
    public sealed class S14SugarSeq
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

        private enum S_Flow_State { Idle, A, B }
        private S_Flow_State _S_Flow_State;

        private enum S_Seq_State { Step1, Step2, Done }
        private S_Seq_State _S_Seq_State;

        // [命题]
        private bool P_Start;

        // [控制律 Cube]
        private readonly Cube T_Step1_Cube = new Cube(100);
        private readonly Cube T_Step2_Cube = new Cube(100);

        /// <summary>
        /// 构造——初始状态置位
        /// </summary>
        public S14SugarSeq()
        {
            S_Flow_Enter_Idle();
            S_Seq_Enter_Step1();
        }

        /// <summary>
        /// 进入 S_Flow.Idle——单值赋值（互斥由类型系统保证）
        /// </summary>
        private void S_Flow_Enter_Idle()
        {
            _S_Flow_State = S_Flow_State.Idle;
        }

        /// <summary>
        /// 进入 S_Flow.A——单值赋值（互斥由类型系统保证）
        /// </summary>
        private void S_Flow_Enter_A()
        {
            _S_Flow_State = S_Flow_State.A;
        }

        /// <summary>
        /// 进入 S_Flow.B——单值赋值（互斥由类型系统保证）
        /// </summary>
        private void S_Flow_Enter_B()
        {
            _S_Flow_State = S_Flow_State.B;
        }

        /// <summary>
        /// 进入 S_Seq.Step1——单值赋值（互斥由类型系统保证）
        /// </summary>
        private void S_Seq_Enter_Step1()
        {
            _S_Seq_State = S_Seq_State.Step1;
        }

        /// <summary>
        /// 进入 S_Seq.Step2——单值赋值（互斥由类型系统保证）
        /// </summary>
        private void S_Seq_Enter_Step2()
        {
            _S_Seq_State = S_Seq_State.Step2;
        }

        /// <summary>
        /// 进入 S_Seq.Done——单值赋值（互斥由类型系统保证）
        /// </summary>
        private void S_Seq_Enter_Done()
        {
            _S_Seq_State = S_Seq_State.Done;
        }

        /// <summary>
        /// S_Flow 是否处于 Idle
        /// </summary>
        public bool IsFlowIdle() { return _S_Flow_State == S_Flow_State.Idle; }

        /// <summary>
        /// S_Flow 是否处于 A
        /// </summary>
        public bool IsFlowA() { return _S_Flow_State == S_Flow_State.A; }

        /// <summary>
        /// S_Flow 是否处于 B
        /// </summary>
        public bool IsFlowB() { return _S_Flow_State == S_Flow_State.B; }

        /// <summary>
        /// S_Seq 是否处于 Step1
        /// </summary>
        public bool IsSeqStep1() { return _S_Seq_State == S_Seq_State.Step1; }

        /// <summary>
        /// S_Seq 是否处于 Step2
        /// </summary>
        public bool IsSeqStep2() { return _S_Seq_State == S_Seq_State.Step2; }

        /// <summary>
        /// S_Seq 是否处于 Done
        /// </summary>
        public bool IsSeqDone() { return _S_Seq_State == S_Seq_State.Done; }

        /// <summary>
        /// S_Flow 当前状态名
        /// </summary>
        public string GetFlowState()
        {
            switch (_S_Flow_State)
            {
                case S_Flow_State.Idle: return "Idle";
                case S_Flow_State.A: return "A";
                case S_Flow_State.B: return "B";
                default: return "Unknown";
            }
        }

        /// <summary>
        /// S_Seq 当前状态名
        /// </summary>
        public string GetSeqState()
        {
            switch (_S_Seq_State)
            {
                case S_Seq_State.Step1: return "Step1";
                case S_Seq_State.Step2: return "Step2";
                case S_Seq_State.Done: return "Done";
                default: return "Unknown";
            }
        }

        /// <summary>
        /// 帧驱动——测量采样 + 控制律守卫（声明顺序）
        /// </summary>
        /// <param name="frame">全局帧号</param>
        public void Tick(int frame)
        {
            T_Step1_Execute(frame);
            T_Step2_Execute(frame);
        }

        /// <summary>
        /// T_Step1 控制律——条件 + 操作 → 结果
        /// </summary>
        /// <param name="frame">全局帧号</param>
        private void T_Step1_Execute(int frame)
        {
            // [段1] 条件守卫——全部成立 → 触发
            if (P_Start && T_Step1_Cube.IsIdle())
            {
                // [段2] 信号消费——触发即清除
                P_Start = false;
                T_Step1_Cube.Start();
                // [段4] 操作——顺序执行 + try-catch 隔离 + 自动审计
                bool ok = false;
                try
                {
                    AuditBrick("invoke", "T_Step1", "file.convert", frame);
                    ok = Mau.Bricks.BRIK_FILE_001.Convert(_input, _output);
                    AuditBrick("ok", "T_Step1", "file.convert", frame);
                }
                catch (Exception ex)
                {
                    ok = false;
                    AuditBrick("error", "T_Step1", "file.convert", frame);
                }
                // [段5] 结果转移——bool 驱动首项成功 / 次项失败 / 多路 switch 分发
                if (ok)
                {
                    S_Flow_Enter_A();
                    S_Seq_Enter_Step2();
                }
                else
                {
                    S_Flow_Enter_Idle();
                }
                // [段6b] Cube 复位——触发完成（成功/失败均复位，允许再次触发）
                T_Step1_Cube.Complete();
            }
            // [段7] τ 时限——Cube 步进 + 耗尽 → 次项（超时走失败分支）
            else if (P_Start && T_Step1_Cube.IsRunning())
            {
                T_Step1_Cube.TickFrame();
                if (T_Step1_Cube.IsExpired())
                {
                    S_Flow_Enter_Idle();
                    T_Step1_Cube.Reset();
                }
            }
        }

        /// <summary>
        /// T_Step2 控制律——条件 + 操作 → 结果
        /// </summary>
        /// <param name="frame">全局帧号</param>
        private void T_Step2_Execute(int frame)
        {
            // [段1] 条件守卫——全部成立 → 触发
            if (_S_Flow_State == S_Flow_State.A && T_Step2_Cube.IsIdle())
            {
                T_Step2_Cube.Start();
                // [段4] 操作——顺序执行 + try-catch 隔离 + 自动审计
                bool ok = false;
                try
                {
                    AuditBrick("invoke", "T_Step2", "file.convert", frame);
                    ok = Mau.Bricks.BRIK_FILE_001.Convert(_input, _output);
                    AuditBrick("ok", "T_Step2", "file.convert", frame);
                }
                catch (Exception ex)
                {
                    ok = false;
                    AuditBrick("error", "T_Step2", "file.convert", frame);
                }
                // [段5] 结果转移——bool 驱动首项成功 / 次项失败 / 多路 switch 分发
                if (ok)
                {
                    S_Flow_Enter_B();
                    S_Seq_Enter_Done();
                }
                else
                {
                    S_Flow_Enter_Idle();
                }
                // [段6b] Cube 复位——触发完成（成功/失败均复位，允许再次触发）
                T_Step2_Cube.Complete();
            }
            // [段7] τ 时限——Cube 步进 + 耗尽 → 次项（超时走失败分支）
            else if (_S_Flow_State == S_Flow_State.A && T_Step2_Cube.IsRunning())
            {
                T_Step2_Cube.TickFrame();
                if (T_Step2_Cube.IsExpired())
                {
                    S_Flow_Enter_Idle();
                    T_Step2_Cube.Reset();
                }
            }
        }

        /// <summary>
        /// 外部投递——P_Start（带载荷）
        /// </summary>
        public void FireStart(string input, string output)
        {
            _input = input;
            _output = output;
            P_Start = true;
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
// #MAU_CHECKSUM:SHA256:A3437EC193AD08CC1AA7B046DA6F2C5D5F86E505957CCF00D48667662FCD699B
