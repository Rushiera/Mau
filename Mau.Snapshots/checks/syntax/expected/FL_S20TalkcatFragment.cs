// ═══ S20TalkcatFragment 生成物 — Mau v2.0 翻译器 ═══
// 生成物由翻译器确定性输出——手工修改无效，改 .mau 后重新生成
using System;
using System.Collections.Generic;
using Mau.Runtime;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Mau.Generated
{
    /// <summary>
    /// 生成物——S20TalkcatFragment 受控系统
    /// </summary>
    public sealed class S20TalkcatFragment
    {
        private enum S_Talk_State { Idle, Building, Done, Failed, Active }
        private S_Talk_State _S_Talk_State;

        // [嵌套子机 S_Active——None = 未激活]
        private enum S_Active_State { None, Thinking, Streaming, ToolWait }
        private S_Active_State _S_Active_State;

        // [命题]
        private bool P_Init;
        private bool P_ChunkReady;

        // [积木输出端口]
        private string _requestId = default;
        private bool _ended = default;
        private string _errorCode = default;
        private bool _isTool = default;
        private string _toolCallsJson = default;
        private string _contentDelta = default;
        private string _reasoningDelta = default;
        private bool _finished = default;

        // [测量帧计数]
        private int M_Pump_FrameCounter;

        // [控制律 Cube]
        private readonly Cube T_Init_Cube = new Cube(5);
        private readonly Cube T_Start_Cube = new Cube(10);
        private readonly Cube T_Classify_Cube = new Cube(5);
        private readonly Cube T_CheckTool_Cube = new Cube(5);
        private readonly Cube T_Finish_Cube = new Cube(5);

        /// <summary>
        /// 构造——初始状态置位
        /// </summary>
        public S20TalkcatFragment()
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
        /// 进入 S_Talk.Building——单值赋值（互斥由类型系统保证）
        /// </summary>
        private void S_Talk_Enter_Building()
        {
            _S_Talk_State = S_Talk_State.Building;
            _S_Active_State = S_Active_State.None;
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
        /// 进入 S_Talk.Active——单值赋值（互斥由类型系统保证）
        /// </summary>
        private void S_Talk_Enter_Active()
        {
            _S_Talk_State = S_Talk_State.Active;
            S_Active_Enter_Thinking();
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
        /// 进入 S_Active.ToolWait——单值赋值（互斥由类型系统保证）
        /// </summary>
        private void S_Active_Enter_ToolWait()
        {
            if (_S_Talk_State != S_Talk_State.Active)
            {
                _S_Talk_State = S_Talk_State.Active;
            }
            _S_Active_State = S_Active_State.ToolWait;
        }

        /// <summary>
        /// S_Talk 是否处于 Idle
        /// </summary>
        public bool IsTalkIdle() { return _S_Talk_State == S_Talk_State.Idle; }

        /// <summary>
        /// S_Talk 是否处于 Building
        /// </summary>
        public bool IsTalkBuilding() { return _S_Talk_State == S_Talk_State.Building; }

        /// <summary>
        /// S_Talk 是否处于 Done
        /// </summary>
        public bool IsTalkDone() { return _S_Talk_State == S_Talk_State.Done; }

        /// <summary>
        /// S_Talk 是否处于 Failed
        /// </summary>
        public bool IsTalkFailed() { return _S_Talk_State == S_Talk_State.Failed; }

        /// <summary>
        /// S_Talk 是否处于 Active
        /// </summary>
        public bool IsTalkActive() { return _S_Talk_State == S_Talk_State.Active; }

        /// <summary>
        /// S_Active 是否处于 Thinking
        /// </summary>
        public bool IsActiveThinking() { return _S_Active_State == S_Active_State.Thinking; }

        /// <summary>
        /// S_Active 是否处于 Streaming
        /// </summary>
        public bool IsActiveStreaming() { return _S_Active_State == S_Active_State.Streaming; }

        /// <summary>
        /// S_Active 是否处于 ToolWait
        /// </summary>
        public bool IsActiveToolWait() { return _S_Active_State == S_Active_State.ToolWait; }

        /// <summary>
        /// S_Talk 当前状态名
        /// </summary>
        public string GetTalkState()
        {
            switch (_S_Talk_State)
            {
                case S_Talk_State.Idle: return "Idle";
                case S_Talk_State.Building: return "Building";
                case S_Talk_State.Done: return "Done";
                case S_Talk_State.Failed: return "Failed";
                case S_Talk_State.Active: return "Active";
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
        /// M_Pump 采样——每帧写入 P_ChunkReady
        /// </summary>
        private void M_Pump_Sample()
        {
            bool result = Mau.Bricks.BRIK_LLM_020.ReadChunk(_requestId, out _contentDelta, out _reasoningDelta, out _toolCallsJson, out _finished, out _errorCode);
            P_ChunkReady = result;
            M_Pump_FrameCounter = M_Pump_FrameCounter + 1;
        }

        /// <summary>
        /// 帧驱动——测量采样 + 控制律守卫（声明顺序）
        /// </summary>
        /// <param name="frame">全局帧号</param>
        public void Tick(int frame)
        {
            M_Pump_Sample();
            T_Init_Execute(frame);
            T_Start_Execute(frame);
            T_Classify_Execute(frame);
            T_CheckTool_Execute(frame);
            T_Finish_Execute(frame);
        }

        /// <summary>
        /// T_Init 控制律——条件 + 操作 → 结果
        /// </summary>
        /// <param name="frame">全局帧号</param>
        private void T_Init_Execute(int frame)
        {
            // [段1] 条件守卫——全部成立 → 触发
            if (P_Init && T_Init_Cube.IsIdle())
            {
                // [段2] 信号消费——触发即清除
                P_Init = false;
                T_Init_Cube.Start();
                // [段4] 操作——顺序执行 + try-catch 隔离 + 自动审计
                bool ok = false;
                try
                {
                    AuditBrick("invoke", "T_Init", "llm.ctx_set_system", frame);
                    ok = Mau.Bricks.BRIK_LLM_003.CtxSetSystem(_sessionKey, _prompt);
                    AuditBrick("ok", "T_Init", "llm.ctx_set_system", frame);
                }
                catch (Exception ex)
                {
                    ok = false;
                    AuditBrick("error", "T_Init", "llm.ctx_set_system", frame);
                }
                // [段5] 结果转移——bool 驱动首项成功 / 次项失败 / 多路 switch 分发
                if (ok)
                {
                    S_Talk_Enter_Building();
                }
                else
                {
                    S_Talk_Enter_Failed();
                }
            }
            // [段7] τ 时限——Cube 步进 + 耗尽 → 次项（超时走失败分支）
            else if (P_Init && T_Init_Cube.IsRunning())
            {
                T_Init_Cube.TickFrame();
                if (T_Init_Cube.IsExpired())
                {
                    S_Talk_Enter_Failed();
                }
            }
        }

        /// <summary>
        /// T_Start 控制律——条件 + 操作 → 结果
        /// </summary>
        /// <param name="frame">全局帧号</param>
        private void T_Start_Execute(int frame)
        {
            // [段1] 条件守卫——全部成立 → 触发
            if (_S_Talk_State == S_Talk_State.Building && T_Start_Cube.IsIdle())
            {
                T_Start_Cube.Start();
                // [段4] 操作——顺序执行 + try-catch 隔离 + 自动审计
                bool ok = false;
                try
                {
                    AuditBrick("invoke", "T_Start", "llm.completions", frame);
                    ok = Mau.Bricks.BRIK_LLM_013.Completions(_model, _messagesJson, _toolsJson, out _requestId);
                    AuditBrick("ok", "T_Start", "llm.completions", frame);
                }
                catch (Exception ex)
                {
                    ok = false;
                    AuditBrick("error", "T_Start", "llm.completions", frame);
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
            }
            // [段7] τ 时限——Cube 步进 + 耗尽 → 次项（超时走失败分支）
            else if (_S_Talk_State == S_Talk_State.Building && T_Start_Cube.IsRunning())
            {
                T_Start_Cube.TickFrame();
                if (T_Start_Cube.IsExpired())
                {
                    S_Talk_Enter_Failed();
                }
            }
        }

        /// <summary>
        /// T_Classify 控制律——条件 + 操作 → 结果
        /// </summary>
        /// <param name="frame">全局帧号</param>
        private void T_Classify_Execute(int frame)
        {
            // [段1] 条件守卫——全部成立 → 触发
            if (P_ChunkReady && T_Classify_Cube.IsIdle())
            {
                T_Classify_Cube.Start();
                // [段4] 操作——顺序执行 + try-catch 隔离 + 自动审计
                bool ok = false;
                try
                {
                    AuditBrick("invoke", "T_Classify", "llm.is_end", frame);
                    ok = Mau.Bricks.BRIK_LLM_014.IsEnd(_requestId, out _ended, out _errorCode);
                    AuditBrick("ok", "T_Classify", "llm.is_end", frame);
                }
                catch (Exception ex)
                {
                    ok = false;
                    AuditBrick("error", "T_Classify", "llm.is_end", frame);
                }
                // [段5] 结果转移——bool 驱动首项成功 / 次项失败 / 多路 switch 分发
                if (ok)
                {
                    S_Active_Enter_Streaming();
                }
                else
                {
                    S_Active_Enter_Thinking();
                }
            }
            // [段7] τ 时限——Cube 步进 + 耗尽 → 次项（超时走失败分支）
            else if (P_ChunkReady && T_Classify_Cube.IsRunning())
            {
                T_Classify_Cube.TickFrame();
                if (T_Classify_Cube.IsExpired())
                {
                    S_Active_Enter_Thinking();
                }
            }
        }

        /// <summary>
        /// T_CheckTool 控制律——条件 + 操作 → 结果
        /// </summary>
        /// <param name="frame">全局帧号</param>
        private void T_CheckTool_Execute(int frame)
        {
            // [段1] 条件守卫——全部成立 → 触发
            if (_S_Active_State == S_Active_State.Streaming && T_CheckTool_Cube.IsIdle())
            {
                T_CheckTool_Cube.Start();
                // [段4] 操作——顺序执行 + try-catch 隔离 + 自动审计
                bool ok = false;
                try
                {
                    AuditBrick("invoke", "T_CheckTool", "llm.is_tool", frame);
                    ok = Mau.Bricks.BRIK_LLM_015.IsTool(_requestId, out _isTool, out _toolCallsJson);
                    AuditBrick("ok", "T_CheckTool", "llm.is_tool", frame);
                }
                catch (Exception ex)
                {
                    ok = false;
                    AuditBrick("error", "T_CheckTool", "llm.is_tool", frame);
                }
                // [段5] 结果转移——bool 驱动首项成功 / 次项失败 / 多路 switch 分发
                if (ok)
                {
                    S_Active_Enter_ToolWait();
                }
                else
                {
                    S_Active_Enter_Thinking();
                }
            }
            // [段7] τ 时限——Cube 步进 + 耗尽 → 次项（超时走失败分支）
            else if (_S_Active_State == S_Active_State.Streaming && T_CheckTool_Cube.IsRunning())
            {
                T_CheckTool_Cube.TickFrame();
                if (T_CheckTool_Cube.IsExpired())
                {
                    S_Active_Enter_Thinking();
                }
            }
        }

        /// <summary>
        /// T_Finish 控制律——条件 + 操作 → 结果
        /// </summary>
        /// <param name="frame">全局帧号</param>
        private void T_Finish_Execute(int frame)
        {
            // [段1] 条件守卫——全部成立 → 触发
            if (_S_Active_State == S_Active_State.Streaming && T_Finish_Cube.IsIdle())
            {
                T_Finish_Cube.Start();
                // [段4] 操作——无（纯转移控制律）
                bool ok = true;
                // [段5] 结果转移——bool 驱动首项成功 / 次项失败 / 多路 switch 分发
                S_Talk_Enter_Done();
            }
            // [段7] τ 时限——Cube 步进 + 耗尽 → 次项（超时走失败分支）
            else if (_S_Active_State == S_Active_State.Streaming && T_Finish_Cube.IsRunning())
            {
                T_Finish_Cube.TickFrame();
                if (T_Finish_Cube.IsExpired())
                {
                }
            }
        }

        /// <summary>
        /// 外部投递——P_Init
        /// </summary>
        public void FireInit()
        {
            P_Init = true;
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
// #MAU_CHECKSUM:SHA256:818B6EB7F4758894FD1CEA47659D8F8EDB0C55854D7FDF4EB1F14AD8E2390766
