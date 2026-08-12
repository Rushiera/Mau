// ═══ Ch4Uipet 生成物 — Mau v2.0 翻译器 ═══
// 生成物由翻译器确定性输出——手工修改无效，改 .mau 后重新生成
using System;
using System.Collections.Generic;
using Mau.Runtime;
using System.IO;
using System.Text;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Mau.Generated
{
    /// <summary>
    /// 生成物——Ch4Uipet 受控系统
    /// </summary>
    public sealed class Ch4Uipet
    {
        // [注入字段]
        private string? _sessionKey;
        private long? _ownerId;
        private string[]? _cmdKeyList;

        /// <summary>
        /// 注入字段设置——sessionKey（纯赋值，不置位信号）
        /// </summary>
        /// <param name="value">注入值</param>
        public void SetSessionKey(string? value) { _sessionKey = value; }

        /// <summary>
        /// 注入字段设置——ownerId（纯赋值，不置位信号）
        /// </summary>
        /// <param name="value">注入值</param>
        public void SetOwnerId(long? value) { _ownerId = value; }

        /// <summary>
        /// 注入字段设置——cmdKeyList（纯赋值，不置位信号）
        /// </summary>
        /// <param name="value">注入值</param>
        public void SetCmdKeyList(string[]? value) { _cmdKeyList = value; }

        private enum S_UiPet_State { Idle, Reg, SnapChat, PushChat, SnapHome, PushHome, SnapConfig, PushConfig, PollCmd, Route, Route2, Open, Toggle, Select, PushCatCfg, Save, ProfSave, ProfDel, ProfAct, OpenDir, Flash, Notify, Failed }
        private S_UiPet_State _S_UiPet_State;

        // [命题]
        private bool P_Init;

        // [积木输出端口]
        private string _chatJson = default;
        private string _homeJson = default;
        private string _configJson = default;
        private bool _hasCommands = default;
        private string[] _cmdKeys = default;
        private int[] _cmdValues = default;
        private string[] _cmdTexts = default;
        private string _text = default;
        private string _activeKey = default;
        private string _json = default;
        private string _profileId = default;

        // [控制律 Cube]
        private readonly Cube T_Init_Cube = new Cube(5);
        private readonly Cube T_Register_Cube = new Cube(5);
        private readonly Cube T_SnapChat_Cube = new Cube(5);
        private readonly Cube T_PushChat_Cube = new Cube(5);
        private readonly Cube T_SnapHome_Cube = new Cube(5);
        private readonly Cube T_PushHome_Cube = new Cube(5);
        private readonly Cube T_SnapConfig_Cube = new Cube(5);
        private readonly Cube T_PushConfig_Cube = new Cube(5);
        private readonly Cube T_PollCmd_Cube = new Cube(5);
        private readonly Cube T_GetKey_Cube = new Cube(5);
        private readonly Cube T_Route_Cube = new Cube(5);
        private readonly Cube T_Open_Cube = new Cube(5);
        private readonly Cube T_Toggle_Cube = new Cube(5);
        private readonly Cube T_Select_Cube = new Cube(5);
        private readonly Cube T_PushCatCfg_Cube = new Cube(5);
        private readonly Cube T_Save_Cube = new Cube(5);
        private readonly Cube T_ProfSave_Cube = new Cube(5);
        private readonly Cube T_ProfDel_Cube = new Cube(5);
        private readonly Cube T_ProfAct_Cube = new Cube(5);
        private readonly Cube T_OpenDir_Cube = new Cube(5);
        private readonly Cube T_Flash_Cube = new Cube(5);
        private readonly Cube T_Notify_Cube = new Cube(5);

        /// <summary>
        /// 构造——初始状态置位
        /// </summary>
        public Ch4Uipet()
        {
            S_UiPet_Enter_Idle();
        }

        /// <summary>
        /// 进入 S_UiPet.Idle——单值赋值（互斥由类型系统保证）
        /// </summary>
        private void S_UiPet_Enter_Idle()
        {
            _S_UiPet_State = S_UiPet_State.Idle;
        }

        /// <summary>
        /// 进入 S_UiPet.Reg——单值赋值（互斥由类型系统保证）
        /// </summary>
        private void S_UiPet_Enter_Reg()
        {
            _S_UiPet_State = S_UiPet_State.Reg;
        }

        /// <summary>
        /// 进入 S_UiPet.SnapChat——单值赋值（互斥由类型系统保证）
        /// </summary>
        private void S_UiPet_Enter_SnapChat()
        {
            _S_UiPet_State = S_UiPet_State.SnapChat;
        }

        /// <summary>
        /// 进入 S_UiPet.PushChat——单值赋值（互斥由类型系统保证）
        /// </summary>
        private void S_UiPet_Enter_PushChat()
        {
            _S_UiPet_State = S_UiPet_State.PushChat;
        }

        /// <summary>
        /// 进入 S_UiPet.SnapHome——单值赋值（互斥由类型系统保证）
        /// </summary>
        private void S_UiPet_Enter_SnapHome()
        {
            _S_UiPet_State = S_UiPet_State.SnapHome;
        }

        /// <summary>
        /// 进入 S_UiPet.PushHome——单值赋值（互斥由类型系统保证）
        /// </summary>
        private void S_UiPet_Enter_PushHome()
        {
            _S_UiPet_State = S_UiPet_State.PushHome;
        }

        /// <summary>
        /// 进入 S_UiPet.SnapConfig——单值赋值（互斥由类型系统保证）
        /// </summary>
        private void S_UiPet_Enter_SnapConfig()
        {
            _S_UiPet_State = S_UiPet_State.SnapConfig;
        }

        /// <summary>
        /// 进入 S_UiPet.PushConfig——单值赋值（互斥由类型系统保证）
        /// </summary>
        private void S_UiPet_Enter_PushConfig()
        {
            _S_UiPet_State = S_UiPet_State.PushConfig;
        }

        /// <summary>
        /// 进入 S_UiPet.PollCmd——单值赋值（互斥由类型系统保证）
        /// </summary>
        private void S_UiPet_Enter_PollCmd()
        {
            _S_UiPet_State = S_UiPet_State.PollCmd;
        }

        /// <summary>
        /// 进入 S_UiPet.Route——单值赋值（互斥由类型系统保证）
        /// </summary>
        private void S_UiPet_Enter_Route()
        {
            _S_UiPet_State = S_UiPet_State.Route;
        }

        /// <summary>
        /// 进入 S_UiPet.Route2——单值赋值（互斥由类型系统保证）
        /// </summary>
        private void S_UiPet_Enter_Route2()
        {
            _S_UiPet_State = S_UiPet_State.Route2;
        }

        /// <summary>
        /// 进入 S_UiPet.Open——单值赋值（互斥由类型系统保证）
        /// </summary>
        private void S_UiPet_Enter_Open()
        {
            _S_UiPet_State = S_UiPet_State.Open;
        }

        /// <summary>
        /// 进入 S_UiPet.Toggle——单值赋值（互斥由类型系统保证）
        /// </summary>
        private void S_UiPet_Enter_Toggle()
        {
            _S_UiPet_State = S_UiPet_State.Toggle;
        }

        /// <summary>
        /// 进入 S_UiPet.Select——单值赋值（互斥由类型系统保证）
        /// </summary>
        private void S_UiPet_Enter_Select()
        {
            _S_UiPet_State = S_UiPet_State.Select;
        }

        /// <summary>
        /// 进入 S_UiPet.PushCatCfg——单值赋值（互斥由类型系统保证）
        /// </summary>
        private void S_UiPet_Enter_PushCatCfg()
        {
            _S_UiPet_State = S_UiPet_State.PushCatCfg;
        }

        /// <summary>
        /// 进入 S_UiPet.Save——单值赋值（互斥由类型系统保证）
        /// </summary>
        private void S_UiPet_Enter_Save()
        {
            _S_UiPet_State = S_UiPet_State.Save;
        }

        /// <summary>
        /// 进入 S_UiPet.ProfSave——单值赋值（互斥由类型系统保证）
        /// </summary>
        private void S_UiPet_Enter_ProfSave()
        {
            _S_UiPet_State = S_UiPet_State.ProfSave;
        }

        /// <summary>
        /// 进入 S_UiPet.ProfDel——单值赋值（互斥由类型系统保证）
        /// </summary>
        private void S_UiPet_Enter_ProfDel()
        {
            _S_UiPet_State = S_UiPet_State.ProfDel;
        }

        /// <summary>
        /// 进入 S_UiPet.ProfAct——单值赋值（互斥由类型系统保证）
        /// </summary>
        private void S_UiPet_Enter_ProfAct()
        {
            _S_UiPet_State = S_UiPet_State.ProfAct;
        }

        /// <summary>
        /// 进入 S_UiPet.OpenDir——单值赋值（互斥由类型系统保证）
        /// </summary>
        private void S_UiPet_Enter_OpenDir()
        {
            _S_UiPet_State = S_UiPet_State.OpenDir;
        }

        /// <summary>
        /// 进入 S_UiPet.Flash——单值赋值（互斥由类型系统保证）
        /// </summary>
        private void S_UiPet_Enter_Flash()
        {
            _S_UiPet_State = S_UiPet_State.Flash;
        }

        /// <summary>
        /// 进入 S_UiPet.Notify——单值赋值（互斥由类型系统保证）
        /// </summary>
        private void S_UiPet_Enter_Notify()
        {
            _S_UiPet_State = S_UiPet_State.Notify;
        }

        /// <summary>
        /// 进入 S_UiPet.Failed——单值赋值（互斥由类型系统保证）
        /// </summary>
        private void S_UiPet_Enter_Failed()
        {
            _S_UiPet_State = S_UiPet_State.Failed;
        }

        /// <summary>
        /// S_UiPet 是否处于 Idle
        /// </summary>
        public bool IsUiPetIdle() { return _S_UiPet_State == S_UiPet_State.Idle; }

        /// <summary>
        /// S_UiPet 是否处于 Reg
        /// </summary>
        public bool IsUiPetReg() { return _S_UiPet_State == S_UiPet_State.Reg; }

        /// <summary>
        /// S_UiPet 是否处于 SnapChat
        /// </summary>
        public bool IsUiPetSnapChat() { return _S_UiPet_State == S_UiPet_State.SnapChat; }

        /// <summary>
        /// S_UiPet 是否处于 PushChat
        /// </summary>
        public bool IsUiPetPushChat() { return _S_UiPet_State == S_UiPet_State.PushChat; }

        /// <summary>
        /// S_UiPet 是否处于 SnapHome
        /// </summary>
        public bool IsUiPetSnapHome() { return _S_UiPet_State == S_UiPet_State.SnapHome; }

        /// <summary>
        /// S_UiPet 是否处于 PushHome
        /// </summary>
        public bool IsUiPetPushHome() { return _S_UiPet_State == S_UiPet_State.PushHome; }

        /// <summary>
        /// S_UiPet 是否处于 SnapConfig
        /// </summary>
        public bool IsUiPetSnapConfig() { return _S_UiPet_State == S_UiPet_State.SnapConfig; }

        /// <summary>
        /// S_UiPet 是否处于 PushConfig
        /// </summary>
        public bool IsUiPetPushConfig() { return _S_UiPet_State == S_UiPet_State.PushConfig; }

        /// <summary>
        /// S_UiPet 是否处于 PollCmd
        /// </summary>
        public bool IsUiPetPollCmd() { return _S_UiPet_State == S_UiPet_State.PollCmd; }

        /// <summary>
        /// S_UiPet 是否处于 Route
        /// </summary>
        public bool IsUiPetRoute() { return _S_UiPet_State == S_UiPet_State.Route; }

        /// <summary>
        /// S_UiPet 是否处于 Route2
        /// </summary>
        public bool IsUiPetRoute2() { return _S_UiPet_State == S_UiPet_State.Route2; }

        /// <summary>
        /// S_UiPet 是否处于 Open
        /// </summary>
        public bool IsUiPetOpen() { return _S_UiPet_State == S_UiPet_State.Open; }

        /// <summary>
        /// S_UiPet 是否处于 Toggle
        /// </summary>
        public bool IsUiPetToggle() { return _S_UiPet_State == S_UiPet_State.Toggle; }

        /// <summary>
        /// S_UiPet 是否处于 Select
        /// </summary>
        public bool IsUiPetSelect() { return _S_UiPet_State == S_UiPet_State.Select; }

        /// <summary>
        /// S_UiPet 是否处于 PushCatCfg
        /// </summary>
        public bool IsUiPetPushCatCfg() { return _S_UiPet_State == S_UiPet_State.PushCatCfg; }

        /// <summary>
        /// S_UiPet 是否处于 Save
        /// </summary>
        public bool IsUiPetSave() { return _S_UiPet_State == S_UiPet_State.Save; }

        /// <summary>
        /// S_UiPet 是否处于 ProfSave
        /// </summary>
        public bool IsUiPetProfSave() { return _S_UiPet_State == S_UiPet_State.ProfSave; }

        /// <summary>
        /// S_UiPet 是否处于 ProfDel
        /// </summary>
        public bool IsUiPetProfDel() { return _S_UiPet_State == S_UiPet_State.ProfDel; }

        /// <summary>
        /// S_UiPet 是否处于 ProfAct
        /// </summary>
        public bool IsUiPetProfAct() { return _S_UiPet_State == S_UiPet_State.ProfAct; }

        /// <summary>
        /// S_UiPet 是否处于 OpenDir
        /// </summary>
        public bool IsUiPetOpenDir() { return _S_UiPet_State == S_UiPet_State.OpenDir; }

        /// <summary>
        /// S_UiPet 是否处于 Flash
        /// </summary>
        public bool IsUiPetFlash() { return _S_UiPet_State == S_UiPet_State.Flash; }

        /// <summary>
        /// S_UiPet 是否处于 Notify
        /// </summary>
        public bool IsUiPetNotify() { return _S_UiPet_State == S_UiPet_State.Notify; }

        /// <summary>
        /// S_UiPet 是否处于 Failed
        /// </summary>
        public bool IsUiPetFailed() { return _S_UiPet_State == S_UiPet_State.Failed; }

        /// <summary>
        /// S_UiPet 当前状态名
        /// </summary>
        public string GetUiPetState()
        {
            switch (_S_UiPet_State)
            {
                case S_UiPet_State.Idle: return "Idle";
                case S_UiPet_State.Reg: return "Reg";
                case S_UiPet_State.SnapChat: return "SnapChat";
                case S_UiPet_State.PushChat: return "PushChat";
                case S_UiPet_State.SnapHome: return "SnapHome";
                case S_UiPet_State.PushHome: return "PushHome";
                case S_UiPet_State.SnapConfig: return "SnapConfig";
                case S_UiPet_State.PushConfig: return "PushConfig";
                case S_UiPet_State.PollCmd: return "PollCmd";
                case S_UiPet_State.Route: return "Route";
                case S_UiPet_State.Route2: return "Route2";
                case S_UiPet_State.Open: return "Open";
                case S_UiPet_State.Toggle: return "Toggle";
                case S_UiPet_State.Select: return "Select";
                case S_UiPet_State.PushCatCfg: return "PushCatCfg";
                case S_UiPet_State.Save: return "Save";
                case S_UiPet_State.ProfSave: return "ProfSave";
                case S_UiPet_State.ProfDel: return "ProfDel";
                case S_UiPet_State.ProfAct: return "ProfAct";
                case S_UiPet_State.OpenDir: return "OpenDir";
                case S_UiPet_State.Flash: return "Flash";
                case S_UiPet_State.Notify: return "Notify";
                case S_UiPet_State.Failed: return "Failed";
                default: return "Unknown";
            }
        }

        /// <summary>
        /// 帧驱动——测量采样 + 控制律守卫（声明顺序）
        /// </summary>
        /// <param name="frame">全局帧号</param>
        public void Tick(int frame)
        {
            T_Init_Execute(frame);
            T_Register_Execute(frame);
            T_SnapChat_Execute(frame);
            T_PushChat_Execute(frame);
            T_SnapHome_Execute(frame);
            T_PushHome_Execute(frame);
            T_SnapConfig_Execute(frame);
            T_PushConfig_Execute(frame);
            T_PollCmd_Execute(frame);
            T_GetKey_Execute(frame);
            T_Route_Execute(frame);
            T_Open_Execute(frame);
            T_Toggle_Execute(frame);
            T_Select_Execute(frame);
            T_PushCatCfg_Execute(frame);
            T_Save_Execute(frame);
            T_ProfSave_Execute(frame);
            T_ProfDel_Execute(frame);
            T_ProfAct_Execute(frame);
            T_OpenDir_Execute(frame);
            T_Flash_Execute(frame);
            T_Notify_Execute(frame);
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
                    AuditBrick("invoke", "T_Init", "log.write", frame);
                    ok = Mau.Bricks.BRIK_LOG_001.Write("CH4.UiPet", 0, "ready");
                    AuditBrick("ok", "T_Init", "log.write", frame);
                }
                catch (Exception ex)
                {
                    ok = false;
                    AuditBrick("error", "T_Init", "log.write", frame);
                }
                // [段5] 结果转移——bool 驱动首项成功 / 次项失败 / 多路 switch 分发
                if (ok)
                {
                    S_UiPet_Enter_Reg();
                }
                else
                {
                    S_UiPet_Enter_Failed();
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
                    S_UiPet_Enter_Failed();
                    T_Init_Cube.Reset();
                }
            }
        }

        /// <summary>
        /// T_Register 控制律——条件 + 操作 → 结果
        /// </summary>
        /// <param name="frame">全局帧号</param>
        private void T_Register_Execute(int frame)
        {
            // [段1] 条件守卫——全部成立 → 触发
            if (_S_UiPet_State == S_UiPet_State.Reg && T_Register_Cube.IsIdle())
            {
                T_Register_Cube.Start();
                // [段4] 操作——顺序执行 + try-catch 隔离 + 自动审计
                bool ok = false;
                try
                {
                    AuditBrick("invoke", "T_Register", "cmd.register", frame);
                    ok = Mau.Bricks.BRIK_CMD_001.Register((_ownerId ?? 0), _cmdKeyList);
                    AuditBrick("ok", "T_Register", "cmd.register", frame);
                }
                catch (Exception ex)
                {
                    ok = false;
                    AuditBrick("error", "T_Register", "cmd.register", frame);
                }
                // [段5] 结果转移——bool 驱动首项成功 / 次项失败 / 多路 switch 分发
                if (ok)
                {
                    S_UiPet_Enter_SnapChat();
                }
                else
                {
                    S_UiPet_Enter_Failed();
                }
                // [段6b] Cube 复位——触发完成（成功/失败均复位，允许再次触发）
                T_Register_Cube.Complete();
            }
            // [段7] τ 时限——Cube 步进 + 耗尽 → 次项（超时走失败分支）
            else if (_S_UiPet_State == S_UiPet_State.Reg && T_Register_Cube.IsRunning())
            {
                T_Register_Cube.TickFrame();
                if (T_Register_Cube.IsExpired())
                {
                    S_UiPet_Enter_Failed();
                    T_Register_Cube.Reset();
                }
            }
        }

        /// <summary>
        /// T_SnapChat 控制律——条件 + 操作 → 结果
        /// </summary>
        /// <param name="frame">全局帧号</param>
        private void T_SnapChat_Execute(int frame)
        {
            // [段1] 条件守卫——全部成立 → 触发
            if (_S_UiPet_State == S_UiPet_State.SnapChat && T_SnapChat_Cube.IsIdle())
            {
                T_SnapChat_Cube.Start();
                // [段4] 操作——顺序执行 + try-catch 隔离 + 自动审计
                bool ok = false;
                try
                {
                    AuditBrick("invoke", "T_SnapChat", "ui.snapshot_chat", frame);
                    ok = Mau.Bricks.BRIK_UI_001.SnapshotChat(_sessionKey, out _chatJson);
                    AuditBrick("ok", "T_SnapChat", "ui.snapshot_chat", frame);
                }
                catch (Exception ex)
                {
                    ok = false;
                    AuditBrick("error", "T_SnapChat", "ui.snapshot_chat", frame);
                }
                // [段5] 结果转移——bool 驱动首项成功 / 次项失败 / 多路 switch 分发
                if (ok)
                {
                    S_UiPet_Enter_PushChat();
                }
                else
                {
                    S_UiPet_Enter_SnapHome();
                }
                // [段6b] Cube 复位——触发完成（成功/失败均复位，允许再次触发）
                T_SnapChat_Cube.Complete();
            }
            // [段7] τ 时限——Cube 步进 + 耗尽 → 次项（超时走失败分支）
            else if (_S_UiPet_State == S_UiPet_State.SnapChat && T_SnapChat_Cube.IsRunning())
            {
                T_SnapChat_Cube.TickFrame();
                if (T_SnapChat_Cube.IsExpired())
                {
                    S_UiPet_Enter_SnapHome();
                    T_SnapChat_Cube.Reset();
                }
            }
        }

        /// <summary>
        /// T_PushChat 控制律——条件 + 操作 → 结果
        /// </summary>
        /// <param name="frame">全局帧号</param>
        private void T_PushChat_Execute(int frame)
        {
            // [段1] 条件守卫——全部成立 → 触发
            if (_S_UiPet_State == S_UiPet_State.PushChat && T_PushChat_Cube.IsIdle())
            {
                T_PushChat_Cube.Start();
                // [段4] 操作——顺序执行 + try-catch 隔离 + 自动审计
                bool ok = false;
                try
                {
                    AuditBrick("invoke", "T_PushChat", "ui.snapshot_push", frame);
                    ok = Mau.Bricks.BRIK_UI_002.SnapshotPush(_sessionKey, _chatJson);
                    AuditBrick("ok", "T_PushChat", "ui.snapshot_push", frame);
                }
                catch (Exception ex)
                {
                    ok = false;
                    AuditBrick("error", "T_PushChat", "ui.snapshot_push", frame);
                }
                // [段5] 结果转移——bool 驱动首项成功 / 次项失败 / 多路 switch 分发
                if (ok)
                {
                    S_UiPet_Enter_SnapHome();
                }
                else
                {
                    S_UiPet_Enter_Failed();
                }
                // [段6b] Cube 复位——触发完成（成功/失败均复位，允许再次触发）
                T_PushChat_Cube.Complete();
            }
            // [段7] τ 时限——Cube 步进 + 耗尽 → 次项（超时走失败分支）
            else if (_S_UiPet_State == S_UiPet_State.PushChat && T_PushChat_Cube.IsRunning())
            {
                T_PushChat_Cube.TickFrame();
                if (T_PushChat_Cube.IsExpired())
                {
                    S_UiPet_Enter_Failed();
                    T_PushChat_Cube.Reset();
                }
            }
        }

        /// <summary>
        /// T_SnapHome 控制律——条件 + 操作 → 结果
        /// </summary>
        /// <param name="frame">全局帧号</param>
        private void T_SnapHome_Execute(int frame)
        {
            // [段1] 条件守卫——全部成立 → 触发
            if (_S_UiPet_State == S_UiPet_State.SnapHome && T_SnapHome_Cube.IsIdle())
            {
                T_SnapHome_Cube.Start();
                // [段4] 操作——顺序执行 + try-catch 隔离 + 自动审计
                bool ok = false;
                try
                {
                    AuditBrick("invoke", "T_SnapHome", "ui.snapshot_home", frame);
                    ok = Mau.Bricks.BRIK_UI_004.SnapshotHome(out _homeJson);
                    AuditBrick("ok", "T_SnapHome", "ui.snapshot_home", frame);
                }
                catch (Exception ex)
                {
                    ok = false;
                    AuditBrick("error", "T_SnapHome", "ui.snapshot_home", frame);
                }
                // [段5] 结果转移——bool 驱动首项成功 / 次项失败 / 多路 switch 分发
                if (ok)
                {
                    S_UiPet_Enter_PushHome();
                }
                else
                {
                    S_UiPet_Enter_SnapConfig();
                }
                // [段6b] Cube 复位——触发完成（成功/失败均复位，允许再次触发）
                T_SnapHome_Cube.Complete();
            }
            // [段7] τ 时限——Cube 步进 + 耗尽 → 次项（超时走失败分支）
            else if (_S_UiPet_State == S_UiPet_State.SnapHome && T_SnapHome_Cube.IsRunning())
            {
                T_SnapHome_Cube.TickFrame();
                if (T_SnapHome_Cube.IsExpired())
                {
                    S_UiPet_Enter_SnapConfig();
                    T_SnapHome_Cube.Reset();
                }
            }
        }

        /// <summary>
        /// T_PushHome 控制律——条件 + 操作 → 结果
        /// </summary>
        /// <param name="frame">全局帧号</param>
        private void T_PushHome_Execute(int frame)
        {
            // [段1] 条件守卫——全部成立 → 触发
            if (_S_UiPet_State == S_UiPet_State.PushHome && T_PushHome_Cube.IsIdle())
            {
                T_PushHome_Cube.Start();
                // [段4] 操作——顺序执行 + try-catch 隔离 + 自动审计
                bool ok = false;
                try
                {
                    AuditBrick("invoke", "T_PushHome", "ui.snapshot_push", frame);
                    ok = Mau.Bricks.BRIK_UI_002.SnapshotPush("home", _homeJson);
                    AuditBrick("ok", "T_PushHome", "ui.snapshot_push", frame);
                }
                catch (Exception ex)
                {
                    ok = false;
                    AuditBrick("error", "T_PushHome", "ui.snapshot_push", frame);
                }
                // [段5] 结果转移——bool 驱动首项成功 / 次项失败 / 多路 switch 分发
                if (ok)
                {
                    S_UiPet_Enter_SnapConfig();
                }
                else
                {
                    S_UiPet_Enter_Failed();
                }
                // [段6b] Cube 复位——触发完成（成功/失败均复位，允许再次触发）
                T_PushHome_Cube.Complete();
            }
            // [段7] τ 时限——Cube 步进 + 耗尽 → 次项（超时走失败分支）
            else if (_S_UiPet_State == S_UiPet_State.PushHome && T_PushHome_Cube.IsRunning())
            {
                T_PushHome_Cube.TickFrame();
                if (T_PushHome_Cube.IsExpired())
                {
                    S_UiPet_Enter_Failed();
                    T_PushHome_Cube.Reset();
                }
            }
        }

        /// <summary>
        /// T_SnapConfig 控制律——条件 + 操作 → 结果
        /// </summary>
        /// <param name="frame">全局帧号</param>
        private void T_SnapConfig_Execute(int frame)
        {
            // [段1] 条件守卫——全部成立 → 触发
            if (_S_UiPet_State == S_UiPet_State.SnapConfig && T_SnapConfig_Cube.IsIdle())
            {
                T_SnapConfig_Cube.Start();
                // [段4] 操作——顺序执行 + try-catch 隔离 + 自动审计
                bool ok = false;
                try
                {
                    AuditBrick("invoke", "T_SnapConfig", "ui.snapshot_config", frame);
                    ok = Mau.Bricks.BRIK_UI_005.SnapshotConfig(out _configJson);
                    AuditBrick("ok", "T_SnapConfig", "ui.snapshot_config", frame);
                }
                catch (Exception ex)
                {
                    ok = false;
                    AuditBrick("error", "T_SnapConfig", "ui.snapshot_config", frame);
                }
                // [段5] 结果转移——bool 驱动首项成功 / 次项失败 / 多路 switch 分发
                if (ok)
                {
                    S_UiPet_Enter_PushConfig();
                }
                else
                {
                    S_UiPet_Enter_PollCmd();
                }
                // [段6b] Cube 复位——触发完成（成功/失败均复位，允许再次触发）
                T_SnapConfig_Cube.Complete();
            }
            // [段7] τ 时限——Cube 步进 + 耗尽 → 次项（超时走失败分支）
            else if (_S_UiPet_State == S_UiPet_State.SnapConfig && T_SnapConfig_Cube.IsRunning())
            {
                T_SnapConfig_Cube.TickFrame();
                if (T_SnapConfig_Cube.IsExpired())
                {
                    S_UiPet_Enter_PollCmd();
                    T_SnapConfig_Cube.Reset();
                }
            }
        }

        /// <summary>
        /// T_PushConfig 控制律——条件 + 操作 → 结果
        /// </summary>
        /// <param name="frame">全局帧号</param>
        private void T_PushConfig_Execute(int frame)
        {
            // [段1] 条件守卫——全部成立 → 触发
            if (_S_UiPet_State == S_UiPet_State.PushConfig && T_PushConfig_Cube.IsIdle())
            {
                T_PushConfig_Cube.Start();
                // [段4] 操作——顺序执行 + try-catch 隔离 + 自动审计
                bool ok = false;
                try
                {
                    AuditBrick("invoke", "T_PushConfig", "ui.snapshot_push", frame);
                    ok = Mau.Bricks.BRIK_UI_002.SnapshotPush("config", _configJson);
                    AuditBrick("ok", "T_PushConfig", "ui.snapshot_push", frame);
                }
                catch (Exception ex)
                {
                    ok = false;
                    AuditBrick("error", "T_PushConfig", "ui.snapshot_push", frame);
                }
                // [段5] 结果转移——bool 驱动首项成功 / 次项失败 / 多路 switch 分发
                if (ok)
                {
                    S_UiPet_Enter_PollCmd();
                }
                else
                {
                    S_UiPet_Enter_Failed();
                }
                // [段6b] Cube 复位——触发完成（成功/失败均复位，允许再次触发）
                T_PushConfig_Cube.Complete();
            }
            // [段7] τ 时限——Cube 步进 + 耗尽 → 次项（超时走失败分支）
            else if (_S_UiPet_State == S_UiPet_State.PushConfig && T_PushConfig_Cube.IsRunning())
            {
                T_PushConfig_Cube.TickFrame();
                if (T_PushConfig_Cube.IsExpired())
                {
                    S_UiPet_Enter_Failed();
                    T_PushConfig_Cube.Reset();
                }
            }
        }

        /// <summary>
        /// T_PollCmd 控制律——条件 + 操作 → 结果
        /// </summary>
        /// <param name="frame">全局帧号</param>
        private void T_PollCmd_Execute(int frame)
        {
            // [段1] 条件守卫——全部成立 → 触发
            if (_S_UiPet_State == S_UiPet_State.PollCmd && T_PollCmd_Cube.IsIdle())
            {
                T_PollCmd_Cube.Start();
                // [段4] 操作——顺序执行 + try-catch 隔离 + 自动审计
                bool ok = false;
                try
                {
                    AuditBrick("invoke", "T_PollCmd", "cmd.consume", frame);
                    ok = Mau.Bricks.BRIK_CMD_003.Consume((_ownerId ?? 0), out _hasCommands, out _cmdKeys, out _cmdValues, out _cmdTexts, out _text);
                    AuditBrick("ok", "T_PollCmd", "cmd.consume", frame);
                }
                catch (Exception ex)
                {
                    ok = false;
                    AuditBrick("error", "T_PollCmd", "cmd.consume", frame);
                }
                // [段5] 结果转移——bool 驱动首项成功 / 次项失败 / 多路 switch 分发
                if (ok)
                {
                    S_UiPet_Enter_Route();
                }
                else
                {
                    S_UiPet_Enter_SnapChat();
                }
                // [段6b] Cube 复位——触发完成（成功/失败均复位，允许再次触发）
                T_PollCmd_Cube.Complete();
            }
            // [段7] τ 时限——Cube 步进 + 耗尽 → 次项（超时走失败分支）
            else if (_S_UiPet_State == S_UiPet_State.PollCmd && T_PollCmd_Cube.IsRunning())
            {
                T_PollCmd_Cube.TickFrame();
                if (T_PollCmd_Cube.IsExpired())
                {
                    S_UiPet_Enter_SnapChat();
                    T_PollCmd_Cube.Reset();
                }
            }
        }

        /// <summary>
        /// T_GetKey 控制律——条件 + 操作 → 结果
        /// </summary>
        /// <param name="frame">全局帧号</param>
        private void T_GetKey_Execute(int frame)
        {
            // [段1] 条件守卫——全部成立 → 触发
            if (_S_UiPet_State == S_UiPet_State.Route && T_GetKey_Cube.IsIdle())
            {
                T_GetKey_Cube.Start();
                // [段4] 操作——顺序执行 + try-catch 隔离 + 自动审计
                bool ok = false;
                try
                {
                    AuditBrick("invoke", "T_GetKey", "cmd.active_key", frame);
                    ok = Mau.Bricks.BRIK_CMD_008.ActiveKey(_cmdKeys, _cmdValues, _cmdTexts, out _activeKey);
                    AuditBrick("ok", "T_GetKey", "cmd.active_key", frame);
                }
                catch (Exception ex)
                {
                    ok = false;
                    AuditBrick("error", "T_GetKey", "cmd.active_key", frame);
                }
                // [段5] 结果转移——bool 驱动首项成功 / 次项失败 / 多路 switch 分发
                if (ok)
                {
                    S_UiPet_Enter_Route2();
                }
                else
                {
                    S_UiPet_Enter_SnapChat();
                }
                // [段6b] Cube 复位——触发完成（成功/失败均复位，允许再次触发）
                T_GetKey_Cube.Complete();
            }
            // [段7] τ 时限——Cube 步进 + 耗尽 → 次项（超时走失败分支）
            else if (_S_UiPet_State == S_UiPet_State.Route && T_GetKey_Cube.IsRunning())
            {
                T_GetKey_Cube.TickFrame();
                if (T_GetKey_Cube.IsExpired())
                {
                    S_UiPet_Enter_SnapChat();
                    T_GetKey_Cube.Reset();
                }
            }
        }

        /// <summary>
        /// T_Route 控制律——条件 + 操作 → 结果
        /// </summary>
        /// <param name="frame">全局帧号</param>
        private void T_Route_Execute(int frame)
        {
            // [段1] 条件守卫——全部成立 → 触发
            if (_S_UiPet_State == S_UiPet_State.Route2 && T_Route_Cube.IsIdle())
            {
                T_Route_Cube.Start();
                // [段4] 操作——名称返回积木（多路匹配，switch 分发源）
                string matched = "";
                bool ok = false;
                try
                {
                    AuditBrick("invoke", "T_Route", "cmd.match", frame);
                    matched = Mau.Bricks.BRIK_CMD_009.Match(_activeKey, new string[] { "Chat_UI_Open", "Chat_UI_Toggle", "Chat_UI_Select", "Chat_UI_SaveConfig", "Chat_UI_ProfileSave", "Chat_UI_ProfileDelete", "Chat_UI_ProfileActive", "Chat_UI_OpenDir", "Chat_UI_Flash", "Chat_UI_Notify" });
                    ok = matched.Length > 0;
                    AuditBrick("ok", "T_Route", "cmd.match", frame);
                }
                catch (Exception ex)
                {
                    ok = false;
                    AuditBrick("error", "T_Route", "cmd.match", frame);
                }
                // [段5] 结果转移——bool 驱动首项成功 / 次项失败 / 多路 switch 分发
                switch (matched)
                {
                    case "Chat_UI_Open": S_UiPet_Enter_Open(); break;
                    case "Chat_UI_Toggle": S_UiPet_Enter_Toggle(); break;
                    case "Chat_UI_Select": S_UiPet_Enter_Select(); break;
                    case "Chat_UI_SaveConfig": S_UiPet_Enter_Save(); break;
                    case "Chat_UI_ProfileSave": S_UiPet_Enter_ProfSave(); break;
                    case "Chat_UI_ProfileDelete": S_UiPet_Enter_ProfDel(); break;
                    case "Chat_UI_ProfileActive": S_UiPet_Enter_ProfAct(); break;
                    case "Chat_UI_OpenDir": S_UiPet_Enter_OpenDir(); break;
                    case "Chat_UI_Flash": S_UiPet_Enter_Flash(); break;
                    case "Chat_UI_Notify": S_UiPet_Enter_Notify(); break;
                    default: S_UiPet_Enter_SnapChat(); break;
                }
                // [段6b] Cube 复位——触发完成（成功/失败均复位，允许再次触发）
                T_Route_Cube.Complete();
            }
            // [段7] τ 时限——Cube 步进 + 耗尽 → 次项（超时走失败分支）
            else if (_S_UiPet_State == S_UiPet_State.Route2 && T_Route_Cube.IsRunning())
            {
                T_Route_Cube.TickFrame();
                if (T_Route_Cube.IsExpired())
                {
                    S_UiPet_Enter_Toggle();
                    T_Route_Cube.Reset();
                }
            }
        }

        /// <summary>
        /// T_Open 控制律——条件 + 操作 → 结果
        /// </summary>
        /// <param name="frame">全局帧号</param>
        private void T_Open_Execute(int frame)
        {
            // [段1] 条件守卫——全部成立 → 触发
            if (_S_UiPet_State == S_UiPet_State.Open && T_Open_Cube.IsIdle())
            {
                T_Open_Cube.Start();
                // [段4] 操作——顺序执行 + try-catch 隔离 + 自动审计
                bool ok = false;
                try
                {
                    AuditBrick("invoke", "T_Open", "ui.window_event", frame);
                    ok = Mau.Bricks.BRIK_UI_003.WindowEvent("Chat_UI_Open", _activeKey);
                    AuditBrick("ok", "T_Open", "ui.window_event", frame);
                }
                catch (Exception ex)
                {
                    ok = false;
                    AuditBrick("error", "T_Open", "ui.window_event", frame);
                }
                // [段5] 结果转移——bool 驱动首项成功 / 次项失败 / 多路 switch 分发
                if (ok)
                {
                    S_UiPet_Enter_SnapChat();
                }
                else
                {
                    S_UiPet_Enter_Failed();
                }
                // [段6b] Cube 复位——触发完成（成功/失败均复位，允许再次触发）
                T_Open_Cube.Complete();
            }
            // [段7] τ 时限——Cube 步进 + 耗尽 → 次项（超时走失败分支）
            else if (_S_UiPet_State == S_UiPet_State.Open && T_Open_Cube.IsRunning())
            {
                T_Open_Cube.TickFrame();
                if (T_Open_Cube.IsExpired())
                {
                    S_UiPet_Enter_Failed();
                    T_Open_Cube.Reset();
                }
            }
        }

        /// <summary>
        /// T_Toggle 控制律——条件 + 操作 → 结果
        /// </summary>
        /// <param name="frame">全局帧号</param>
        private void T_Toggle_Execute(int frame)
        {
            // [段1] 条件守卫——全部成立 → 触发
            if (_S_UiPet_State == S_UiPet_State.Toggle && T_Toggle_Cube.IsIdle())
            {
                T_Toggle_Cube.Start();
                // [段4] 操作——顺序执行 + try-catch 隔离 + 自动审计
                bool ok = false;
                try
                {
                    AuditBrick("invoke", "T_Toggle", "ui.window_event", frame);
                    ok = Mau.Bricks.BRIK_UI_003.WindowEvent("Chat_UI_Toggle", _activeKey);
                    AuditBrick("ok", "T_Toggle", "ui.window_event", frame);
                }
                catch (Exception ex)
                {
                    ok = false;
                    AuditBrick("error", "T_Toggle", "ui.window_event", frame);
                }
                // [段5] 结果转移——bool 驱动首项成功 / 次项失败 / 多路 switch 分发
                if (ok)
                {
                    S_UiPet_Enter_SnapChat();
                }
                else
                {
                    S_UiPet_Enter_Failed();
                }
                // [段6b] Cube 复位——触发完成（成功/失败均复位，允许再次触发）
                T_Toggle_Cube.Complete();
            }
            // [段7] τ 时限——Cube 步进 + 耗尽 → 次项（超时走失败分支）
            else if (_S_UiPet_State == S_UiPet_State.Toggle && T_Toggle_Cube.IsRunning())
            {
                T_Toggle_Cube.TickFrame();
                if (T_Toggle_Cube.IsExpired())
                {
                    S_UiPet_Enter_Failed();
                    T_Toggle_Cube.Reset();
                }
            }
        }

        /// <summary>
        /// T_Select 控制律——条件 + 操作 → 结果
        /// </summary>
        /// <param name="frame">全局帧号</param>
        private void T_Select_Execute(int frame)
        {
            // [段1] 条件守卫——全部成立 → 触发
            if (_S_UiPet_State == S_UiPet_State.Select && T_Select_Cube.IsIdle())
            {
                T_Select_Cube.Start();
                // [段4] 操作——顺序执行 + try-catch 隔离 + 自动审计
                bool ok = false;
                try
                {
                    AuditBrick("invoke", "T_Select", "ui.config_get", frame);
                    ok = Mau.Bricks.BRIK_UI_011.ConfigGet("active", out _json);
                    AuditBrick("ok", "T_Select", "ui.config_get", frame);
                }
                catch (Exception ex)
                {
                    ok = false;
                    AuditBrick("error", "T_Select", "ui.config_get", frame);
                }
                // [段5] 结果转移——bool 驱动首项成功 / 次项失败 / 多路 switch 分发
                if (ok)
                {
                    S_UiPet_Enter_PushCatCfg();
                }
                else
                {
                    S_UiPet_Enter_SnapChat();
                }
                // [段6b] Cube 复位——触发完成（成功/失败均复位，允许再次触发）
                T_Select_Cube.Complete();
            }
            // [段7] τ 时限——Cube 步进 + 耗尽 → 次项（超时走失败分支）
            else if (_S_UiPet_State == S_UiPet_State.Select && T_Select_Cube.IsRunning())
            {
                T_Select_Cube.TickFrame();
                if (T_Select_Cube.IsExpired())
                {
                    S_UiPet_Enter_SnapChat();
                    T_Select_Cube.Reset();
                }
            }
        }

        /// <summary>
        /// T_PushCatCfg 控制律——条件 + 操作 → 结果
        /// </summary>
        /// <param name="frame">全局帧号</param>
        private void T_PushCatCfg_Execute(int frame)
        {
            // [段1] 条件守卫——全部成立 → 触发
            if (_S_UiPet_State == S_UiPet_State.PushCatCfg && T_PushCatCfg_Cube.IsIdle())
            {
                T_PushCatCfg_Cube.Start();
                // [段4] 操作——顺序执行 + try-catch 隔离 + 自动审计
                bool ok = false;
                try
                {
                    AuditBrick("invoke", "T_PushCatCfg", "ui.snapshot_push", frame);
                    ok = Mau.Bricks.BRIK_UI_002.SnapshotPush("catcfg", _json);
                    AuditBrick("ok", "T_PushCatCfg", "ui.snapshot_push", frame);
                }
                catch (Exception ex)
                {
                    ok = false;
                    AuditBrick("error", "T_PushCatCfg", "ui.snapshot_push", frame);
                }
                // [段5] 结果转移——bool 驱动首项成功 / 次项失败 / 多路 switch 分发
                if (ok)
                {
                    S_UiPet_Enter_SnapChat();
                }
                else
                {
                    S_UiPet_Enter_Failed();
                }
                // [段6b] Cube 复位——触发完成（成功/失败均复位，允许再次触发）
                T_PushCatCfg_Cube.Complete();
            }
            // [段7] τ 时限——Cube 步进 + 耗尽 → 次项（超时走失败分支）
            else if (_S_UiPet_State == S_UiPet_State.PushCatCfg && T_PushCatCfg_Cube.IsRunning())
            {
                T_PushCatCfg_Cube.TickFrame();
                if (T_PushCatCfg_Cube.IsExpired())
                {
                    S_UiPet_Enter_Failed();
                    T_PushCatCfg_Cube.Reset();
                }
            }
        }

        /// <summary>
        /// T_Save 控制律——条件 + 操作 → 结果
        /// </summary>
        /// <param name="frame">全局帧号</param>
        private void T_Save_Execute(int frame)
        {
            // [段1] 条件守卫——全部成立 → 触发
            if (_S_UiPet_State == S_UiPet_State.Save && T_Save_Cube.IsIdle())
            {
                T_Save_Cube.Start();
                // [段4] 操作——顺序执行 + try-catch 隔离 + 自动审计
                bool ok = false;
                try
                {
                    AuditBrick("invoke", "T_Save", "ui.config_set", frame);
                    ok = Mau.Bricks.BRIK_UI_010.ConfigSet("cat|key|value");
                    AuditBrick("ok", "T_Save", "ui.config_set", frame);
                }
                catch (Exception ex)
                {
                    ok = false;
                    AuditBrick("error", "T_Save", "ui.config_set", frame);
                }
                // [段5] 结果转移——bool 驱动首项成功 / 次项失败 / 多路 switch 分发
                if (ok)
                {
                    S_UiPet_Enter_SnapChat();
                }
                else
                {
                    S_UiPet_Enter_Failed();
                }
                // [段6b] Cube 复位——触发完成（成功/失败均复位，允许再次触发）
                T_Save_Cube.Complete();
            }
            // [段7] τ 时限——Cube 步进 + 耗尽 → 次项（超时走失败分支）
            else if (_S_UiPet_State == S_UiPet_State.Save && T_Save_Cube.IsRunning())
            {
                T_Save_Cube.TickFrame();
                if (T_Save_Cube.IsExpired())
                {
                    S_UiPet_Enter_Failed();
                    T_Save_Cube.Reset();
                }
            }
        }

        /// <summary>
        /// T_ProfSave 控制律——条件 + 操作 → 结果
        /// </summary>
        /// <param name="frame">全局帧号</param>
        private void T_ProfSave_Execute(int frame)
        {
            // [段1] 条件守卫——全部成立 → 触发
            if (_S_UiPet_State == S_UiPet_State.ProfSave && T_ProfSave_Cube.IsIdle())
            {
                T_ProfSave_Cube.Start();
                // [段4] 操作——顺序执行 + try-catch 隔离 + 自动审计
                bool ok = false;
                try
                {
                    AuditBrick("invoke", "T_ProfSave", "ui.profile_save", frame);
                    ok = Mau.Bricks.BRIK_UI_006.ProfileSave("{}", out _profileId);
                    AuditBrick("ok", "T_ProfSave", "ui.profile_save", frame);
                }
                catch (Exception ex)
                {
                    ok = false;
                    AuditBrick("error", "T_ProfSave", "ui.profile_save", frame);
                }
                // [段5] 结果转移——bool 驱动首项成功 / 次项失败 / 多路 switch 分发
                if (ok)
                {
                    S_UiPet_Enter_SnapChat();
                }
                else
                {
                    S_UiPet_Enter_Failed();
                }
                // [段6b] Cube 复位——触发完成（成功/失败均复位，允许再次触发）
                T_ProfSave_Cube.Complete();
            }
            // [段7] τ 时限——Cube 步进 + 耗尽 → 次项（超时走失败分支）
            else if (_S_UiPet_State == S_UiPet_State.ProfSave && T_ProfSave_Cube.IsRunning())
            {
                T_ProfSave_Cube.TickFrame();
                if (T_ProfSave_Cube.IsExpired())
                {
                    S_UiPet_Enter_Failed();
                    T_ProfSave_Cube.Reset();
                }
            }
        }

        /// <summary>
        /// T_ProfDel 控制律——条件 + 操作 → 结果
        /// </summary>
        /// <param name="frame">全局帧号</param>
        private void T_ProfDel_Execute(int frame)
        {
            // [段1] 条件守卫——全部成立 → 触发
            if (_S_UiPet_State == S_UiPet_State.ProfDel && T_ProfDel_Cube.IsIdle())
            {
                T_ProfDel_Cube.Start();
                // [段4] 操作——顺序执行 + try-catch 隔离 + 自动审计
                bool ok = false;
                try
                {
                    AuditBrick("invoke", "T_ProfDel", "ui.profile_delete", frame);
                    ok = Mau.Bricks.BRIK_UI_007.ProfileDelete("default");
                    AuditBrick("ok", "T_ProfDel", "ui.profile_delete", frame);
                }
                catch (Exception ex)
                {
                    ok = false;
                    AuditBrick("error", "T_ProfDel", "ui.profile_delete", frame);
                }
                // [段5] 结果转移——bool 驱动首项成功 / 次项失败 / 多路 switch 分发
                if (ok)
                {
                    S_UiPet_Enter_SnapChat();
                }
                else
                {
                    S_UiPet_Enter_Failed();
                }
                // [段6b] Cube 复位——触发完成（成功/失败均复位，允许再次触发）
                T_ProfDel_Cube.Complete();
            }
            // [段7] τ 时限——Cube 步进 + 耗尽 → 次项（超时走失败分支）
            else if (_S_UiPet_State == S_UiPet_State.ProfDel && T_ProfDel_Cube.IsRunning())
            {
                T_ProfDel_Cube.TickFrame();
                if (T_ProfDel_Cube.IsExpired())
                {
                    S_UiPet_Enter_Failed();
                    T_ProfDel_Cube.Reset();
                }
            }
        }

        /// <summary>
        /// T_ProfAct 控制律——条件 + 操作 → 结果
        /// </summary>
        /// <param name="frame">全局帧号</param>
        private void T_ProfAct_Execute(int frame)
        {
            // [段1] 条件守卫——全部成立 → 触发
            if (_S_UiPet_State == S_UiPet_State.ProfAct && T_ProfAct_Cube.IsIdle())
            {
                T_ProfAct_Cube.Start();
                // [段4] 操作——顺序执行 + try-catch 隔离 + 自动审计
                bool ok = false;
                try
                {
                    AuditBrick("invoke", "T_ProfAct", "ui.profile_active", frame);
                    ok = Mau.Bricks.BRIK_UI_008.ProfileActive("default");
                    AuditBrick("ok", "T_ProfAct", "ui.profile_active", frame);
                }
                catch (Exception ex)
                {
                    ok = false;
                    AuditBrick("error", "T_ProfAct", "ui.profile_active", frame);
                }
                // [段5] 结果转移——bool 驱动首项成功 / 次项失败 / 多路 switch 分发
                if (ok)
                {
                    S_UiPet_Enter_SnapChat();
                }
                else
                {
                    S_UiPet_Enter_Failed();
                }
                // [段6b] Cube 复位——触发完成（成功/失败均复位，允许再次触发）
                T_ProfAct_Cube.Complete();
            }
            // [段7] τ 时限——Cube 步进 + 耗尽 → 次项（超时走失败分支）
            else if (_S_UiPet_State == S_UiPet_State.ProfAct && T_ProfAct_Cube.IsRunning())
            {
                T_ProfAct_Cube.TickFrame();
                if (T_ProfAct_Cube.IsExpired())
                {
                    S_UiPet_Enter_Failed();
                    T_ProfAct_Cube.Reset();
                }
            }
        }

        /// <summary>
        /// T_OpenDir 控制律——条件 + 操作 → 结果
        /// </summary>
        /// <param name="frame">全局帧号</param>
        private void T_OpenDir_Execute(int frame)
        {
            // [段1] 条件守卫——全部成立 → 触发
            if (_S_UiPet_State == S_UiPet_State.OpenDir && T_OpenDir_Cube.IsIdle())
            {
                T_OpenDir_Cube.Start();
                // [段4] 操作——顺序执行 + try-catch 隔离 + 自动审计
                bool ok = false;
                try
                {
                    AuditBrick("invoke", "T_OpenDir", "win.open_dir", frame);
                    ok = Mau.Bricks.BRIK_WIN_001.OpenDir("config");
                    AuditBrick("ok", "T_OpenDir", "win.open_dir", frame);
                }
                catch (Exception ex)
                {
                    ok = false;
                    AuditBrick("error", "T_OpenDir", "win.open_dir", frame);
                }
                // [段5] 结果转移——bool 驱动首项成功 / 次项失败 / 多路 switch 分发
                if (ok)
                {
                    S_UiPet_Enter_SnapChat();
                }
                else
                {
                    S_UiPet_Enter_Failed();
                }
                // [段6b] Cube 复位——触发完成（成功/失败均复位，允许再次触发）
                T_OpenDir_Cube.Complete();
            }
            // [段7] τ 时限——Cube 步进 + 耗尽 → 次项（超时走失败分支）
            else if (_S_UiPet_State == S_UiPet_State.OpenDir && T_OpenDir_Cube.IsRunning())
            {
                T_OpenDir_Cube.TickFrame();
                if (T_OpenDir_Cube.IsExpired())
                {
                    S_UiPet_Enter_Failed();
                    T_OpenDir_Cube.Reset();
                }
            }
        }

        /// <summary>
        /// T_Flash 控制律——条件 + 操作 → 结果
        /// </summary>
        /// <param name="frame">全局帧号</param>
        private void T_Flash_Execute(int frame)
        {
            // [段1] 条件守卫——全部成立 → 触发
            if (_S_UiPet_State == S_UiPet_State.Flash && T_Flash_Cube.IsIdle())
            {
                T_Flash_Cube.Start();
                // [段4] 操作——顺序执行 + try-catch 隔离 + 自动审计
                bool ok = false;
                try
                {
                    AuditBrick("invoke", "T_Flash", "win.flash_taskbar", frame);
                    ok = Mau.Bricks.BRIK_WIN_002.FlashTaskbar();
                    AuditBrick("ok", "T_Flash", "win.flash_taskbar", frame);
                }
                catch (Exception ex)
                {
                    ok = false;
                    AuditBrick("error", "T_Flash", "win.flash_taskbar", frame);
                }
                // [段5] 结果转移——bool 驱动首项成功 / 次项失败 / 多路 switch 分发
                if (ok)
                {
                    S_UiPet_Enter_SnapChat();
                }
                else
                {
                    S_UiPet_Enter_Failed();
                }
                // [段6b] Cube 复位——触发完成（成功/失败均复位，允许再次触发）
                T_Flash_Cube.Complete();
            }
            // [段7] τ 时限——Cube 步进 + 耗尽 → 次项（超时走失败分支）
            else if (_S_UiPet_State == S_UiPet_State.Flash && T_Flash_Cube.IsRunning())
            {
                T_Flash_Cube.TickFrame();
                if (T_Flash_Cube.IsExpired())
                {
                    S_UiPet_Enter_Failed();
                    T_Flash_Cube.Reset();
                }
            }
        }

        /// <summary>
        /// T_Notify 控制律——条件 + 操作 → 结果
        /// </summary>
        /// <param name="frame">全局帧号</param>
        private void T_Notify_Execute(int frame)
        {
            // [段1] 条件守卫——全部成立 → 触发
            if (_S_UiPet_State == S_UiPet_State.Notify && T_Notify_Cube.IsIdle())
            {
                T_Notify_Cube.Start();
                // [段4] 操作——顺序执行 + try-catch 隔离 + 自动审计
                bool ok = false;
                try
                {
                    AuditBrick("invoke", "T_Notify", "win.notify", frame);
                    ok = Mau.Bricks.BRIK_WIN_003.Notify("title|text");
                    AuditBrick("ok", "T_Notify", "win.notify", frame);
                }
                catch (Exception ex)
                {
                    ok = false;
                    AuditBrick("error", "T_Notify", "win.notify", frame);
                }
                // [段5] 结果转移——bool 驱动首项成功 / 次项失败 / 多路 switch 分发
                if (ok)
                {
                    S_UiPet_Enter_SnapChat();
                }
                else
                {
                    S_UiPet_Enter_Failed();
                }
                // [段6b] Cube 复位——触发完成（成功/失败均复位，允许再次触发）
                T_Notify_Cube.Complete();
            }
            // [段7] τ 时限——Cube 步进 + 耗尽 → 次项（超时走失败分支）
            else if (_S_UiPet_State == S_UiPet_State.Notify && T_Notify_Cube.IsRunning())
            {
                T_Notify_Cube.TickFrame();
                if (T_Notify_Cube.IsExpired())
                {
                    S_UiPet_Enter_Failed();
                    T_Notify_Cube.Reset();
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
// #MAU_CHECKSUM:SHA256:FE385531C89169951F991D0455D39C2E92E87CD868972F76FAB2D9DA4099B5D4