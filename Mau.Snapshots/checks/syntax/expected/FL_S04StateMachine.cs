// ═══ S04StateMachine 生成物 — Mau v2.0 翻译器 ═══
// 生成物由翻译器确定性输出——手工修改无效，改 .mau 后重新生成
using System;
using System.Collections.Generic;
using Mau.Runtime;

namespace Mau.Generated
{
    /// <summary>
    /// 生成物——S04StateMachine 受控系统
    /// </summary>
    public sealed class S04StateMachine
    {
        private enum S_Flow_State { Idle, Working, Done, Failed }
        private S_Flow_State _S_Flow_State;

        // [命题]
        private bool P_Go;

        // [控制律 Cube]
        private readonly Cube T_Go_Cube = new Cube(5);
        private readonly Cube T_Done_Cube = new Cube(5);
        private readonly Cube T_Back_Cube = new Cube(5);

        /// <summary>
        /// 构造——初始状态置位
        /// </summary>
        public S04StateMachine()
        {
            S_Flow_Enter_Idle();
        }

        /// <summary>
        /// 进入 S_Flow.Idle——单值赋值（互斥由类型系统保证）
        /// </summary>
        private void S_Flow_Enter_Idle()
        {
            _S_Flow_State = S_Flow_State.Idle;
        }

        /// <summary>
        /// 进入 S_Flow.Working——单值赋值（互斥由类型系统保证）
        /// </summary>
        private void S_Flow_Enter_Working()
        {
            _S_Flow_State = S_Flow_State.Working;
        }

        /// <summary>
        /// 进入 S_Flow.Done——单值赋值（互斥由类型系统保证）
        /// </summary>
        private void S_Flow_Enter_Done()
        {
            _S_Flow_State = S_Flow_State.Done;
        }

        /// <summary>
        /// 进入 S_Flow.Failed——单值赋值（互斥由类型系统保证）
        /// </summary>
        private void S_Flow_Enter_Failed()
        {
            _S_Flow_State = S_Flow_State.Failed;
        }

        /// <summary>
        /// S_Flow 是否处于 Idle
        /// </summary>
        public bool IsFlowIdle() { return _S_Flow_State == S_Flow_State.Idle; }

        /// <summary>
        /// S_Flow 是否处于 Working
        /// </summary>
        public bool IsFlowWorking() { return _S_Flow_State == S_Flow_State.Working; }

        /// <summary>
        /// S_Flow 是否处于 Done
        /// </summary>
        public bool IsFlowDone() { return _S_Flow_State == S_Flow_State.Done; }

        /// <summary>
        /// S_Flow 是否处于 Failed
        /// </summary>
        public bool IsFlowFailed() { return _S_Flow_State == S_Flow_State.Failed; }

        /// <summary>
        /// S_Flow 当前状态名
        /// </summary>
        public string GetFlowState()
        {
            switch (_S_Flow_State)
            {
                case S_Flow_State.Idle: return "Idle";
                case S_Flow_State.Working: return "Working";
                case S_Flow_State.Done: return "Done";
                case S_Flow_State.Failed: return "Failed";
                default: return "Unknown";
            }
        }

        /// <summary>
        /// 帧驱动——测量采样 + 控制律守卫（声明顺序）
        /// </summary>
        /// <param name="frame">全局帧号</param>
        public void Tick(int frame)
        {
            T_Go_Execute(frame);
            T_Done_Execute(frame);
            T_Back_Execute(frame);
        }

        /// <summary>
        /// T_Go 控制律——条件 + 操作 → 结果
        /// </summary>
        /// <param name="frame">全局帧号</param>
        private void T_Go_Execute(int frame)
        {
            // [段1] 条件守卫——全部成立 → 触发
            if (P_Go && T_Go_Cube.IsIdle())
            {
                // [段2] 信号消费——触发即清除
                P_Go = false;
                T_Go_Cube.Start();
                // [段4] 操作——顺序执行 + try-catch 隔离 + 自动审计
                bool ok = false;
                try
                {
                    AuditBrick("invoke", "T_Go", "file.convert", frame);
                    ok = Mau.Bricks.BRIK_FILE_001.Convert(_src, _dst);
                    AuditBrick("ok", "T_Go", "file.convert", frame);
                }
                catch (Exception ex)
                {
                    ok = false;
                    AuditBrick("error", "T_Go", "file.convert", frame);
                }
                // [段5] 结果转移——bool 驱动首项成功 / 次项失败 / 多路 switch 分发
                if (ok)
                {
                    S_Flow_Enter_Working();
                }
                else
                {
                    S_Flow_Enter_Failed();
                }
                // [段6b] Cube 复位——触发完成（成功/失败均复位，允许再次触发）
                T_Go_Cube.Complete();
            }
            // [段7] τ 时限——Cube 步进 + 耗尽 → 次项（超时走失败分支）
            else if (P_Go && T_Go_Cube.IsRunning())
            {
                T_Go_Cube.TickFrame();
                if (T_Go_Cube.IsExpired())
                {
                    S_Flow_Enter_Failed();
                    T_Go_Cube.Reset();
                }
            }
        }

        /// <summary>
        /// T_Done 控制律——条件 + 操作 → 结果
        /// </summary>
        /// <param name="frame">全局帧号</param>
        private void T_Done_Execute(int frame)
        {
            // [段1] 条件守卫——全部成立 → 触发
            if (_S_Flow_State == S_Flow_State.Working && T_Done_Cube.IsIdle())
            {
                T_Done_Cube.Start();
                // [段4] 操作——顺序执行 + try-catch 隔离 + 自动审计
                bool ok = false;
                try
                {
                    AuditBrick("invoke", "T_Done", "file.convert", frame);
                    ok = Mau.Bricks.BRIK_FILE_001.Convert(_src2, _dst2);
                    AuditBrick("ok", "T_Done", "file.convert", frame);
                }
                catch (Exception ex)
                {
                    ok = false;
                    AuditBrick("error", "T_Done", "file.convert", frame);
                }
                // [段5] 结果转移——bool 驱动首项成功 / 次项失败 / 多路 switch 分发
                if (ok)
                {
                    S_Flow_Enter_Done();
                }
                else
                {
                    S_Flow_Enter_Failed();
                }
                // [段6b] Cube 复位——触发完成（成功/失败均复位，允许再次触发）
                T_Done_Cube.Complete();
            }
            // [段7] τ 时限——Cube 步进 + 耗尽 → 次项（超时走失败分支）
            else if (_S_Flow_State == S_Flow_State.Working && T_Done_Cube.IsRunning())
            {
                T_Done_Cube.TickFrame();
                if (T_Done_Cube.IsExpired())
                {
                    S_Flow_Enter_Failed();
                    T_Done_Cube.Reset();
                }
            }
        }

        /// <summary>
        /// T_Back 控制律——条件 + 操作 → 结果
        /// </summary>
        /// <param name="frame">全局帧号</param>
        private void T_Back_Execute(int frame)
        {
            // [段1] 条件守卫——全部成立 → 触发
            if ((_S_Flow_State == S_Flow_State.Done) || (_S_Flow_State == S_Flow_State.Failed) && T_Back_Cube.IsIdle())
            {
                T_Back_Cube.Start();
                // [段4] 操作——无（纯转移控制律）
                bool ok = true;
                // [段5] 结果转移——bool 驱动首项成功 / 次项失败 / 多路 switch 分发
                S_Flow_Enter_Idle();
                // [段6b] Cube 复位——触发完成（成功/失败均复位，允许再次触发）
                T_Back_Cube.Complete();
            }
            // [段7] τ 时限——Cube 步进 + 耗尽 → 次项（超时走失败分支）
            else if ((_S_Flow_State == S_Flow_State.Done) || (_S_Flow_State == S_Flow_State.Failed) && T_Back_Cube.IsRunning())
            {
                T_Back_Cube.TickFrame();
                if (T_Back_Cube.IsExpired())
                {
                    T_Back_Cube.Reset();
                }
            }
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
// #MAU_CHECKSUM:SHA256:BCB382729B2B54D93E1E4A933E0CE35FF9A7123CCF4A710E504E478D208FEB93
