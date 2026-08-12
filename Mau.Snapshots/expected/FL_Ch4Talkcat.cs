// ═══ Ch4Talkcat 生成物 — Mau v2.0 翻译器 ═══
// 生成物由翻译器确定性输出——手工修改无效，改 .mau 后重新生成
using System;
using System.Collections.Generic;
using Mau.Runtime;
using System.IO;
using System.Text;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;

namespace Mau.Generated
{
    /// <summary>
    /// 生成物——Ch4Talkcat 受控系统
    /// </summary>
    public sealed class Ch4Talkcat
    {
        // [注入字段]
        private string? _sessionKey;
        private string? _prompt;
        private long? _ownerId;

        /// <summary>
        /// 注入字段设置——sessionKey（纯赋值，不置位信号）
        /// </summary>
        /// <param name="value">注入值</param>
        public void SetSessionKey(string? value) { _sessionKey = value; }

        /// <summary>
        /// 注入字段设置——prompt（纯赋值，不置位信号）
        /// </summary>
        /// <param name="value">注入值</param>
        public void SetPrompt(string? value) { _prompt = value; }

        /// <summary>
        /// 注入字段设置——ownerId（纯赋值，不置位信号）
        /// </summary>
        /// <param name="value">注入值</param>
        public void SetOwnerId(long? value) { _ownerId = value; }

        private enum S_Talk_State { Idle, RouteCmd, Stop, Rollback, NewSession, UserMsg, PushUser, SaveUser, BuildMsg, BuildTools, Start, WaitTools, Done, Failed, Active }
        private S_Talk_State _S_Talk_State;

        // [嵌套子机 S_Active——None = 未激活]
        private enum S_Active_State { None, Thinking, Streaming, PushStream, PushReasoning, PushStats, PushStatsLine, ToolWait, ToolFlag, ErrorFinish, ErrorRecord }
        private S_Active_State _S_Active_State;

        // [命题]
        private bool P_Init;
        private bool P_ChunkReady;
        private bool P_Ended;
        private bool P_Error;
        private bool P_ToolsDone;

        // [资源]
        private int R_ReplySlot_Count = 1;

        // [积木输出端口]
        private bool _hasCommands = default;
        private string[] _cmdKeys = default;
        private int[] _cmdValues = default;
        private string[] _cmdTexts = default;
        private string _text = default;
        private int _checkpoint = default;
        private string _messagesJson = default;
        private string _toolsJson = default;
        private string _requestId = default;
        private bool _isTool = default;
        private string _toolCallsJson = default;
        private int _count = default;
        private string _contentDelta = default;
        private string _reasoningDelta = default;
        private bool _finished = default;
        private string _errorCode = default;
        private bool _ended = default;
        private bool _hasError = default;

        // [测量帧计数]
        private int M_Pump_FrameCounter;
        private int M_CheckEnd_FrameCounter;
        private int M_CheckError_FrameCounter;
        private int M_PollTools_FrameCounter;

        // [控制律 Cube]
        private readonly Cube T_Init_Cube = new Cube(5);
        private readonly Cube T_CheckCmd_Cube = new Cube(5);
        private readonly Cube T_RouteCmd_Cube = new Cube(5);
        private readonly Cube T_DoStop_Cube = new Cube(5);
        private readonly Cube T_DoRollback_Cube = new Cube(5);
        private readonly Cube T_DoNewSession_Cube = new Cube(5);
        private readonly Cube T_Checkpoint_Cube = new Cube(5);
        private readonly Cube T_PushUser_Cube = new Cube(5);
        private readonly Cube T_SaveUser_Cube = new Cube(5);
        private readonly Cube T_BuildMsg_Cube = new Cube(5);
        private readonly Cube T_BuildTools_Cube = new Cube(5);
        private readonly Cube T_Start_Cube = new Cube(10);
        private readonly Cube T_Classify_Cube = new Cube(5);
        private readonly Cube T_Error_Cube = new Cube(5);
        private readonly Cube T_ErrorRollback_Cube = new Cube(5);
        private readonly Cube T_ErrorRecord_Cube = new Cube(5);
        private readonly Cube T_CheckTool_Cube = new Cube(5);
        private readonly Cube T_PushToolReq_Cube = new Cube(5);
        private readonly Cube T_ToolFlag_Cube = new Cube(5);
        private readonly Cube T_ToolsDone_Cube = new Cube(5);
        private readonly Cube T_PushStream_Cube = new Cube(5);
        private readonly Cube T_PushReasoning_Cube = new Cube(5);
        private readonly Cube T_PushStats_Cube = new Cube(5);
        private readonly Cube T_PushStatsLine_Cube = new Cube(5);
        private readonly Cube T_Reset_Cube = new Cube(5);

        /// <summary>
        /// 构造——初始状态置位
        /// </summary>
        public Ch4Talkcat()
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
        /// 进入 S_Talk.RouteCmd——单值赋值（互斥由类型系统保证）
        /// </summary>
        private void S_Talk_Enter_RouteCmd()
        {
            _S_Talk_State = S_Talk_State.RouteCmd;
            _S_Active_State = S_Active_State.None;
        }

        /// <summary>
        /// 进入 S_Talk.Stop——单值赋值（互斥由类型系统保证）
        /// </summary>
        private void S_Talk_Enter_Stop()
        {
            _S_Talk_State = S_Talk_State.Stop;
            _S_Active_State = S_Active_State.None;
        }

        /// <summary>
        /// 进入 S_Talk.Rollback——单值赋值（互斥由类型系统保证）
        /// </summary>
        private void S_Talk_Enter_Rollback()
        {
            _S_Talk_State = S_Talk_State.Rollback;
            _S_Active_State = S_Active_State.None;
        }

        /// <summary>
        /// 进入 S_Talk.NewSession——单值赋值（互斥由类型系统保证）
        /// </summary>
        private void S_Talk_Enter_NewSession()
        {
            _S_Talk_State = S_Talk_State.NewSession;
            _S_Active_State = S_Active_State.None;
        }

        /// <summary>
        /// 进入 S_Talk.UserMsg——单值赋值（互斥由类型系统保证）
        /// </summary>
        private void S_Talk_Enter_UserMsg()
        {
            _S_Talk_State = S_Talk_State.UserMsg;
            _S_Active_State = S_Active_State.None;
        }

        /// <summary>
        /// 进入 S_Talk.PushUser——单值赋值（互斥由类型系统保证）
        /// </summary>
        private void S_Talk_Enter_PushUser()
        {
            _S_Talk_State = S_Talk_State.PushUser;
            _S_Active_State = S_Active_State.None;
        }

        /// <summary>
        /// 进入 S_Talk.SaveUser——单值赋值（互斥由类型系统保证）
        /// </summary>
        private void S_Talk_Enter_SaveUser()
        {
            _S_Talk_State = S_Talk_State.SaveUser;
            _S_Active_State = S_Active_State.None;
        }

        /// <summary>
        /// 进入 S_Talk.BuildMsg——单值赋值（互斥由类型系统保证）
        /// </summary>
        private void S_Talk_Enter_BuildMsg()
        {
            _S_Talk_State = S_Talk_State.BuildMsg;
            _S_Active_State = S_Active_State.None;
        }

        /// <summary>
        /// 进入 S_Talk.BuildTools——单值赋值（互斥由类型系统保证）
        /// </summary>
        private void S_Talk_Enter_BuildTools()
        {
            _S_Talk_State = S_Talk_State.BuildTools;
            _S_Active_State = S_Active_State.None;
        }

        /// <summary>
        /// 进入 S_Talk.Start——单值赋值（互斥由类型系统保证）
        /// </summary>
        private void S_Talk_Enter_Start()
        {
            _S_Talk_State = S_Talk_State.Start;
            _S_Active_State = S_Active_State.None;
        }

        /// <summary>
        /// 进入 S_Talk.WaitTools——单值赋值（互斥由类型系统保证）
        /// </summary>
        private void S_Talk_Enter_WaitTools()
        {
            _S_Talk_State = S_Talk_State.WaitTools;
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
        /// 进入 S_Active.PushStream——单值赋值（互斥由类型系统保证）
        /// </summary>
        private void S_Active_Enter_PushStream()
        {
            if (_S_Talk_State != S_Talk_State.Active)
            {
                _S_Talk_State = S_Talk_State.Active;
            }
            _S_Active_State = S_Active_State.PushStream;
        }

        /// <summary>
        /// 进入 S_Active.PushReasoning——单值赋值（互斥由类型系统保证）
        /// </summary>
        private void S_Active_Enter_PushReasoning()
        {
            if (_S_Talk_State != S_Talk_State.Active)
            {
                _S_Talk_State = S_Talk_State.Active;
            }
            _S_Active_State = S_Active_State.PushReasoning;
        }

        /// <summary>
        /// 进入 S_Active.PushStats——单值赋值（互斥由类型系统保证）
        /// </summary>
        private void S_Active_Enter_PushStats()
        {
            if (_S_Talk_State != S_Talk_State.Active)
            {
                _S_Talk_State = S_Talk_State.Active;
            }
            _S_Active_State = S_Active_State.PushStats;
        }

        /// <summary>
        /// 进入 S_Active.PushStatsLine——单值赋值（互斥由类型系统保证）
        /// </summary>
        private void S_Active_Enter_PushStatsLine()
        {
            if (_S_Talk_State != S_Talk_State.Active)
            {
                _S_Talk_State = S_Talk_State.Active;
            }
            _S_Active_State = S_Active_State.PushStatsLine;
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
        /// 进入 S_Active.ToolFlag——单值赋值（互斥由类型系统保证）
        /// </summary>
        private void S_Active_Enter_ToolFlag()
        {
            if (_S_Talk_State != S_Talk_State.Active)
            {
                _S_Talk_State = S_Talk_State.Active;
            }
            _S_Active_State = S_Active_State.ToolFlag;
        }

        /// <summary>
        /// 进入 S_Active.ErrorFinish——单值赋值（互斥由类型系统保证）
        /// </summary>
        private void S_Active_Enter_ErrorFinish()
        {
            if (_S_Talk_State != S_Talk_State.Active)
            {
                _S_Talk_State = S_Talk_State.Active;
            }
            _S_Active_State = S_Active_State.ErrorFinish;
        }

        /// <summary>
        /// 进入 S_Active.ErrorRecord——单值赋值（互斥由类型系统保证）
        /// </summary>
        private void S_Active_Enter_ErrorRecord()
        {
            if (_S_Talk_State != S_Talk_State.Active)
            {
                _S_Talk_State = S_Talk_State.Active;
            }
            _S_Active_State = S_Active_State.ErrorRecord;
        }

        /// <summary>
        /// S_Talk 是否处于 Idle
        /// </summary>
        public bool IsTalkIdle() { return _S_Talk_State == S_Talk_State.Idle; }

        /// <summary>
        /// S_Talk 是否处于 RouteCmd
        /// </summary>
        public bool IsTalkRouteCmd() { return _S_Talk_State == S_Talk_State.RouteCmd; }

        /// <summary>
        /// S_Talk 是否处于 Stop
        /// </summary>
        public bool IsTalkStop() { return _S_Talk_State == S_Talk_State.Stop; }

        /// <summary>
        /// S_Talk 是否处于 Rollback
        /// </summary>
        public bool IsTalkRollback() { return _S_Talk_State == S_Talk_State.Rollback; }

        /// <summary>
        /// S_Talk 是否处于 NewSession
        /// </summary>
        public bool IsTalkNewSession() { return _S_Talk_State == S_Talk_State.NewSession; }

        /// <summary>
        /// S_Talk 是否处于 UserMsg
        /// </summary>
        public bool IsTalkUserMsg() { return _S_Talk_State == S_Talk_State.UserMsg; }

        /// <summary>
        /// S_Talk 是否处于 PushUser
        /// </summary>
        public bool IsTalkPushUser() { return _S_Talk_State == S_Talk_State.PushUser; }

        /// <summary>
        /// S_Talk 是否处于 SaveUser
        /// </summary>
        public bool IsTalkSaveUser() { return _S_Talk_State == S_Talk_State.SaveUser; }

        /// <summary>
        /// S_Talk 是否处于 BuildMsg
        /// </summary>
        public bool IsTalkBuildMsg() { return _S_Talk_State == S_Talk_State.BuildMsg; }

        /// <summary>
        /// S_Talk 是否处于 BuildTools
        /// </summary>
        public bool IsTalkBuildTools() { return _S_Talk_State == S_Talk_State.BuildTools; }

        /// <summary>
        /// S_Talk 是否处于 Start
        /// </summary>
        public bool IsTalkStart() { return _S_Talk_State == S_Talk_State.Start; }

        /// <summary>
        /// S_Talk 是否处于 WaitTools
        /// </summary>
        public bool IsTalkWaitTools() { return _S_Talk_State == S_Talk_State.WaitTools; }

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
        /// S_Active 是否处于 PushStream
        /// </summary>
        public bool IsActivePushStream() { return _S_Active_State == S_Active_State.PushStream; }

        /// <summary>
        /// S_Active 是否处于 PushReasoning
        /// </summary>
        public bool IsActivePushReasoning() { return _S_Active_State == S_Active_State.PushReasoning; }

        /// <summary>
        /// S_Active 是否处于 PushStats
        /// </summary>
        public bool IsActivePushStats() { return _S_Active_State == S_Active_State.PushStats; }

        /// <summary>
        /// S_Active 是否处于 PushStatsLine
        /// </summary>
        public bool IsActivePushStatsLine() { return _S_Active_State == S_Active_State.PushStatsLine; }

        /// <summary>
        /// S_Active 是否处于 ToolWait
        /// </summary>
        public bool IsActiveToolWait() { return _S_Active_State == S_Active_State.ToolWait; }

        /// <summary>
        /// S_Active 是否处于 ToolFlag
        /// </summary>
        public bool IsActiveToolFlag() { return _S_Active_State == S_Active_State.ToolFlag; }

        /// <summary>
        /// S_Active 是否处于 ErrorFinish
        /// </summary>
        public bool IsActiveErrorFinish() { return _S_Active_State == S_Active_State.ErrorFinish; }

        /// <summary>
        /// S_Active 是否处于 ErrorRecord
        /// </summary>
        public bool IsActiveErrorRecord() { return _S_Active_State == S_Active_State.ErrorRecord; }

        /// <summary>
        /// S_Talk 当前状态名
        /// </summary>
        public string GetTalkState()
        {
            switch (_S_Talk_State)
            {
                case S_Talk_State.Idle: return "Idle";
                case S_Talk_State.RouteCmd: return "RouteCmd";
                case S_Talk_State.Stop: return "Stop";
                case S_Talk_State.Rollback: return "Rollback";
                case S_Talk_State.NewSession: return "NewSession";
                case S_Talk_State.UserMsg: return "UserMsg";
                case S_Talk_State.PushUser: return "PushUser";
                case S_Talk_State.SaveUser: return "SaveUser";
                case S_Talk_State.BuildMsg: return "BuildMsg";
                case S_Talk_State.BuildTools: return "BuildTools";
                case S_Talk_State.Start: return "Start";
                case S_Talk_State.WaitTools: return "WaitTools";
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
        /// M_CheckEnd 采样——每帧写入 P_Ended
        /// </summary>
        private void M_CheckEnd_Sample()
        {
            bool result = Mau.Bricks.BRIK_LLM_014.IsEnd(_requestId, out _ended, out _errorCode);
            P_Ended = result;
            M_CheckEnd_FrameCounter = M_CheckEnd_FrameCounter + 1;
        }

        /// <summary>
        /// M_CheckError 采样——每帧写入 P_Error
        /// </summary>
        private void M_CheckError_Sample()
        {
            bool result = Mau.Bricks.BRIK_LLM_016.HasError(_requestId, out _hasError, out _errorCode);
            P_Error = result;
            M_CheckError_FrameCounter = M_CheckError_FrameCounter + 1;
        }

        /// <summary>
        /// M_PollTools 采样——每帧写入 P_ToolsDone
        /// </summary>
        private void M_PollTools_Sample()
        {
            bool result = Mau.Bricks.BRIK_DATA_007.Is(_sessionKey, "tools_done", 1);
            P_ToolsDone = result;
            M_PollTools_FrameCounter = M_PollTools_FrameCounter + 1;
        }

        /// <summary>
        /// 帧驱动——测量采样 + 控制律守卫（声明顺序）
        /// </summary>
        /// <param name="frame">全局帧号</param>
        public void Tick(int frame)
        {
            M_Pump_Sample();
            M_CheckEnd_Sample();
            M_CheckError_Sample();
            M_PollTools_Sample();
            T_Init_Execute(frame);
            T_CheckCmd_Execute(frame);
            T_RouteCmd_Execute(frame);
            T_DoStop_Execute(frame);
            T_DoRollback_Execute(frame);
            T_DoNewSession_Execute(frame);
            T_Checkpoint_Execute(frame);
            T_PushUser_Execute(frame);
            T_SaveUser_Execute(frame);
            T_BuildMsg_Execute(frame);
            T_BuildTools_Execute(frame);
            T_Start_Execute(frame);
            T_Classify_Execute(frame);
            T_Error_Execute(frame);
            T_ErrorRollback_Execute(frame);
            T_ErrorRecord_Execute(frame);
            T_CheckTool_Execute(frame);
            T_PushToolReq_Execute(frame);
            T_ToolFlag_Execute(frame);
            T_ToolsDone_Execute(frame);
            T_PushStream_Execute(frame);
            T_PushReasoning_Execute(frame);
            T_PushStats_Execute(frame);
            T_PushStatsLine_Execute(frame);
            T_Reset_Execute(frame);
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
                    S_Talk_Enter_Idle();
                }
                else
                {
                    S_Talk_Enter_Failed();
                }
                // [段6b] Cube 复位——触发完成（成功/失败均复位，允许再次触发）
                T_Init_Cube.Complete();
            }
            // [段7] τ 时限——Cube 步进 + 耗尽 → 次项（超时走失败分支）
            else if (P_Init && T_Init_Cube.IsRunning())
            {
                T_Init_Cube.TickFrame();
                if (T_Init_Cube.IsExpired())
                {
                    S_Talk_Enter_Failed();
                    T_Init_Cube.Reset();
                }
            }
        }

        /// <summary>
        /// T_CheckCmd 控制律——条件 + 操作 → 结果
        /// </summary>
        /// <param name="frame">全局帧号</param>
        private void T_CheckCmd_Execute(int frame)
        {
            // [段1] 条件守卫——全部成立 → 触发
            if (_S_Talk_State == S_Talk_State.Idle && T_CheckCmd_Cube.IsIdle())
            {
                T_CheckCmd_Cube.Start();
                // [段4] 操作——顺序执行 + try-catch 隔离 + 自动审计
                bool ok = false;
                try
                {
                    AuditBrick("invoke", "T_CheckCmd", "cmd.consume", frame);
                    ok = Mau.Bricks.BRIK_CMD_003.Consume((_ownerId ?? 0), out _hasCommands, out _cmdKeys, out _cmdValues, out _cmdTexts, out _text);
                    AuditBrick("ok", "T_CheckCmd", "cmd.consume", frame);
                }
                catch (Exception ex)
                {
                    ok = false;
                    AuditBrick("error", "T_CheckCmd", "cmd.consume", frame);
                }
                // [段5] 结果转移——bool 驱动首项成功 / 次项失败 / 多路 switch 分发
                if (ok)
                {
                    S_Talk_Enter_RouteCmd();
                }
                else
                {
                    S_Talk_Enter_Idle();
                }
                // [段6b] Cube 复位——触发完成（成功/失败均复位，允许再次触发）
                T_CheckCmd_Cube.Complete();
            }
            // [段7] τ 时限——Cube 步进 + 耗尽 → 次项（超时走失败分支）
            else if (_S_Talk_State == S_Talk_State.Idle && T_CheckCmd_Cube.IsRunning())
            {
                T_CheckCmd_Cube.TickFrame();
                if (T_CheckCmd_Cube.IsExpired())
                {
                    S_Talk_Enter_Idle();
                    T_CheckCmd_Cube.Reset();
                }
            }
        }

        /// <summary>
        /// T_RouteCmd 控制律——条件 + 操作 → 结果
        /// </summary>
        /// <param name="frame">全局帧号</param>
        private void T_RouteCmd_Execute(int frame)
        {
            // [段1] 条件守卫——全部成立 → 触发
            if (_S_Talk_State == S_Talk_State.RouteCmd && T_RouteCmd_Cube.IsIdle())
            {
                T_RouteCmd_Cube.Start();
                // [段4] 操作——名称返回积木（多路匹配，switch 分发源）
                string matched = "";
                bool ok = false;
                try
                {
                    AuditBrick("invoke", "T_RouteCmd", "cmd.match", frame);
                    matched = Mau.Bricks.BRIK_CMD_009.Match(_text, new string[] { "stop", "rollback", "newsession" });
                    ok = matched.Length > 0;
                    AuditBrick("ok", "T_RouteCmd", "cmd.match", frame);
                }
                catch (Exception ex)
                {
                    ok = false;
                    AuditBrick("error", "T_RouteCmd", "cmd.match", frame);
                }
                // [段5] 结果转移——bool 驱动首项成功 / 次项失败 / 多路 switch 分发
                switch (matched)
                {
                    case "stop": S_Talk_Enter_Stop(); break;
                    case "rollback": S_Talk_Enter_Rollback(); break;
                    case "newsession": S_Talk_Enter_NewSession(); break;
                    default: S_Talk_Enter_UserMsg(); break;
                }
                // [段6b] Cube 复位——触发完成（成功/失败均复位，允许再次触发）
                T_RouteCmd_Cube.Complete();
            }
            // [段7] τ 时限——Cube 步进 + 耗尽 → 次项（超时走失败分支）
            else if (_S_Talk_State == S_Talk_State.RouteCmd && T_RouteCmd_Cube.IsRunning())
            {
                T_RouteCmd_Cube.TickFrame();
                if (T_RouteCmd_Cube.IsExpired())
                {
                    S_Talk_Enter_Rollback();
                    T_RouteCmd_Cube.Reset();
                }
            }
        }

        /// <summary>
        /// T_DoStop 控制律——条件 + 操作 → 结果
        /// </summary>
        /// <param name="frame">全局帧号</param>
        private void T_DoStop_Execute(int frame)
        {
            // [段1] 条件守卫——全部成立 → 触发
            if (_S_Talk_State == S_Talk_State.Stop && T_DoStop_Cube.IsIdle())
            {
                T_DoStop_Cube.Start();
                // [段4] 操作——顺序执行 + try-catch 隔离 + 自动审计
                bool ok = false;
                try
                {
                    AuditBrick("invoke", "T_DoStop", "llm.finish", frame);
                    ok = Mau.Bricks.BRIK_LLM_021.Finish(_requestId);
                    AuditBrick("ok", "T_DoStop", "llm.finish", frame);
                }
                catch (Exception ex)
                {
                    ok = false;
                    AuditBrick("error", "T_DoStop", "llm.finish", frame);
                }
                // [段5] 结果转移——bool 驱动首项成功 / 次项失败 / 多路 switch 分发
                if (ok)
                {
                    S_Talk_Enter_Idle();
                }
                else
                {
                    S_Talk_Enter_Failed();
                }
                // [段6b] Cube 复位——触发完成（成功/失败均复位，允许再次触发）
                T_DoStop_Cube.Complete();
            }
            // [段7] τ 时限——Cube 步进 + 耗尽 → 次项（超时走失败分支）
            else if (_S_Talk_State == S_Talk_State.Stop && T_DoStop_Cube.IsRunning())
            {
                T_DoStop_Cube.TickFrame();
                if (T_DoStop_Cube.IsExpired())
                {
                    S_Talk_Enter_Failed();
                    T_DoStop_Cube.Reset();
                }
            }
        }

        /// <summary>
        /// T_DoRollback 控制律——条件 + 操作 → 结果
        /// </summary>
        /// <param name="frame">全局帧号</param>
        private void T_DoRollback_Execute(int frame)
        {
            // [段1] 条件守卫——全部成立 → 触发
            if (_S_Talk_State == S_Talk_State.Rollback && T_DoRollback_Cube.IsIdle())
            {
                T_DoRollback_Cube.Start();
                // [段4] 操作——顺序执行 + try-catch 隔离 + 自动审计
                bool ok = false;
                try
                {
                    AuditBrick("invoke", "T_DoRollback", "llm.ctx_rollback", frame);
                    ok = Mau.Bricks.BRIK_LLM_018.CtxRollback(_sessionKey, _checkpoint);
                    AuditBrick("ok", "T_DoRollback", "llm.ctx_rollback", frame);
                }
                catch (Exception ex)
                {
                    ok = false;
                    AuditBrick("error", "T_DoRollback", "llm.ctx_rollback", frame);
                }
                // [段5] 结果转移——bool 驱动首项成功 / 次项失败 / 多路 switch 分发
                if (ok)
                {
                    S_Talk_Enter_Idle();
                }
                else
                {
                    S_Talk_Enter_Failed();
                }
                // [段6b] Cube 复位——触发完成（成功/失败均复位，允许再次触发）
                T_DoRollback_Cube.Complete();
            }
            // [段7] τ 时限——Cube 步进 + 耗尽 → 次项（超时走失败分支）
            else if (_S_Talk_State == S_Talk_State.Rollback && T_DoRollback_Cube.IsRunning())
            {
                T_DoRollback_Cube.TickFrame();
                if (T_DoRollback_Cube.IsExpired())
                {
                    S_Talk_Enter_Failed();
                    T_DoRollback_Cube.Reset();
                }
            }
        }

        /// <summary>
        /// T_DoNewSession 控制律——条件 + 操作 → 结果
        /// </summary>
        /// <param name="frame">全局帧号</param>
        private void T_DoNewSession_Execute(int frame)
        {
            // [段1] 条件守卫——全部成立 → 触发
            if (_S_Talk_State == S_Talk_State.NewSession && T_DoNewSession_Cube.IsIdle())
            {
                T_DoNewSession_Cube.Start();
                // [段4] 操作——顺序执行 + try-catch 隔离 + 自动审计
                bool ok = false;
                try
                {
                    AuditBrick("invoke", "T_DoNewSession", "llm.ctx_clear", frame);
                    ok = Mau.Bricks.BRIK_LLM_009.CtxClear(_sessionKey);
                    AuditBrick("ok", "T_DoNewSession", "llm.ctx_clear", frame);
                }
                catch (Exception ex)
                {
                    ok = false;
                    AuditBrick("error", "T_DoNewSession", "llm.ctx_clear", frame);
                }
                // [段5] 结果转移——bool 驱动首项成功 / 次项失败 / 多路 switch 分发
                if (ok)
                {
                    S_Talk_Enter_Idle();
                }
                else
                {
                    S_Talk_Enter_Failed();
                }
                // [段6b] Cube 复位——触发完成（成功/失败均复位，允许再次触发）
                T_DoNewSession_Cube.Complete();
            }
            // [段7] τ 时限——Cube 步进 + 耗尽 → 次项（超时走失败分支）
            else if (_S_Talk_State == S_Talk_State.NewSession && T_DoNewSession_Cube.IsRunning())
            {
                T_DoNewSession_Cube.TickFrame();
                if (T_DoNewSession_Cube.IsExpired())
                {
                    S_Talk_Enter_Failed();
                    T_DoNewSession_Cube.Reset();
                }
            }
        }

        /// <summary>
        /// T_Checkpoint 控制律——条件 + 操作 → 结果
        /// </summary>
        /// <param name="frame">全局帧号</param>
        private void T_Checkpoint_Execute(int frame)
        {
            // [段1] 条件守卫——全部成立 → 触发
            if (_S_Talk_State == S_Talk_State.UserMsg && T_Checkpoint_Cube.IsIdle())
            {
                T_Checkpoint_Cube.Start();
                // [段4] 操作——顺序执行 + try-catch 隔离 + 自动审计
                bool ok = false;
                try
                {
                    AuditBrick("invoke", "T_Checkpoint", "llm.ctx_checkpoint", frame);
                    ok = Mau.Bricks.BRIK_LLM_017.CtxCheckpoint(_sessionKey, out _checkpoint);
                    AuditBrick("ok", "T_Checkpoint", "llm.ctx_checkpoint", frame);
                }
                catch (Exception ex)
                {
                    ok = false;
                    AuditBrick("error", "T_Checkpoint", "llm.ctx_checkpoint", frame);
                }
                // [段5] 结果转移——bool 驱动首项成功 / 次项失败 / 多路 switch 分发
                if (ok)
                {
                    S_Talk_Enter_PushUser();
                }
                else
                {
                    S_Talk_Enter_Failed();
                }
                // [段6b] Cube 复位——触发完成（成功/失败均复位，允许再次触发）
                T_Checkpoint_Cube.Complete();
            }
            // [段7] τ 时限——Cube 步进 + 耗尽 → 次项（超时走失败分支）
            else if (_S_Talk_State == S_Talk_State.UserMsg && T_Checkpoint_Cube.IsRunning())
            {
                T_Checkpoint_Cube.TickFrame();
                if (T_Checkpoint_Cube.IsExpired())
                {
                    S_Talk_Enter_Failed();
                    T_Checkpoint_Cube.Reset();
                }
            }
        }

        /// <summary>
        /// T_PushUser 控制律——条件 + 操作 → 结果
        /// </summary>
        /// <param name="frame">全局帧号</param>
        private void T_PushUser_Execute(int frame)
        {
            // [段1] 条件守卫——全部成立 → 触发
            if (_S_Talk_State == S_Talk_State.PushUser && T_PushUser_Cube.IsIdle())
            {
                T_PushUser_Cube.Start();
                // [段4] 操作——顺序执行 + try-catch 隔离 + 自动审计
                bool ok = false;
                try
                {
                    AuditBrick("invoke", "T_PushUser", "llm.ctx_push_user", frame);
                    ok = Mau.Bricks.BRIK_LLM_004.CtxPushUser(_sessionKey, _text);
                    AuditBrick("ok", "T_PushUser", "llm.ctx_push_user", frame);
                }
                catch (Exception ex)
                {
                    ok = false;
                    AuditBrick("error", "T_PushUser", "llm.ctx_push_user", frame);
                }
                // [段5] 结果转移——bool 驱动首项成功 / 次项失败 / 多路 switch 分发
                if (ok)
                {
                    S_Talk_Enter_SaveUser();
                }
                else
                {
                    S_Talk_Enter_Failed();
                }
                // [段6b] Cube 复位——触发完成（成功/失败均复位，允许再次触发）
                T_PushUser_Cube.Complete();
            }
            // [段7] τ 时限——Cube 步进 + 耗尽 → 次项（超时走失败分支）
            else if (_S_Talk_State == S_Talk_State.PushUser && T_PushUser_Cube.IsRunning())
            {
                T_PushUser_Cube.TickFrame();
                if (T_PushUser_Cube.IsExpired())
                {
                    S_Talk_Enter_Failed();
                    T_PushUser_Cube.Reset();
                }
            }
        }

        /// <summary>
        /// T_SaveUser 控制律——条件 + 操作 → 结果
        /// </summary>
        /// <param name="frame">全局帧号</param>
        private void T_SaveUser_Execute(int frame)
        {
            // [段1] 条件守卫——全部成立 → 触发
            if (_S_Talk_State == S_Talk_State.SaveUser && T_SaveUser_Cube.IsIdle())
            {
                T_SaveUser_Cube.Start();
                // [段4] 操作——顺序执行 + try-catch 隔离 + 自动审计
                bool ok = false;
                try
                {
                    AuditBrick("invoke", "T_SaveUser", "data.box_set_dic", frame);
                    ok = Mau.Bricks.BRIK_DATA_005.SetDic(_sessionKey, "last_user_text", _text);
                    AuditBrick("ok", "T_SaveUser", "data.box_set_dic", frame);
                }
                catch (Exception ex)
                {
                    ok = false;
                    AuditBrick("error", "T_SaveUser", "data.box_set_dic", frame);
                }
                // [段5] 结果转移——bool 驱动首项成功 / 次项失败 / 多路 switch 分发
                if (ok)
                {
                    S_Talk_Enter_BuildMsg();
                }
                else
                {
                    S_Talk_Enter_Failed();
                }
                // [段6b] Cube 复位——触发完成（成功/失败均复位，允许再次触发）
                T_SaveUser_Cube.Complete();
            }
            // [段7] τ 时限——Cube 步进 + 耗尽 → 次项（超时走失败分支）
            else if (_S_Talk_State == S_Talk_State.SaveUser && T_SaveUser_Cube.IsRunning())
            {
                T_SaveUser_Cube.TickFrame();
                if (T_SaveUser_Cube.IsExpired())
                {
                    S_Talk_Enter_Failed();
                    T_SaveUser_Cube.Reset();
                }
            }
        }

        /// <summary>
        /// T_BuildMsg 控制律——条件 + 操作 → 结果
        /// </summary>
        /// <param name="frame">全局帧号</param>
        private void T_BuildMsg_Execute(int frame)
        {
            // [段1] 条件守卫——全部成立 → 触发
            if (_S_Talk_State == S_Talk_State.BuildMsg && T_BuildMsg_Cube.IsIdle())
            {
                T_BuildMsg_Cube.Start();
                // [段4] 操作——顺序执行 + try-catch 隔离 + 自动审计
                bool ok = false;
                try
                {
                    AuditBrick("invoke", "T_BuildMsg", "llm.ctx_build_messages_json", frame);
                    ok = Mau.Bricks.BRIK_LLM_012.CtxBuildMessagesJson(_sessionKey, out _messagesJson);
                    AuditBrick("ok", "T_BuildMsg", "llm.ctx_build_messages_json", frame);
                }
                catch (Exception ex)
                {
                    ok = false;
                    AuditBrick("error", "T_BuildMsg", "llm.ctx_build_messages_json", frame);
                }
                // [段5] 结果转移——bool 驱动首项成功 / 次项失败 / 多路 switch 分发
                if (ok)
                {
                    S_Talk_Enter_BuildTools();
                }
                else
                {
                    S_Talk_Enter_Failed();
                }
                // [段6b] Cube 复位——触发完成（成功/失败均复位，允许再次触发）
                T_BuildMsg_Cube.Complete();
            }
            // [段7] τ 时限——Cube 步进 + 耗尽 → 次项（超时走失败分支）
            else if (_S_Talk_State == S_Talk_State.BuildMsg && T_BuildMsg_Cube.IsRunning())
            {
                T_BuildMsg_Cube.TickFrame();
                if (T_BuildMsg_Cube.IsExpired())
                {
                    S_Talk_Enter_Failed();
                    T_BuildMsg_Cube.Reset();
                }
            }
        }

        /// <summary>
        /// T_BuildTools 控制律——条件 + 操作 → 结果
        /// </summary>
        /// <param name="frame">全局帧号</param>
        private void T_BuildTools_Execute(int frame)
        {
            // [段1] 条件守卫——全部成立 → 触发
            if (_S_Talk_State == S_Talk_State.BuildTools && T_BuildTools_Cube.IsIdle())
            {
                T_BuildTools_Cube.Start();
                // [段4] 操作——顺序执行 + try-catch 隔离 + 自动审计
                bool ok = false;
                try
                {
                    AuditBrick("invoke", "T_BuildTools", "cat.tools_json", frame);
                    ok = Mau.Bricks.BRIK_CAT_002.ToolsJson(_sessionKey, out _toolsJson);
                    AuditBrick("ok", "T_BuildTools", "cat.tools_json", frame);
                }
                catch (Exception ex)
                {
                    ok = false;
                    AuditBrick("error", "T_BuildTools", "cat.tools_json", frame);
                }
                // [段5] 结果转移——bool 驱动首项成功 / 次项失败 / 多路 switch 分发
                if (ok)
                {
                    S_Talk_Enter_Start();
                }
                else
                {
                    S_Talk_Enter_Failed();
                }
                // [段6b] Cube 复位——触发完成（成功/失败均复位，允许再次触发）
                T_BuildTools_Cube.Complete();
            }
            // [段7] τ 时限——Cube 步进 + 耗尽 → 次项（超时走失败分支）
            else if (_S_Talk_State == S_Talk_State.BuildTools && T_BuildTools_Cube.IsRunning())
            {
                T_BuildTools_Cube.TickFrame();
                if (T_BuildTools_Cube.IsExpired())
                {
                    S_Talk_Enter_Failed();
                    T_BuildTools_Cube.Reset();
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
            if ((_S_Talk_State == S_Talk_State.Start) && (R_ReplySlot_Count > 0) && T_Start_Cube.IsIdle())
            {
                // [段3] 资源获取——动作完成后释放
                R_ReplySlot_Count = R_ReplySlot_Count - 1;
                T_Start_Cube.Start();
                // [段4] 操作——顺序执行 + try-catch 隔离 + 自动审计
                bool ok = false;
                try
                {
                    AuditBrick("invoke", "T_Start", "llm.completions", frame);
                    ok = Mau.Bricks.BRIK_LLM_013.Completions("deepseek-v4-flash", _messagesJson, _toolsJson, out _requestId);
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
                // [段6] 资源释放
                R_ReplySlot_Count = R_ReplySlot_Count + 1;
                // [段6b] Cube 复位——触发完成（成功/失败均复位，允许再次触发）
                T_Start_Cube.Complete();
            }
            // [段7] τ 时限——Cube 步进 + 耗尽 → 次项（超时走失败分支）
            else if ((_S_Talk_State == S_Talk_State.Start) && (R_ReplySlot_Count > 0) && T_Start_Cube.IsRunning())
            {
                T_Start_Cube.TickFrame();
                if (T_Start_Cube.IsExpired())
                {
                    S_Talk_Enter_Failed();
                    T_Start_Cube.Reset();
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
            if ((P_ChunkReady) && (P_Ended) && T_Classify_Cube.IsIdle())
            {
                T_Classify_Cube.Start();
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
                T_Classify_Cube.Complete();
            }
            // [段7] τ 时限——Cube 步进 + 耗尽 → 次项（超时走失败分支）
            else if ((P_ChunkReady) && (P_Ended) && T_Classify_Cube.IsRunning())
            {
                T_Classify_Cube.TickFrame();
                if (T_Classify_Cube.IsExpired())
                {
                    S_Active_Enter_Thinking();
                    T_Classify_Cube.Reset();
                }
            }
        }

        /// <summary>
        /// T_Error 控制律——条件 + 操作 → 结果
        /// </summary>
        /// <param name="frame">全局帧号</param>
        private void T_Error_Execute(int frame)
        {
            // [段1] 条件守卫——全部成立 → 触发
            if ((P_Error) && (_S_Active_State == S_Active_State.Streaming) && T_Error_Cube.IsIdle())
            {
                T_Error_Cube.Start();
                // [段4] 操作——顺序执行 + try-catch 隔离 + 自动审计
                bool ok = false;
                try
                {
                    AuditBrick("invoke", "T_Error", "llm.finish", frame);
                    ok = Mau.Bricks.BRIK_LLM_021.Finish(_requestId);
                    AuditBrick("ok", "T_Error", "llm.finish", frame);
                }
                catch (Exception ex)
                {
                    ok = false;
                    AuditBrick("error", "T_Error", "llm.finish", frame);
                }
                // [段5] 结果转移——bool 驱动首项成功 / 次项失败 / 多路 switch 分发
                if (ok)
                {
                    S_Active_Enter_ErrorFinish();
                }
                else
                {
                    S_Talk_Enter_Failed();
                }
                // [段6b] Cube 复位——触发完成（成功/失败均复位，允许再次触发）
                T_Error_Cube.Complete();
            }
            // [段7] τ 时限——Cube 步进 + 耗尽 → 次项（超时走失败分支）
            else if ((P_Error) && (_S_Active_State == S_Active_State.Streaming) && T_Error_Cube.IsRunning())
            {
                T_Error_Cube.TickFrame();
                if (T_Error_Cube.IsExpired())
                {
                    S_Talk_Enter_Failed();
                    T_Error_Cube.Reset();
                }
            }
        }

        /// <summary>
        /// T_ErrorRollback 控制律——条件 + 操作 → 结果
        /// </summary>
        /// <param name="frame">全局帧号</param>
        private void T_ErrorRollback_Execute(int frame)
        {
            // [段1] 条件守卫——全部成立 → 触发
            if (_S_Active_State == S_Active_State.ErrorFinish && T_ErrorRollback_Cube.IsIdle())
            {
                T_ErrorRollback_Cube.Start();
                // [段4] 操作——顺序执行 + try-catch 隔离 + 自动审计
                bool ok = false;
                try
                {
                    AuditBrick("invoke", "T_ErrorRollback", "llm.ctx_rollback", frame);
                    ok = Mau.Bricks.BRIK_LLM_018.CtxRollback(_sessionKey, _checkpoint);
                    AuditBrick("ok", "T_ErrorRollback", "llm.ctx_rollback", frame);
                }
                catch (Exception ex)
                {
                    ok = false;
                    AuditBrick("error", "T_ErrorRollback", "llm.ctx_rollback", frame);
                }
                // [段5] 结果转移——bool 驱动首项成功 / 次项失败 / 多路 switch 分发
                if (ok)
                {
                    S_Active_Enter_ErrorRecord();
                }
                else
                {
                    S_Talk_Enter_Failed();
                }
                // [段6b] Cube 复位——触发完成（成功/失败均复位，允许再次触发）
                T_ErrorRollback_Cube.Complete();
            }
            // [段7] τ 时限——Cube 步进 + 耗尽 → 次项（超时走失败分支）
            else if (_S_Active_State == S_Active_State.ErrorFinish && T_ErrorRollback_Cube.IsRunning())
            {
                T_ErrorRollback_Cube.TickFrame();
                if (T_ErrorRollback_Cube.IsExpired())
                {
                    S_Talk_Enter_Failed();
                    T_ErrorRollback_Cube.Reset();
                }
            }
        }

        /// <summary>
        /// T_ErrorRecord 控制律——条件 + 操作 → 结果
        /// </summary>
        /// <param name="frame">全局帧号</param>
        private void T_ErrorRecord_Execute(int frame)
        {
            // [段1] 条件守卫——全部成立 → 触发
            if (_S_Active_State == S_Active_State.ErrorRecord && T_ErrorRecord_Cube.IsIdle())
            {
                T_ErrorRecord_Cube.Start();
                // [段4] 操作——顺序执行 + try-catch 隔离 + 自动审计
                bool ok = false;
                try
                {
                    AuditBrick("invoke", "T_ErrorRecord", "llm.ctx_push_assistant", frame);
                    ok = Mau.Bricks.BRIK_LLM_005.CtxPushAssistant(_sessionKey, "请求失败，已回滚");
                    AuditBrick("ok", "T_ErrorRecord", "llm.ctx_push_assistant", frame);
                }
                catch (Exception ex)
                {
                    ok = false;
                    AuditBrick("error", "T_ErrorRecord", "llm.ctx_push_assistant", frame);
                }
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
                T_ErrorRecord_Cube.Complete();
            }
            // [段7] τ 时限——Cube 步进 + 耗尽 → 次项（超时走失败分支）
            else if (_S_Active_State == S_Active_State.ErrorRecord && T_ErrorRecord_Cube.IsRunning())
            {
                T_ErrorRecord_Cube.TickFrame();
                if (T_ErrorRecord_Cube.IsExpired())
                {
                    S_Talk_Enter_Failed();
                    T_ErrorRecord_Cube.Reset();
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
                    S_Active_Enter_PushStream();
                }
                // [段6b] Cube 复位——触发完成（成功/失败均复位，允许再次触发）
                T_CheckTool_Cube.Complete();
            }
            // [段7] τ 时限——Cube 步进 + 耗尽 → 次项（超时走失败分支）
            else if (_S_Active_State == S_Active_State.Streaming && T_CheckTool_Cube.IsRunning())
            {
                T_CheckTool_Cube.TickFrame();
                if (T_CheckTool_Cube.IsExpired())
                {
                    S_Active_Enter_PushStream();
                    T_CheckTool_Cube.Reset();
                }
            }
        }

        /// <summary>
        /// T_PushToolReq 控制律——条件 + 操作 → 结果
        /// </summary>
        /// <param name="frame">全局帧号</param>
        private void T_PushToolReq_Execute(int frame)
        {
            // [段1] 条件守卫——全部成立 → 触发
            if (_S_Active_State == S_Active_State.ToolWait && T_PushToolReq_Cube.IsIdle())
            {
                T_PushToolReq_Cube.Start();
                // [段4] 操作——顺序执行 + try-catch 隔离 + 自动审计
                bool ok = false;
                try
                {
                    AuditBrick("invoke", "T_PushToolReq", "data.box_set_dic", frame);
                    ok = Mau.Bricks.BRIK_DATA_005.SetDic(_sessionKey, "tool_req", _toolCallsJson);
                    AuditBrick("ok", "T_PushToolReq", "data.box_set_dic", frame);
                }
                catch (Exception ex)
                {
                    ok = false;
                    AuditBrick("error", "T_PushToolReq", "data.box_set_dic", frame);
                }
                // [段5] 结果转移——bool 驱动首项成功 / 次项失败 / 多路 switch 分发
                if (ok)
                {
                    S_Active_Enter_ToolFlag();
                }
                else
                {
                    S_Talk_Enter_Failed();
                }
                // [段6b] Cube 复位——触发完成（成功/失败均复位，允许再次触发）
                T_PushToolReq_Cube.Complete();
            }
            // [段7] τ 时限——Cube 步进 + 耗尽 → 次项（超时走失败分支）
            else if (_S_Active_State == S_Active_State.ToolWait && T_PushToolReq_Cube.IsRunning())
            {
                T_PushToolReq_Cube.TickFrame();
                if (T_PushToolReq_Cube.IsExpired())
                {
                    S_Talk_Enter_Failed();
                    T_PushToolReq_Cube.Reset();
                }
            }
        }

        /// <summary>
        /// T_ToolFlag 控制律——条件 + 操作 → 结果
        /// </summary>
        /// <param name="frame">全局帧号</param>
        private void T_ToolFlag_Execute(int frame)
        {
            // [段1] 条件守卫——全部成立 → 触发
            if (_S_Active_State == S_Active_State.ToolFlag && T_ToolFlag_Cube.IsIdle())
            {
                T_ToolFlag_Cube.Start();
                // [段4] 操作——顺序执行 + try-catch 隔离 + 自动审计
                bool ok = false;
                try
                {
                    AuditBrick("invoke", "T_ToolFlag", "data.box_set", frame);
                    ok = Mau.Bricks.BRIK_DATA_003.Set(_sessionKey, "tool_req_flag", 1);
                    AuditBrick("ok", "T_ToolFlag", "data.box_set", frame);
                }
                catch (Exception ex)
                {
                    ok = false;
                    AuditBrick("error", "T_ToolFlag", "data.box_set", frame);
                }
                // [段5] 结果转移——bool 驱动首项成功 / 次项失败 / 多路 switch 分发
                if (ok)
                {
                    S_Talk_Enter_WaitTools();
                }
                else
                {
                    S_Talk_Enter_Failed();
                }
                // [段6b] Cube 复位——触发完成（成功/失败均复位，允许再次触发）
                T_ToolFlag_Cube.Complete();
            }
            // [段7] τ 时限——Cube 步进 + 耗尽 → 次项（超时走失败分支）
            else if (_S_Active_State == S_Active_State.ToolFlag && T_ToolFlag_Cube.IsRunning())
            {
                T_ToolFlag_Cube.TickFrame();
                if (T_ToolFlag_Cube.IsExpired())
                {
                    S_Talk_Enter_Failed();
                    T_ToolFlag_Cube.Reset();
                }
            }
        }

        /// <summary>
        /// T_ToolsDone 控制律——条件 + 操作 → 结果
        /// </summary>
        /// <param name="frame">全局帧号</param>
        private void T_ToolsDone_Execute(int frame)
        {
            // [段1] 条件守卫——全部成立 → 触发
            if ((P_ToolsDone) && (_S_Talk_State == S_Talk_State.WaitTools) && T_ToolsDone_Cube.IsIdle())
            {
                T_ToolsDone_Cube.Start();
                // [段4] 操作——顺序执行 + try-catch 隔离 + 自动审计
                bool ok = false;
                try
                {
                    AuditBrick("invoke", "T_ToolsDone", "data.box_set", frame);
                    ok = Mau.Bricks.BRIK_DATA_003.Set(_sessionKey, "tools_done", 0);
                    AuditBrick("ok", "T_ToolsDone", "data.box_set", frame);
                }
                catch (Exception ex)
                {
                    ok = false;
                    AuditBrick("error", "T_ToolsDone", "data.box_set", frame);
                }
                // [段5] 结果转移——bool 驱动首项成功 / 次项失败 / 多路 switch 分发
                if (ok)
                {
                    S_Talk_Enter_BuildMsg();
                }
                else
                {
                    S_Talk_Enter_Failed();
                }
                // [段6b] Cube 复位——触发完成（成功/失败均复位，允许再次触发）
                T_ToolsDone_Cube.Complete();
            }
            // [段7] τ 时限——Cube 步进 + 耗尽 → 次项（超时走失败分支）
            else if ((P_ToolsDone) && (_S_Talk_State == S_Talk_State.WaitTools) && T_ToolsDone_Cube.IsRunning())
            {
                T_ToolsDone_Cube.TickFrame();
                if (T_ToolsDone_Cube.IsExpired())
                {
                    S_Talk_Enter_Failed();
                    T_ToolsDone_Cube.Reset();
                }
            }
        }

        /// <summary>
        /// T_PushStream 控制律——条件 + 操作 → 结果
        /// </summary>
        /// <param name="frame">全局帧号</param>
        private void T_PushStream_Execute(int frame)
        {
            // [段1] 条件守卫——全部成立 → 触发
            if (_S_Active_State == S_Active_State.PushStream && T_PushStream_Cube.IsIdle())
            {
                T_PushStream_Cube.Start();
                // [段4] 操作——顺序执行 + try-catch 隔离 + 自动审计
                bool ok = false;
                try
                {
                    AuditBrick("invoke", "T_PushStream", "llm.ctx_push_stream", frame);
                    ok = Mau.Bricks.BRIK_LLM_027.CtxPushStream(_requestId, _sessionKey);
                    AuditBrick("ok", "T_PushStream", "llm.ctx_push_stream", frame);
                }
                catch (Exception ex)
                {
                    ok = false;
                    AuditBrick("error", "T_PushStream", "llm.ctx_push_stream", frame);
                }
                // [段5] 结果转移——bool 驱动首项成功 / 次项失败 / 多路 switch 分发
                if (ok)
                {
                    S_Active_Enter_PushReasoning();
                }
                else
                {
                    S_Talk_Enter_Failed();
                }
                // [段6b] Cube 复位——触发完成（成功/失败均复位，允许再次触发）
                T_PushStream_Cube.Complete();
            }
            // [段7] τ 时限——Cube 步进 + 耗尽 → 次项（超时走失败分支）
            else if (_S_Active_State == S_Active_State.PushStream && T_PushStream_Cube.IsRunning())
            {
                T_PushStream_Cube.TickFrame();
                if (T_PushStream_Cube.IsExpired())
                {
                    S_Talk_Enter_Failed();
                    T_PushStream_Cube.Reset();
                }
            }
        }

        /// <summary>
        /// T_PushReasoning 控制律——条件 + 操作 → 结果
        /// </summary>
        /// <param name="frame">全局帧号</param>
        private void T_PushReasoning_Execute(int frame)
        {
            // [段1] 条件守卫——全部成立 → 触发
            if (_S_Active_State == S_Active_State.PushReasoning && T_PushReasoning_Cube.IsIdle())
            {
                T_PushReasoning_Cube.Start();
                // [段4] 操作——顺序执行 + try-catch 隔离 + 自动审计
                bool ok = false;
                try
                {
                    AuditBrick("invoke", "T_PushReasoning", "llm.ctx_push_reasoning", frame);
                    ok = Mau.Bricks.BRIK_LLM_028.CtxPushReasoning(_requestId, _sessionKey);
                    AuditBrick("ok", "T_PushReasoning", "llm.ctx_push_reasoning", frame);
                }
                catch (Exception ex)
                {
                    ok = false;
                    AuditBrick("error", "T_PushReasoning", "llm.ctx_push_reasoning", frame);
                }
                // [段5] 结果转移——bool 驱动首项成功 / 次项失败 / 多路 switch 分发
                if (ok)
                {
                    S_Active_Enter_PushStats();
                }
                else
                {
                    S_Talk_Enter_Failed();
                }
                // [段6b] Cube 复位——触发完成（成功/失败均复位，允许再次触发）
                T_PushReasoning_Cube.Complete();
            }
            // [段7] τ 时限——Cube 步进 + 耗尽 → 次项（超时走失败分支）
            else if (_S_Active_State == S_Active_State.PushReasoning && T_PushReasoning_Cube.IsRunning())
            {
                T_PushReasoning_Cube.TickFrame();
                if (T_PushReasoning_Cube.IsExpired())
                {
                    S_Talk_Enter_Failed();
                    T_PushReasoning_Cube.Reset();
                }
            }
        }

        /// <summary>
        /// T_PushStats 控制律——条件 + 操作 → 结果
        /// </summary>
        /// <param name="frame">全局帧号</param>
        private void T_PushStats_Execute(int frame)
        {
            // [段1] 条件守卫——全部成立 → 触发
            if (_S_Active_State == S_Active_State.PushStats && T_PushStats_Cube.IsIdle())
            {
                T_PushStats_Cube.Start();
                // [段4] 操作——顺序执行 + try-catch 隔离 + 自动审计
                bool ok = false;
                try
                {
                    AuditBrick("invoke", "T_PushStats", "llm.round_stats_text", frame);
                    ok = Mau.Bricks.BRIK_LLM_024.RoundStatsText(_requestId, _sessionKey, out _text);
                    AuditBrick("ok", "T_PushStats", "llm.round_stats_text", frame);
                }
                catch (Exception ex)
                {
                    ok = false;
                    AuditBrick("error", "T_PushStats", "llm.round_stats_text", frame);
                }
                // [段5] 结果转移——bool 驱动首项成功 / 次项失败 / 多路 switch 分发
                if (ok)
                {
                    S_Active_Enter_PushStatsLine();
                }
                else
                {
                    S_Talk_Enter_Failed();
                }
                // [段6b] Cube 复位——触发完成（成功/失败均复位，允许再次触发）
                T_PushStats_Cube.Complete();
            }
            // [段7] τ 时限——Cube 步进 + 耗尽 → 次项（超时走失败分支）
            else if (_S_Active_State == S_Active_State.PushStats && T_PushStats_Cube.IsRunning())
            {
                T_PushStats_Cube.TickFrame();
                if (T_PushStats_Cube.IsExpired())
                {
                    S_Talk_Enter_Failed();
                    T_PushStats_Cube.Reset();
                }
            }
        }

        /// <summary>
        /// T_PushStatsLine 控制律——条件 + 操作 → 结果
        /// </summary>
        /// <param name="frame">全局帧号</param>
        private void T_PushStatsLine_Execute(int frame)
        {
            // [段1] 条件守卫——全部成立 → 触发
            if (_S_Active_State == S_Active_State.PushStatsLine && T_PushStatsLine_Cube.IsIdle())
            {
                T_PushStatsLine_Cube.Start();
                // [段4] 操作——顺序执行 + try-catch 隔离 + 自动审计
                bool ok = false;
                try
                {
                    AuditBrick("invoke", "T_PushStatsLine", "llm.ctx_push_system", frame);
                    ok = Mau.Bricks.BRIK_LLM_025.CtxPushSystem(_sessionKey, _text);
                    AuditBrick("ok", "T_PushStatsLine", "llm.ctx_push_system", frame);
                }
                catch (Exception ex)
                {
                    ok = false;
                    AuditBrick("error", "T_PushStatsLine", "llm.ctx_push_system", frame);
                }
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
                T_PushStatsLine_Cube.Complete();
            }
            // [段7] τ 时限——Cube 步进 + 耗尽 → 次项（超时走失败分支）
            else if (_S_Active_State == S_Active_State.PushStatsLine && T_PushStatsLine_Cube.IsRunning())
            {
                T_PushStatsLine_Cube.TickFrame();
                if (T_PushStatsLine_Cube.IsExpired())
                {
                    S_Talk_Enter_Failed();
                    T_PushStatsLine_Cube.Reset();
                }
            }
        }

        /// <summary>
        /// T_Reset 控制律——条件 + 操作 → 结果
        /// </summary>
        /// <param name="frame">全局帧号</param>
        private void T_Reset_Execute(int frame)
        {
            // [段1] 条件守卫——全部成立 → 触发
            if ((_S_Talk_State == S_Talk_State.Done) || (_S_Talk_State == S_Talk_State.Failed) && T_Reset_Cube.IsIdle())
            {
                T_Reset_Cube.Start();
                // [段4] 操作——顺序执行 + try-catch 隔离 + 自动审计
                bool ok = false;
                try
                {
                    AuditBrick("invoke", "T_Reset", "llm.ctx_count", frame);
                    ok = Mau.Bricks.BRIK_LLM_007.CtxCount(_sessionKey, out _count);
                    AuditBrick("ok", "T_Reset", "llm.ctx_count", frame);
                }
                catch (Exception ex)
                {
                    ok = false;
                    AuditBrick("error", "T_Reset", "llm.ctx_count", frame);
                }
                // [段5] 结果转移——bool 驱动首项成功 / 次项失败 / 多路 switch 分发
                if (ok)
                {
                    S_Talk_Enter_Idle();
                }
                else
                {
                    S_Talk_Enter_Failed();
                }
                // [段6b] Cube 复位——触发完成（成功/失败均复位，允许再次触发）
                T_Reset_Cube.Complete();
            }
            // [段7] τ 时限——Cube 步进 + 耗尽 → 次项（超时走失败分支）
            else if ((_S_Talk_State == S_Talk_State.Done) || (_S_Talk_State == S_Talk_State.Failed) && T_Reset_Cube.IsRunning())
            {
                T_Reset_Cube.TickFrame();
                if (T_Reset_Cube.IsExpired())
                {
                    S_Talk_Enter_Failed();
                    T_Reset_Cube.Reset();
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
// #MAU_CHECKSUM:SHA256:4E9B7BA3753EB49D480EBB5689E6231AE16EBD60791A8A9239D95D74A88F0971