// ═══ S08MultiRoute 生成物 — Mau v2.0 翻译器 ═══
// 生成物由翻译器确定性输出——手工修改无效，改 .mau 后重新生成
using System;
using System.Collections.Generic;
using Mau.Runtime;

namespace Mau.Generated
{
    /// <summary>
    /// 生成物——S08MultiRoute 受控系统
    /// </summary>
    public sealed class S08MultiRoute
    {
        private enum S_Ui_State { Idle, Open, Toggle, Select }
        private S_Ui_State _S_Ui_State;

        // [命题]
        private bool P_Cmd;

        // [积木输出端口]
        private string _activeKey = default;

        // [控制律 Cube]
        private readonly Cube T_Route_Cube = new Cube(5);

        /// <summary>
        /// 构造——初始状态置位
        /// </summary>
        public S08MultiRoute()
        {
            S_Ui_Enter_Idle();
        }

        /// <summary>
        /// 进入 S_Ui.Idle——单值赋值（互斥由类型系统保证）
        /// </summary>
        private void S_Ui_Enter_Idle()
        {
            _S_Ui_State = S_Ui_State.Idle;
        }

        /// <summary>
        /// 进入 S_Ui.Open——单值赋值（互斥由类型系统保证）
        /// </summary>
        private void S_Ui_Enter_Open()
        {
            _S_Ui_State = S_Ui_State.Open;
        }

        /// <summary>
        /// 进入 S_Ui.Toggle——单值赋值（互斥由类型系统保证）
        /// </summary>
        private void S_Ui_Enter_Toggle()
        {
            _S_Ui_State = S_Ui_State.Toggle;
        }

        /// <summary>
        /// 进入 S_Ui.Select——单值赋值（互斥由类型系统保证）
        /// </summary>
        private void S_Ui_Enter_Select()
        {
            _S_Ui_State = S_Ui_State.Select;
        }

        /// <summary>
        /// S_Ui 是否处于 Idle
        /// </summary>
        public bool IsUiIdle() { return _S_Ui_State == S_Ui_State.Idle; }

        /// <summary>
        /// S_Ui 是否处于 Open
        /// </summary>
        public bool IsUiOpen() { return _S_Ui_State == S_Ui_State.Open; }

        /// <summary>
        /// S_Ui 是否处于 Toggle
        /// </summary>
        public bool IsUiToggle() { return _S_Ui_State == S_Ui_State.Toggle; }

        /// <summary>
        /// S_Ui 是否处于 Select
        /// </summary>
        public bool IsUiSelect() { return _S_Ui_State == S_Ui_State.Select; }

        /// <summary>
        /// S_Ui 当前状态名
        /// </summary>
        public string GetUiState()
        {
            switch (_S_Ui_State)
            {
                case S_Ui_State.Idle: return "Idle";
                case S_Ui_State.Open: return "Open";
                case S_Ui_State.Toggle: return "Toggle";
                case S_Ui_State.Select: return "Select";
                default: return "Unknown";
            }
        }

        /// <summary>
        /// 帧驱动——测量采样 + 控制律守卫（声明顺序）
        /// </summary>
        /// <param name="frame">全局帧号</param>
        public void Tick(int frame)
        {
            T_Route_Execute(frame);
        }

        /// <summary>
        /// T_Route 控制律——条件 + 操作 → 结果
        /// </summary>
        /// <param name="frame">全局帧号</param>
        private void T_Route_Execute(int frame)
        {
            // [段1] 条件守卫——全部成立 → 触发
            if (P_Cmd && T_Route_Cube.IsIdle())
            {
                // [段2] 信号消费——触发即清除
                P_Cmd = false;
                T_Route_Cube.Start();
                // [段4] 操作——名称返回积木（多路匹配，switch 分发源）
                string matched = "";
                bool ok = false;
                try
                {
                    AuditBrick("invoke", "T_Route", "cmd.match", frame);
                    matched = Mau.Bricks.BRIK_CMD_009.Match(_activeKey, new string[] { "Open", "Toggle", "Select" });
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
                    case "Open": S_Ui_Enter_Open(); break;
                    case "Toggle": S_Ui_Enter_Toggle(); break;
                    case "Select": S_Ui_Enter_Select(); break;
                    default: S_Ui_Enter_Idle(); break;
                }
                // [段6b] Cube 复位——触发完成（成功/失败均复位，允许再次触发）
                T_Route_Cube.Complete();
            }
            // [段7] τ 时限——Cube 步进 + 耗尽 → 次项（超时走失败分支）
            else if (P_Cmd && T_Route_Cube.IsRunning())
            {
                T_Route_Cube.TickFrame();
                if (T_Route_Cube.IsExpired())
                {
                    S_Ui_Enter_Toggle();
                    T_Route_Cube.Reset();
                }
            }
        }

        /// <summary>
        /// 外部投递——P_Cmd（带载荷）
        /// </summary>
        public void FireCmd(string activeKey)
        {
            _activeKey = activeKey;
            P_Cmd = true;
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
// #MAU_CHECKSUM:SHA256:154670FA3BADE2A2C7598596950A3C14EAC6A44EAF5916438BE7D0DCC46595B5
