// ═══ S16SugarFbk 生成物 — Mau v2.0 翻译器 ═══
// 生成物由翻译器确定性输出——手工修改无效，改 .mau 后重新生成
using System;
using System.Collections.Generic;
using Mau.Runtime;

namespace Mau.Generated
{
    /// <summary>
    /// 生成物——S16SugarFbk 受控系统
    /// </summary>
    public sealed class S16SugarFbk
    {
        private enum S_Work_State { Idle, Done }
        private S_Work_State _S_Work_State;

        private enum S_Fb_State { Waiting, Working }
        private S_Fb_State _S_Fb_State;

        // [命题]
        private bool P_Go;
        private bool P_Ready;

        // [测量帧计数]
        private int M_Poll_FrameCounter;

        // [控制律 Cube]
        private readonly Cube T_Work_Cube = new Cube(5);

        /// <summary>
        /// 构造——初始状态置位
        /// </summary>
        public S16SugarFbk()
        {
            S_Work_Enter_Idle();
            S_Fb_Enter_Waiting();
        }

        /// <summary>
        /// 进入 S_Work.Idle——单值赋值（互斥由类型系统保证）
        /// </summary>
        private void S_Work_Enter_Idle()
        {
            _S_Work_State = S_Work_State.Idle;
        }

        /// <summary>
        /// 进入 S_Work.Done——单值赋值（互斥由类型系统保证）
        /// </summary>
        private void S_Work_Enter_Done()
        {
            _S_Work_State = S_Work_State.Done;
        }

        /// <summary>
        /// 进入 S_Fb.Waiting——单值赋值（互斥由类型系统保证）
        /// </summary>
        private void S_Fb_Enter_Waiting()
        {
            _S_Fb_State = S_Fb_State.Waiting;
        }

        /// <summary>
        /// 进入 S_Fb.Working——单值赋值（互斥由类型系统保证）
        /// </summary>
        private void S_Fb_Enter_Working()
        {
            _S_Fb_State = S_Fb_State.Working;
        }

        /// <summary>
        /// S_Work 是否处于 Idle
        /// </summary>
        public bool IsWorkIdle() { return _S_Work_State == S_Work_State.Idle; }

        /// <summary>
        /// S_Work 是否处于 Done
        /// </summary>
        public bool IsWorkDone() { return _S_Work_State == S_Work_State.Done; }

        /// <summary>
        /// S_Fb 是否处于 Waiting
        /// </summary>
        public bool IsFbWaiting() { return _S_Fb_State == S_Fb_State.Waiting; }

        /// <summary>
        /// S_Fb 是否处于 Working
        /// </summary>
        public bool IsFbWorking() { return _S_Fb_State == S_Fb_State.Working; }

        /// <summary>
        /// S_Work 当前状态名
        /// </summary>
        public string GetWorkState()
        {
            switch (_S_Work_State)
            {
                case S_Work_State.Idle: return "Idle";
                case S_Work_State.Done: return "Done";
                default: return "Unknown";
            }
        }

        /// <summary>
        /// S_Fb 当前状态名
        /// </summary>
        public string GetFbState()
        {
            switch (_S_Fb_State)
            {
                case S_Fb_State.Waiting: return "Waiting";
                case S_Fb_State.Working: return "Working";
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
            T_Work_Execute(frame);
        }

        /// <summary>
        /// T_Work 控制律——条件 + 操作 → 结果
        /// </summary>
        /// <param name="frame">全局帧号</param>
        private void T_Work_Execute(int frame)
        {
            // [段1] 条件守卫——全部成立 → 触发
            if ((P_Ready) && (P_Go) && (_S_Fb_State == S_Fb_State.Waiting) && T_Work_Cube.IsIdle())
            {
                // [段2] 信号消费——触发即清除
                P_Go = false;
                T_Work_Cube.Start();
                // [段4] 操作——顺序执行 + try-catch 隔离 + 自动审计
                bool ok = false;
                try
                {
                    AuditBrick("invoke", "T_Work", "file.convert", frame);
                    ok = Mau.Bricks.BRIK_FILE_001.Convert(_src, _dst);
                    AuditBrick("ok", "T_Work", "file.convert", frame);
                }
                catch (Exception ex)
                {
                    ok = false;
                    AuditBrick("error", "T_Work", "file.convert", frame);
                }
                // [段5] 结果转移——bool 驱动首项成功 / 次项失败 / 多路 switch 分发
                if (ok)
                {
                    S_Work_Enter_Done();
                }
                else
                {
                    S_Fb_Enter_Working();
                }
            }
            // [段7] τ 时限——Cube 步进 + 耗尽 → 次项（超时走失败分支）
            else if ((P_Ready) && (P_Go) && (_S_Fb_State == S_Fb_State.Waiting) && T_Work_Cube.IsRunning())
            {
                T_Work_Cube.TickFrame();
                if (T_Work_Cube.IsExpired())
                {
                    S_Fb_Enter_Working();
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
// #MAU_CHECKSUM:SHA256:0C27462D653D31AA6C080F4A168CA000D3B668009B549FEB6B92F327A37B7670
