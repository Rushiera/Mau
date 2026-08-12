// ═══ Ch4Toolposter 生成物 — Mau v2.0 翻译器 ═══
// 生成物由翻译器确定性输出——手工修改无效，改 .mau 后重新生成
using System;
using System.Collections.Generic;
using Mau.Runtime;

namespace Mau.Generated
{
    /// <summary>
    /// 生成物——Ch4Toolposter 受控系统
    /// </summary>
    public sealed class Ch4Toolposter
    {
        // [注入字段]
        private string? _sessionKey;

        /// <summary>
        /// 注入字段设置——sessionKey（纯赋值，不置位信号）
        /// </summary>
        /// <param name="value">注入值</param>
        public void SetsessionKey(string? value) { _sessionKey = value; }

        private enum S_Poster_State { Polling, Dispatch, WaitClosed, Collect, Finish, Summarize, ClearReq, Failed }
        private S_Poster_State _S_Poster_State;

        // [命题]
        private bool P_ReqReady;
        private bool P_Closed;
        private bool P_Timeout;

        // [积木输出端口]
        private string _dataJson = default;
        private long _dogId = default;
        private string _toolName = default;
        private string _result = default;
        private string _error = default;
        private string _callId = default;
        private string _session = default;
        private string _argsJson = default;

        // [测量帧计数]
        private int M_PollReq_FrameCounter;
        private int M_PollClosed_FrameCounter;
        private int M_PollTimeout_FrameCounter;

        // [控制律 Cube]
        private readonly Cube T_ReadReq_Cube = new Cube(5);
        private readonly Cube T_CreateNext_Cube = new Cube(10);
        private readonly Cube T_ReadAll_Cube = new Cube(5);
        private readonly Cube T_TimeoutFinish_Cube = new Cube(5);
        private readonly Cube T_PushTool_Cube = new Cube(5);
        private readonly Cube T_Finish_Cube = new Cube(5);
        private readonly Cube T_Summarize_Cube = new Cube(5);
        private readonly Cube T_ClearReq_Cube = new Cube(5);

        /// <summary>
        /// 构造——初始状态置位
        /// </summary>
        public Ch4Toolposter()
        {
            S_Poster_Enter_Polling();
        }

        /// <summary>
        /// 进入 S_Poster.Polling——单值赋值（互斥由类型系统保证）
        /// </summary>
        private void S_Poster_Enter_Polling()
        {
            _S_Poster_State = S_Poster_State.Polling;
        }

        /// <summary>
        /// 进入 S_Poster.Dispatch——单值赋值（互斥由类型系统保证）
        /// </summary>
        private void S_Poster_Enter_Dispatch()
        {
            _S_Poster_State = S_Poster_State.Dispatch;
        }

        /// <summary>
        /// 进入 S_Poster.WaitClosed——单值赋值（互斥由类型系统保证）
        /// </summary>
        private void S_Poster_Enter_WaitClosed()
        {
            _S_Poster_State = S_Poster_State.WaitClosed;
        }

        /// <summary>
        /// 进入 S_Poster.Collect——单值赋值（互斥由类型系统保证）
        /// </summary>
        private void S_Poster_Enter_Collect()
        {
            _S_Poster_State = S_Poster_State.Collect;
        }

        /// <summary>
        /// 进入 S_Poster.Finish——单值赋值（互斥由类型系统保证）
        /// </summary>
        private void S_Poster_Enter_Finish()
        {
            _S_Poster_State = S_Poster_State.Finish;
        }

        /// <summary>
        /// 进入 S_Poster.Summarize——单值赋值（互斥由类型系统保证）
        /// </summary>
        private void S_Poster_Enter_Summarize()
        {
            _S_Poster_State = S_Poster_State.Summarize;
        }

        /// <summary>
        /// 进入 S_Poster.ClearReq——单值赋值（互斥由类型系统保证）
        /// </summary>
        private void S_Poster_Enter_ClearReq()
        {
            _S_Poster_State = S_Poster_State.ClearReq;
        }

        /// <summary>
        /// 进入 S_Poster.Failed——单值赋值（互斥由类型系统保证）
        /// </summary>
        private void S_Poster_Enter_Failed()
        {
            _S_Poster_State = S_Poster_State.Failed;
        }

        /// <summary>
        /// S_Poster 是否处于 Polling
        /// </summary>
        public bool IsPosterPolling() { return _S_Poster_State == S_Poster_State.Polling; }

        /// <summary>
        /// S_Poster 是否处于 Dispatch
        /// </summary>
        public bool IsPosterDispatch() { return _S_Poster_State == S_Poster_State.Dispatch; }

        /// <summary>
        /// S_Poster 是否处于 WaitClosed
        /// </summary>
        public bool IsPosterWaitClosed() { return _S_Poster_State == S_Poster_State.WaitClosed; }

        /// <summary>
        /// S_Poster 是否处于 Collect
        /// </summary>
        public bool IsPosterCollect() { return _S_Poster_State == S_Poster_State.Collect; }

        /// <summary>
        /// S_Poster 是否处于 Finish
        /// </summary>
        public bool IsPosterFinish() { return _S_Poster_State == S_Poster_State.Finish; }

        /// <summary>
        /// S_Poster 是否处于 Summarize
        /// </summary>
        public bool IsPosterSummarize() { return _S_Poster_State == S_Poster_State.Summarize; }

        /// <summary>
        /// S_Poster 是否处于 ClearReq
        /// </summary>
        public bool IsPosterClearReq() { return _S_Poster_State == S_Poster_State.ClearReq; }

        /// <summary>
        /// S_Poster 是否处于 Failed
        /// </summary>
        public bool IsPosterFailed() { return _S_Poster_State == S_Poster_State.Failed; }

        /// <summary>
        /// S_Poster 当前状态名
        /// </summary>
        public string GetPosterState()
        {
            switch (_S_Poster_State)
            {
                case S_Poster_State.Polling: return "Polling";
                case S_Poster_State.Dispatch: return "Dispatch";
                case S_Poster_State.WaitClosed: return "WaitClosed";
                case S_Poster_State.Collect: return "Collect";
                case S_Poster_State.Finish: return "Finish";
                case S_Poster_State.Summarize: return "Summarize";
                case S_Poster_State.ClearReq: return "ClearReq";
                case S_Poster_State.Failed: return "Failed";
                default: return "Unknown";
            }
        }

        /// <summary>
        /// M_PollReq 采样——每帧写入 P_ReqReady
        /// </summary>
        private void M_PollReq_Sample()
        {
            bool result = Mau.Bricks.BRIK_DATA_007.Is(_sessionKey, "tool_req_flag", 1);
            P_ReqReady = result;
            M_PollReq_FrameCounter = M_PollReq_FrameCounter + 1;
        }

        /// <summary>
        /// M_PollClosed 采样——每帧写入 P_Closed
        /// </summary>
        private void M_PollClosed_Sample()
        {
            bool result = Mau.Bricks.BRIK_DOG_004.IsClosed(_dogId);
            P_Closed = result;
            M_PollClosed_FrameCounter = M_PollClosed_FrameCounter + 1;
        }

        /// <summary>
        /// M_PollTimeout 采样——每帧写入 P_Timeout
        /// </summary>
        private void M_PollTimeout_Sample()
        {
            bool result = Mau.Bricks.BRIK_DOG_005.IsTimeout(_dogId);
            P_Timeout = result;
            M_PollTimeout_FrameCounter = M_PollTimeout_FrameCounter + 1;
        }

        /// <summary>
        /// 帧驱动——测量采样 + 控制律守卫（声明顺序）
        /// </summary>
        /// <param name="frame">全局帧号</param>
        public void Tick(int frame)
        {
            M_PollReq_Sample();
            M_PollClosed_Sample();
            M_PollTimeout_Sample();
            T_ReadReq_Execute(frame);
            T_CreateNext_Execute(frame);
            T_ReadAll_Execute(frame);
            T_TimeoutFinish_Execute(frame);
            T_PushTool_Execute(frame);
            T_Finish_Execute(frame);
            T_Summarize_Execute(frame);
            T_ClearReq_Execute(frame);
        }

        /// <summary>
        /// T_ReadReq 控制律——条件 + 操作 → 结果
        /// </summary>
        /// <param name="frame">全局帧号</param>
        private void T_ReadReq_Execute(int frame)
        {
            // [段1] 条件守卫——全部成立 → 触发
            if ((P_ReqReady) && (_S_Poster_State == S_Poster_State.Polling) && T_ReadReq_Cube.IsIdle())
            {
                T_ReadReq_Cube.Start();
                // [段4] 操作——顺序执行 + try-catch 隔离 + 自动审计
                bool ok = false;
                try
                {
                    AuditBrick("invoke", "T_ReadReq", "data.box_get_dic", frame);
                    ok = Mau.Bricks.BRIK_DATA_006.GetDic(_sessionKey, "tool_req", out _dataJson);
                    AuditBrick("ok", "T_ReadReq", "data.box_get_dic", frame);
                }
                catch (Exception ex)
                {
                    ok = false;
                    AuditBrick("error", "T_ReadReq", "data.box_get_dic", frame);
                }
                // [段5] 结果转移——bool 驱动首项成功 / 次项失败 / 多路 switch 分发
                if (ok)
                {
                    S_Poster_Enter_Dispatch();
                }
                else
                {
                    S_Poster_Enter_Failed();
                }
            }
            // [段7] τ 时限——Cube 步进 + 耗尽 → 次项（超时走失败分支）
            else if ((P_ReqReady) && (_S_Poster_State == S_Poster_State.Polling) && T_ReadReq_Cube.IsRunning())
            {
                T_ReadReq_Cube.TickFrame();
                if (T_ReadReq_Cube.IsExpired())
                {
                    S_Poster_Enter_Failed();
                }
            }
        }

        /// <summary>
        /// T_CreateNext 控制律——条件 + 操作 → 结果
        /// </summary>
        /// <param name="frame">全局帧号</param>
        private void T_CreateNext_Execute(int frame)
        {
            // [段1] 条件守卫——全部成立 → 触发
            if (_S_Poster_State == S_Poster_State.Dispatch && T_CreateNext_Cube.IsIdle())
            {
                T_CreateNext_Cube.Start();
                // [段4] 操作——顺序执行 + try-catch 隔离 + 自动审计
                bool ok = false;
                try
                {
                    AuditBrick("invoke", "T_CreateNext", "tool.create_next", frame);
                    ok = Mau.Bricks.BRIK_TOOL_012.CreateNext(_dataJson, _sessionKey, 300, out _dogId, out _toolName);
                    AuditBrick("ok", "T_CreateNext", "tool.create_next", frame);
                }
                catch (Exception ex)
                {
                    ok = false;
                    AuditBrick("error", "T_CreateNext", "tool.create_next", frame);
                }
                // [段5] 结果转移——bool 驱动首项成功 / 次项失败 / 多路 switch 分发
                if (ok)
                {
                    S_Poster_Enter_WaitClosed();
                }
                else
                {
                    S_Poster_Enter_Summarize();
                }
            }
            // [段7] τ 时限——Cube 步进 + 耗尽 → 次项（超时走失败分支）
            else if (_S_Poster_State == S_Poster_State.Dispatch && T_CreateNext_Cube.IsRunning())
            {
                T_CreateNext_Cube.TickFrame();
                if (T_CreateNext_Cube.IsExpired())
                {
                    S_Poster_Enter_Summarize();
                }
            }
        }

        /// <summary>
        /// T_ReadAll 控制律——条件 + 操作 → 结果
        /// </summary>
        /// <param name="frame">全局帧号</param>
        private void T_ReadAll_Execute(int frame)
        {
            // [段1] 条件守卫——全部成立 → 触发
            if ((P_Closed) && (_S_Poster_State == S_Poster_State.WaitClosed) && T_ReadAll_Cube.IsIdle())
            {
                T_ReadAll_Cube.Start();
                // [段4] 操作——顺序执行 + try-catch 隔离 + 自动审计
                bool ok = false;
                try
                {
                    AuditBrick("invoke", "T_ReadAll", "dog.collect_result", frame);
                    ok = Mau.Bricks.BRIK_DOG_009.CollectResult(_dogId, out _result, out _error, out _callId, out _session, out _toolName, out _argsJson);
                    AuditBrick("ok", "T_ReadAll", "dog.collect_result", frame);
                }
                catch (Exception ex)
                {
                    ok = false;
                    AuditBrick("error", "T_ReadAll", "dog.collect_result", frame);
                }
                // [段5] 结果转移——bool 驱动首项成功 / 次项失败 / 多路 switch 分发
                if (ok)
                {
                    S_Poster_Enter_Collect();
                }
                else
                {
                    S_Poster_Enter_Failed();
                }
            }
            // [段7] τ 时限——Cube 步进 + 耗尽 → 次项（超时走失败分支）
            else if ((P_Closed) && (_S_Poster_State == S_Poster_State.WaitClosed) && T_ReadAll_Cube.IsRunning())
            {
                T_ReadAll_Cube.TickFrame();
                if (T_ReadAll_Cube.IsExpired())
                {
                    S_Poster_Enter_Failed();
                }
            }
        }

        /// <summary>
        /// T_TimeoutFinish 控制律——条件 + 操作 → 结果
        /// </summary>
        /// <param name="frame">全局帧号</param>
        private void T_TimeoutFinish_Execute(int frame)
        {
            // [段1] 条件守卫——全部成立 → 触发
            if ((P_Timeout) && (_S_Poster_State == S_Poster_State.WaitClosed) && T_TimeoutFinish_Cube.IsIdle())
            {
                T_TimeoutFinish_Cube.Start();
                // [段4] 操作——顺序执行 + try-catch 隔离 + 自动审计
                bool ok = false;
                try
                {
                    AuditBrick("invoke", "T_TimeoutFinish", "dog.finish", frame);
                    ok = Mau.Bricks.BRIK_DOG_008.Finish(_dogId);
                    AuditBrick("ok", "T_TimeoutFinish", "dog.finish", frame);
                }
                catch (Exception ex)
                {
                    ok = false;
                    AuditBrick("error", "T_TimeoutFinish", "dog.finish", frame);
                }
                // [段5] 结果转移——bool 驱动首项成功 / 次项失败 / 多路 switch 分发
                if (ok)
                {
                    S_Poster_Enter_Dispatch();
                }
                else
                {
                    S_Poster_Enter_Failed();
                }
            }
            // [段7] τ 时限——Cube 步进 + 耗尽 → 次项（超时走失败分支）
            else if ((P_Timeout) && (_S_Poster_State == S_Poster_State.WaitClosed) && T_TimeoutFinish_Cube.IsRunning())
            {
                T_TimeoutFinish_Cube.TickFrame();
                if (T_TimeoutFinish_Cube.IsExpired())
                {
                    S_Poster_Enter_Failed();
                }
            }
        }

        /// <summary>
        /// T_PushTool 控制律——条件 + 操作 → 结果
        /// </summary>
        /// <param name="frame">全局帧号</param>
        private void T_PushTool_Execute(int frame)
        {
            // [段1] 条件守卫——全部成立 → 触发
            if (_S_Poster_State == S_Poster_State.Collect && T_PushTool_Cube.IsIdle())
            {
                T_PushTool_Cube.Start();
                // [段4] 操作——顺序执行 + try-catch 隔离 + 自动审计
                bool ok = false;
                try
                {
                    AuditBrick("invoke", "T_PushTool", "llm.ctx_push_tool", frame);
                    ok = Mau.Bricks.BRIK_LLM_010.CtxPushTool(_session, _callId, _toolName, _argsJson, _result);
                    AuditBrick("ok", "T_PushTool", "llm.ctx_push_tool", frame);
                }
                catch (Exception ex)
                {
                    ok = false;
                    AuditBrick("error", "T_PushTool", "llm.ctx_push_tool", frame);
                }
                // [段5] 结果转移——bool 驱动首项成功 / 次项失败 / 多路 switch 分发
                if (ok)
                {
                    S_Poster_Enter_Finish();
                }
                else
                {
                    S_Poster_Enter_Failed();
                }
            }
            // [段7] τ 时限——Cube 步进 + 耗尽 → 次项（超时走失败分支）
            else if (_S_Poster_State == S_Poster_State.Collect && T_PushTool_Cube.IsRunning())
            {
                T_PushTool_Cube.TickFrame();
                if (T_PushTool_Cube.IsExpired())
                {
                    S_Poster_Enter_Failed();
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
            if (_S_Poster_State == S_Poster_State.Finish && T_Finish_Cube.IsIdle())
            {
                T_Finish_Cube.Start();
                // [段4] 操作——顺序执行 + try-catch 隔离 + 自动审计
                bool ok = false;
                try
                {
                    AuditBrick("invoke", "T_Finish", "dog.finish", frame);
                    ok = Mau.Bricks.BRIK_DOG_008.Finish(_dogId);
                    AuditBrick("ok", "T_Finish", "dog.finish", frame);
                }
                catch (Exception ex)
                {
                    ok = false;
                    AuditBrick("error", "T_Finish", "dog.finish", frame);
                }
                // [段5] 结果转移——bool 驱动首项成功 / 次项失败 / 多路 switch 分发
                if (ok)
                {
                    S_Poster_Enter_Dispatch();
                }
                else
                {
                    S_Poster_Enter_Failed();
                }
            }
            // [段7] τ 时限——Cube 步进 + 耗尽 → 次项（超时走失败分支）
            else if (_S_Poster_State == S_Poster_State.Finish && T_Finish_Cube.IsRunning())
            {
                T_Finish_Cube.TickFrame();
                if (T_Finish_Cube.IsExpired())
                {
                    S_Poster_Enter_Failed();
                }
            }
        }

        /// <summary>
        /// T_Summarize 控制律——条件 + 操作 → 结果
        /// </summary>
        /// <param name="frame">全局帧号</param>
        private void T_Summarize_Execute(int frame)
        {
            // [段1] 条件守卫——全部成立 → 触发
            if (_S_Poster_State == S_Poster_State.Summarize && T_Summarize_Cube.IsIdle())
            {
                T_Summarize_Cube.Start();
                // [段4] 操作——顺序执行 + try-catch 隔离 + 自动审计
                bool ok = false;
                try
                {
                    AuditBrick("invoke", "T_Summarize", "data.box_set", frame);
                    ok = Mau.Bricks.BRIK_DATA_003.Set(_sessionKey, "tools_done", 1);
                    AuditBrick("ok", "T_Summarize", "data.box_set", frame);
                }
                catch (Exception ex)
                {
                    ok = false;
                    AuditBrick("error", "T_Summarize", "data.box_set", frame);
                }
                // [段5] 结果转移——bool 驱动首项成功 / 次项失败 / 多路 switch 分发
                if (ok)
                {
                    S_Poster_Enter_ClearReq();
                }
                else
                {
                    S_Poster_Enter_Failed();
                }
            }
            // [段7] τ 时限——Cube 步进 + 耗尽 → 次项（超时走失败分支）
            else if (_S_Poster_State == S_Poster_State.Summarize && T_Summarize_Cube.IsRunning())
            {
                T_Summarize_Cube.TickFrame();
                if (T_Summarize_Cube.IsExpired())
                {
                    S_Poster_Enter_Failed();
                }
            }
        }

        /// <summary>
        /// T_ClearReq 控制律——条件 + 操作 → 结果
        /// </summary>
        /// <param name="frame">全局帧号</param>
        private void T_ClearReq_Execute(int frame)
        {
            // [段1] 条件守卫——全部成立 → 触发
            if (_S_Poster_State == S_Poster_State.ClearReq && T_ClearReq_Cube.IsIdle())
            {
                T_ClearReq_Cube.Start();
                // [段4] 操作——顺序执行 + try-catch 隔离 + 自动审计
                bool ok = false;
                try
                {
                    AuditBrick("invoke", "T_ClearReq", "data.box_set", frame);
                    ok = Mau.Bricks.BRIK_DATA_003.Set(_sessionKey, "tool_req_flag", 0);
                    AuditBrick("ok", "T_ClearReq", "data.box_set", frame);
                }
                catch (Exception ex)
                {
                    ok = false;
                    AuditBrick("error", "T_ClearReq", "data.box_set", frame);
                }
                // [段5] 结果转移——bool 驱动首项成功 / 次项失败 / 多路 switch 分发
                if (ok)
                {
                    S_Poster_Enter_Polling();
                }
                else
                {
                    S_Poster_Enter_Failed();
                }
            }
            // [段7] τ 时限——Cube 步进 + 耗尽 → 次项（超时走失败分支）
            else if (_S_Poster_State == S_Poster_State.ClearReq && T_ClearReq_Cube.IsRunning())
            {
                T_ClearReq_Cube.TickFrame();
                if (T_ClearReq_Cube.IsExpired())
                {
                    S_Poster_Enter_Failed();
                }
            }
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
// #MAU_CHECKSUM:SHA256:E23125AEDD856CAB0EFBCD0F92D6EA0F0D41B710511612B3ABCC83AD7F0DBFC5