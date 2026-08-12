// ═══ S01MeasureCondition 生成物 — Mau v2.0 翻译器 ═══
// 生成物由翻译器确定性输出——手工修改无效，改 .mau 后重新生成
using System;
using System.Collections.Generic;
using Mau.Runtime;

namespace Mau.Generated
{
    /// <summary>
    /// 生成物——S01MeasureCondition 受控系统
    /// </summary>
    public sealed class S01MeasureCondition
    {
        private enum S_Run_State { Idle, Done, Failed }
        private S_Run_State _S_Run_State;

        // [命题]
        private bool P_Go;
        private bool P_Ready;

        // [测量帧计数]
        private int M_Poll_FrameCounter;

        // [控制律 Cube]
        private readonly Cube T_Run_Cube = new Cube(5);

        /// <summary>
        /// 构造——初始状态置位
        /// </summary>
        public S01MeasureCondition()
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
        /// M_Poll 采样——每帧写入 P_Ready
        /// </summary>
        private void M_Poll_Sample()
        {
            bool result = Mau.Bricks.BRIK_DATA_007.Is(_key, _flag, 1);
            P_Ready = result;
            M_Poll_FrameCounter = M_Poll_FrameCounter + 1;
        }

        /// <summary>
        /// 帧驱动——测量采样 + 控制律守卫（声明顺序）
        /// </summary>
        /// <param name="frame">全局帧号</param>
        public void Tick(int frame)
        {
            M_Poll_Sample();
            T_Run_Execute(frame);
        }

        /// <summary>
        /// T_Run 控制律——条件 + 操作 → 结果
        /// </summary>
        /// <param name="frame">全局帧号</param>
        private void T_Run_Execute(int frame)
        {
            // [段1] 条件守卫——全部成立 → 触发
            if ((P_Go) && (P_Ready) && T_Run_Cube.IsIdle())
            {
                // [段2] 信号消费——触发即清除
                P_Go = false;
                T_Run_Cube.Start();
                // [段4] 操作——顺序执行 + try-catch 隔离 + 自动审计
                bool ok = false;
                try
                {
                    AuditBrick("invoke", "T_Run", "file.convert", frame);
                    ok = Mau.Bricks.BRIK_FILE_001.Convert(_src, _dst);
                    AuditBrick("ok", "T_Run", "file.convert", frame);
                }
                catch (Exception ex)
                {
                    ok = false;
                    AuditBrick("error", "T_Run", "file.convert", frame);
                }
                // [段5] 结果转移——bool 驱动首项成功 / 次项失败 / 多路 switch 分发
                if (ok)
                {
                    S_Run_Enter_Done();
                }
                else
                {
                    S_Run_Enter_Failed();
                }
            }
            // [段7] τ 时限——Cube 步进 + 耗尽 → 次项（超时走失败分支）
            else if ((P_Go) && (P_Ready) && T_Run_Cube.IsRunning())
            {
                T_Run_Cube.TickFrame();
                if (T_Run_Cube.IsExpired())
                {
                    S_Run_Enter_Failed();
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
// #MAU_CHECKSUM:SHA256:2929E59920D9126EE3B4B96D76E6045FF216EE4BABAEAD490741D32013F5D503
