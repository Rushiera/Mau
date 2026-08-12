// ═══ S05NestedMachine 生成物 — Mau v2.0 翻译器 ═══
// 生成物由翻译器确定性输出——手工修改无效，改 .mau 后重新生成
using System;
using System.Collections.Generic;
using Mau.Runtime;

namespace Mau.Generated
{
    /// <summary>
    /// 生成物——S05NestedMachine 受控系统
    /// </summary>
    public sealed class S05NestedMachine
    {
        private enum S_Talk_State { Idle, Active, Done, Failed }
        private S_Talk_State _S_Talk_State;

        // [嵌套子机 S_Active——None = 未激活]
        private enum S_Active_State { None, Thinking, Streaming }
        private S_Active_State _S_Active_State;

        // [命题]
        private bool P_Go;
        private bool P_Chunk;

        // [测量帧计数]
        private int M_Poll_FrameCounter;

        // [控制律 Cube]
        private readonly Cube T_Go_Cube = new Cube(5);
        private readonly Cube T_Stream_Cube = new Cube(5);
        private readonly Cube T_Done_Cube = new Cube(5);

        /// <summary>
        /// 构造——初始状态置位
        /// </summary>
        public S05NestedMachine()
        {
            S_Talk_Enter_Idle();
            S_Active_Enter_Thinking();
        }

        /// <summary>
        /// 进入 S_Talk.Idle——单值赋值（互斥由类型系统保证）
        /// </summary>
        private void S_Talk_Enter_Idle()
        {
            _S_Talk_State = S_Talk_State.Idle;
            _S_Active_State = S_Active_State.None;
        }

        /// <summary>
        /// 进入 S_Talk.Active——单值赋值（互斥由类型系统保证）
        /// </summary>
        private void S_Talk_Enter_Active()
        {
            _S_Talk_State = S_Talk_State.Active;
            S_Active_Enter_Thinking();
        }

        /// <summary>
        /// 进入 S_Talk.Done——单值赋值（互斥由类型系统保证）
        /// </summary>
        private void S_Talk_Enter_Done()
        {
            _S_Talk_State = S_Talk_State.Done;
            _S_Active_State = S_Active_State.None;
        }

        /// <summary>
        /// 进入 S_Talk.Failed——单值赋值（互斥由类型系统保证）
        /// </summary>
        private void S_Talk_Enter_Failed()
        {
            _S_Talk_State = S_Talk_State.Failed;
            _S_Active_State = S_Active_State.None;
        }

        /// <summary>
        /// 进入 S_Active.Thinking——单值赋值（互斥由类型系统保证）
        /// </summary>
        private void S_Active_Enter_Thinking()
        {
            if (_S_Talk_State != S_Talk_State.Active)
            {
                _S_Talk_State = S_Talk_State.Active;
            }
            _S_Active_State = S_Active_State.Thinking;
        }

        /// <summary>
        /// 进入 S_Active.Streaming——单值赋值（互斥由类型系统保证）
        /// </summary>
        private void S_Active_Enter_Streaming()
        {
            if (_S_Talk_State != S_Talk_State.Active)
            {
                _S_Talk_State = S_Talk_State.Active;
            }
            _S_Active_State = S_Active_State.Streaming;
        }

        /// <summary>
        /// S_Talk 是否处于 Idle
        /// </summary>
        public bool IsTalkIdle() { return _S_Talk_State == S_Talk_State.Idle; }

        /// <summary>
        /// S_Talk 是否处于 Active
        /// </summary>
        public bool IsTalkActive() { return _S_Talk_State == S_Talk_State.Active; }

        /// <summary>
        /// S_Talk 是否处于 Done
        /// </summary>
        public bool IsTalkDone() { return _S_Talk_State == S_Talk_State.Done; }

        /// <summary>
        /// S_Talk 是否处于 Failed
        /// </summary>
        public bool IsTalkFailed() { return _S_Talk_State == S_Talk_State.Failed; }

        /// <summary>
        /// S_Active 是否处于 Thinking
        /// </summary>
        public bool IsActiveThinking() { return _S_Active_State == S_Active_State.Thinking; }

        /// <summary>
        /// S_Active 是否处于 Streaming
        /// </summary>
        public bool IsActiveStreaming() { return _S_Active_State == S_Active_State.Streaming; }

        /// <summary>
        /// S_Talk 当前状态名
        /// </summary>
        public string GetTalkState()
        {
            switch (_S_Talk_State)
            {
                case S_Talk_State.Idle: return "Idle";
                case S_Talk_State.Active: return "Active";
                case S_Talk_State.Done: return "Done";
                case S_Talk_State.Failed: return "Failed";
                default: return "Unknown";
            }
        }

        /// <summary>
        /// S_Talk 嵌套状态路径——'Parent.Child'
        /// </summary>
        public string GetTalkStatePath()
        {
            switch (_S_Talk_State)
            {
                case S_Talk_State.Active: return "Active." + _S_Active_State.ToString();
                default: return _S_Talk_State.ToString();
            }
        }

        /// <summary>
        /// M_Poll 采样——每帧写入 P_Chunk
        /// </summary>
        private void M_Poll_Sample()
        {
            bool result = Mau.Bricks.BRIK_DATA_007.Is(_key, _flag, 1);
            P_Chunk = result;
            M_Poll_FrameCounter = M_Poll_FrameCounter + 1;
        }

        /// <summary>
        /// 帧驱动——测量采样 + 控制律守卫（声明顺序）
        /// </summary>
        /// <param name="frame">全局帧号</param>
        public void Tick(int frame)
        {
            M_Poll_Sample();
            T_Go_Execute(frame);
            T_Stream_Execute(frame);
            T_Done_Execute(frame);
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
                    S_Talk_Enter_Active();
                }
                else
                {
                    S_Talk_Enter_Failed();
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
                    S_Talk_Enter_Failed();
                    T_Go_Cube.Reset();
                }
            }
        }

        /// <summary>
        /// T_Stream 控制律——条件 + 操作 → 结果
        /// </summary>
        /// <param name="frame">全局帧号</param>
        private void T_Stream_Execute(int frame)
        {
            // [段1] 条件守卫——全部成立 → 触发
            if ((_S_Active_State == S_Active_State.Thinking) && (P_Chunk) && T_Stream_Cube.IsIdle())
            {
                T_Stream_Cube.Start();
                // [段4] 操作——无（纯转移控制律）
                bool ok = true;
                // [段5] 结果转移——bool 驱动首项成功 / 次项失败 / 多路 switch 分发
                if (ok)
                {
                    S_Active_Enter_Streaming();
                }
                else
                {
                    S_Active_Enter_Thinking();
                }
                // [段6b] Cube 复位——触发完成（成功/失败均复位，允许再次触发）
                T_Stream_Cube.Complete();
            }
            // [段7] τ 时限——Cube 步进 + 耗尽 → 次项（超时走失败分支）
            else if ((_S_Active_State == S_Active_State.Thinking) && (P_Chunk) && T_Stream_Cube.IsRunning())
            {
                T_Stream_Cube.TickFrame();
                if (T_Stream_Cube.IsExpired())
                {
                    S_Active_Enter_Thinking();
                    T_Stream_Cube.Reset();
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
            if (_S_Active_State == S_Active_State.Streaming && T_Done_Cube.IsIdle())
            {
                T_Done_Cube.Start();
                // [段4] 操作——无（纯转移控制律）
                bool ok = true;
                // [段5] 结果转移——bool 驱动首项成功 / 次项失败 / 多路 switch 分发
                if (ok)
                {
                    S_Talk_Enter_Done();
                }
                else
                {
                    S_Talk_Enter_Failed();
                }
                // [段6b] Cube 复位——触发完成（成功/失败均复位，允许再次触发）
                T_Done_Cube.Complete();
            }
            // [段7] τ 时限——Cube 步进 + 耗尽 → 次项（超时走失败分支）
            else if (_S_Active_State == S_Active_State.Streaming && T_Done_Cube.IsRunning())
            {
                T_Done_Cube.TickFrame();
                if (T_Done_Cube.IsExpired())
                {
                    S_Talk_Enter_Failed();
                    T_Done_Cube.Reset();
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
// #MAU_CHECKSUM:SHA256:8882D0C99B6A73F18FB260927F185731E616F5B9AFF014276846C51B90F7E380
