// ═══ S15SugarPar 生成物 — Mau v2.0 翻译器 ═══
// 生成物由翻译器确定性输出——手工修改无效，改 .mau 后重新生成
using System;
using System.Collections.Generic;
using Mau.Runtime;

namespace Mau.Generated
{
    /// <summary>
    /// 生成物——S15SugarPar 受控系统
    /// </summary>
    public sealed class S15SugarPar
    {
        private enum S_A_State { Idle, Done, Failed }
        private S_A_State _S_A_State;

        private enum S_B_State { Idle, Done, Failed }
        private S_B_State _S_B_State;

        // [命题]
        private bool P_Go;

        // [资源]
        private int R_Parallel_Count = 2;

        // [控制律 Cube]
        private readonly Cube T_A_Cube = new Cube(5);
        private readonly Cube T_B_Cube = new Cube(5);

        /// <summary>
        /// 构造——初始状态置位
        /// </summary>
        public S15SugarPar()
        {
            S_A_Enter_Idle();
            S_B_Enter_Idle();
        }

        /// <summary>
        /// 进入 S_A.Idle——单值赋值（互斥由类型系统保证）
        /// </summary>
        private void S_A_Enter_Idle()
        {
            _S_A_State = S_A_State.Idle;
        }

        /// <summary>
        /// 进入 S_A.Done——单值赋值（互斥由类型系统保证）
        /// </summary>
        private void S_A_Enter_Done()
        {
            _S_A_State = S_A_State.Done;
        }

        /// <summary>
        /// 进入 S_A.Failed——单值赋值（互斥由类型系统保证）
        /// </summary>
        private void S_A_Enter_Failed()
        {
            _S_A_State = S_A_State.Failed;
        }

        /// <summary>
        /// 进入 S_B.Idle——单值赋值（互斥由类型系统保证）
        /// </summary>
        private void S_B_Enter_Idle()
        {
            _S_B_State = S_B_State.Idle;
        }

        /// <summary>
        /// 进入 S_B.Done——单值赋值（互斥由类型系统保证）
        /// </summary>
        private void S_B_Enter_Done()
        {
            _S_B_State = S_B_State.Done;
        }

        /// <summary>
        /// 进入 S_B.Failed——单值赋值（互斥由类型系统保证）
        /// </summary>
        private void S_B_Enter_Failed()
        {
            _S_B_State = S_B_State.Failed;
        }

        /// <summary>
        /// S_A 是否处于 Idle
        /// </summary>
        public bool IsAIdle() { return _S_A_State == S_A_State.Idle; }

        /// <summary>
        /// S_A 是否处于 Done
        /// </summary>
        public bool IsADone() { return _S_A_State == S_A_State.Done; }

        /// <summary>
        /// S_A 是否处于 Failed
        /// </summary>
        public bool IsAFailed() { return _S_A_State == S_A_State.Failed; }

        /// <summary>
        /// S_B 是否处于 Idle
        /// </summary>
        public bool IsBIdle() { return _S_B_State == S_B_State.Idle; }

        /// <summary>
        /// S_B 是否处于 Done
        /// </summary>
        public bool IsBDone() { return _S_B_State == S_B_State.Done; }

        /// <summary>
        /// S_B 是否处于 Failed
        /// </summary>
        public bool IsBFailed() { return _S_B_State == S_B_State.Failed; }

        /// <summary>
        /// S_A 当前状态名
        /// </summary>
        public string GetAState()
        {
            switch (_S_A_State)
            {
                case S_A_State.Idle: return "Idle";
                case S_A_State.Done: return "Done";
                case S_A_State.Failed: return "Failed";
                default: return "Unknown";
            }
        }

        /// <summary>
        /// S_B 当前状态名
        /// </summary>
        public string GetBState()
        {
            switch (_S_B_State)
            {
                case S_B_State.Idle: return "Idle";
                case S_B_State.Done: return "Done";
                case S_B_State.Failed: return "Failed";
                default: return "Unknown";
            }
        }

        /// <summary>
        /// 帧驱动——测量采样 + 控制律守卫（声明顺序）
        /// </summary>
        /// <param name="frame">全局帧号</param>
        public void Tick(int frame)
        {
            T_A_Execute(frame);
            T_B_Execute(frame);
        }

        /// <summary>
        /// T_A 控制律——条件 + 操作 → 结果
        /// </summary>
        /// <param name="frame">全局帧号</param>
        private void T_A_Execute(int frame)
        {
            // [段1] 条件守卫——全部成立 → 触发
            if ((P_Go) && (R_Parallel_Count > 0) && T_A_Cube.IsIdle())
            {
                // [段2] 信号消费——触发即清除
                P_Go = false;
                // [段3] 资源获取——动作完成后释放
                R_Parallel_Count = R_Parallel_Count - 1;
                T_A_Cube.Start();
                // [段4] 操作——顺序执行 + try-catch 隔离 + 自动审计
                bool ok = false;
                try
                {
                    AuditBrick("invoke", "T_A", "file.convert", frame);
                    ok = Mau.Bricks.BRIK_FILE_001.Convert(_src1, _dst1);
                    AuditBrick("ok", "T_A", "file.convert", frame);
                }
                catch (Exception ex)
                {
                    ok = false;
                    AuditBrick("error", "T_A", "file.convert", frame);
                }
                // [段5] 结果转移——bool 驱动首项成功 / 次项失败 / 多路 switch 分发
                if (ok)
                {
                    S_A_Enter_Done();
                }
                else
                {
                    S_A_Enter_Failed();
                }
                // [段6] 资源释放
                R_Parallel_Count = R_Parallel_Count + 1;
                // [段6b] Cube 复位——触发完成（成功/失败均复位，允许再次触发）
                T_A_Cube.Complete();
            }
            // [段7] τ 时限——Cube 步进 + 耗尽 → 次项（超时走失败分支）
            else if ((P_Go) && (R_Parallel_Count > 0) && T_A_Cube.IsRunning())
            {
                T_A_Cube.TickFrame();
                if (T_A_Cube.IsExpired())
                {
                    S_A_Enter_Failed();
                    T_A_Cube.Reset();
                }
            }
        }

        /// <summary>
        /// T_B 控制律——条件 + 操作 → 结果
        /// </summary>
        /// <param name="frame">全局帧号</param>
        private void T_B_Execute(int frame)
        {
            // [段1] 条件守卫——全部成立 → 触发
            if ((P_Go) && (R_Parallel_Count > 0) && T_B_Cube.IsIdle())
            {
                // [段2] 信号消费——触发即清除
                P_Go = false;
                // [段3] 资源获取——动作完成后释放
                R_Parallel_Count = R_Parallel_Count - 1;
                T_B_Cube.Start();
                // [段4] 操作——顺序执行 + try-catch 隔离 + 自动审计
                bool ok = false;
                try
                {
                    AuditBrick("invoke", "T_B", "file.convert", frame);
                    ok = Mau.Bricks.BRIK_FILE_001.Convert(_src2, _dst2);
                    AuditBrick("ok", "T_B", "file.convert", frame);
                }
                catch (Exception ex)
                {
                    ok = false;
                    AuditBrick("error", "T_B", "file.convert", frame);
                }
                // [段5] 结果转移——bool 驱动首项成功 / 次项失败 / 多路 switch 分发
                if (ok)
                {
                    S_B_Enter_Done();
                }
                else
                {
                    S_B_Enter_Failed();
                }
                // [段6] 资源释放
                R_Parallel_Count = R_Parallel_Count + 1;
                // [段6b] Cube 复位——触发完成（成功/失败均复位，允许再次触发）
                T_B_Cube.Complete();
            }
            // [段7] τ 时限——Cube 步进 + 耗尽 → 次项（超时走失败分支）
            else if ((P_Go) && (R_Parallel_Count > 0) && T_B_Cube.IsRunning())
            {
                T_B_Cube.TickFrame();
                if (T_B_Cube.IsExpired())
                {
                    S_B_Enter_Failed();
                    T_B_Cube.Reset();
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
// #MAU_CHECKSUM:SHA256:3062A8B46E966A39D4D523A65E6F43C29FCF20B4E9104E44C072E3EA14F4FBD8
