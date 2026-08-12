// ═══ ToolCat 生成物 — Mau v2.0 翻译器 ═══
// 生成物由翻译器确定性输出——手工修改无效，改 .mau 后重新生成
using System;
using System.Collections.Generic;
using Mau.Runtime;

namespace Mau.Generated
{
    /// <summary>
    /// 生成物——ToolCat 受控系统
    /// </summary>
    public sealed class ToolCat
    {
        // [注入字段]
        private string? _path;
        private string? _content;

        /// <summary>
        /// 注入字段设置——path（纯赋值，不置位信号）
        /// </summary>
        /// <param name="value">注入值</param>
        public void SetPath(string? value) { _path = value; }

        /// <summary>
        /// 注入字段设置——content（纯赋值，不置位信号）
        /// </summary>
        /// <param name="value">注入值</param>
        public void SetContent(string? value) { _content = value; }

        private enum S_Tool_State { Idle, ReadDone, ReadFailed, WriteDone, WriteFailed }
        private S_Tool_State _S_Tool_State;

        // [命题]
        private bool P_Read;
        private bool P_Write;

        // [积木输出端口]
        private string _content = default;

        // [控制律 Cube]

        /// <summary>
        /// 构造——初始状态置位
        /// </summary>
        public ToolCat()
        {
            S_Tool_Enter_Idle();
        }

        /// <summary>
        /// 进入 S_Tool.Idle——单值赋值（互斥由类型系统保证）
        /// </summary>
        private void S_Tool_Enter_Idle()
        {
            _S_Tool_State = S_Tool_State.Idle;
        }

        /// <summary>
        /// 进入 S_Tool.ReadDone——单值赋值（互斥由类型系统保证）
        /// </summary>
        private void S_Tool_Enter_ReadDone()
        {
            _S_Tool_State = S_Tool_State.ReadDone;
        }

        /// <summary>
        /// 进入 S_Tool.ReadFailed——单值赋值（互斥由类型系统保证）
        /// </summary>
        private void S_Tool_Enter_ReadFailed()
        {
            _S_Tool_State = S_Tool_State.ReadFailed;
        }

        /// <summary>
        /// 进入 S_Tool.WriteDone——单值赋值（互斥由类型系统保证）
        /// </summary>
        private void S_Tool_Enter_WriteDone()
        {
            _S_Tool_State = S_Tool_State.WriteDone;
        }

        /// <summary>
        /// 进入 S_Tool.WriteFailed——单值赋值（互斥由类型系统保证）
        /// </summary>
        private void S_Tool_Enter_WriteFailed()
        {
            _S_Tool_State = S_Tool_State.WriteFailed;
        }

        /// <summary>
        /// S_Tool 是否处于 Idle
        /// </summary>
        public bool IsToolIdle() { return _S_Tool_State == S_Tool_State.Idle; }

        /// <summary>
        /// S_Tool 是否处于 ReadDone
        /// </summary>
        public bool IsToolReadDone() { return _S_Tool_State == S_Tool_State.ReadDone; }

        /// <summary>
        /// S_Tool 是否处于 ReadFailed
        /// </summary>
        public bool IsToolReadFailed() { return _S_Tool_State == S_Tool_State.ReadFailed; }

        /// <summary>
        /// S_Tool 是否处于 WriteDone
        /// </summary>
        public bool IsToolWriteDone() { return _S_Tool_State == S_Tool_State.WriteDone; }

        /// <summary>
        /// S_Tool 是否处于 WriteFailed
        /// </summary>
        public bool IsToolWriteFailed() { return _S_Tool_State == S_Tool_State.WriteFailed; }

        /// <summary>
        /// S_Tool 当前状态名
        /// </summary>
        public string GetToolState()
        {
            switch (_S_Tool_State)
            {
                case S_Tool_State.Idle: return "Idle";
                case S_Tool_State.ReadDone: return "ReadDone";
                case S_Tool_State.ReadFailed: return "ReadFailed";
                case S_Tool_State.WriteDone: return "WriteDone";
                case S_Tool_State.WriteFailed: return "WriteFailed";
                default: return "Unknown";
            }
        }

        /// <summary>
        /// 帧驱动——测量采样 + 控制律守卫（声明顺序）
        /// </summary>
        /// <param name="frame">全局帧号</param>
        public void Tick(int frame)
        {
            T_Read_Execute(frame);
            T_Write_Execute(frame);
        }

        /// <summary>
        /// T_Read 控制律——条件 + 操作 → 结果
        /// </summary>
        /// <param name="frame">全局帧号</param>
        private void T_Read_Execute(int frame)
        {
            // [段1] 条件守卫——全部成立 → 触发
            if (P_Read)
            {
                // [段2] 信号消费——触发即清除
                P_Read = false;
                // [段4] 操作——顺序执行 + try-catch 隔离 + 自动审计
                bool ok = false;
                try
                {
                    AuditBrick("invoke", "T_Read", "file.read", frame);
                    ok = Mau.Bricks.BRIK_FILE_002.Read(_path, out _content);
                    AuditBrick("ok", "T_Read", "file.read", frame);
                }
                catch (Exception ex)
                {
                    ok = false;
                    AuditBrick("error", "T_Read", "file.read", frame);
                }
                // [段5] 结果转移——bool 驱动首项成功 / 次项失败 / 多路 switch 分发
                if (ok)
                {
                    S_Tool_Enter_ReadDone();
                }
                else
                {
                    S_Tool_Enter_ReadFailed();
                }
            }
        }

        /// <summary>
        /// T_Write 控制律——条件 + 操作 → 结果
        /// </summary>
        /// <param name="frame">全局帧号</param>
        private void T_Write_Execute(int frame)
        {
            // [段1] 条件守卫——全部成立 → 触发
            if (P_Write)
            {
                // [段2] 信号消费——触发即清除
                P_Write = false;
                // [段4] 操作——顺序执行 + try-catch 隔离 + 自动审计
                bool ok = false;
                try
                {
                    AuditBrick("invoke", "T_Write", "file.write", frame);
                    ok = Mau.Bricks.BRIK_FILE_003.Write(_path, _content);
                    AuditBrick("ok", "T_Write", "file.write", frame);
                }
                catch (Exception ex)
                {
                    ok = false;
                    AuditBrick("error", "T_Write", "file.write", frame);
                }
                // [段5] 结果转移——bool 驱动首项成功 / 次项失败 / 多路 switch 分发
                if (ok)
                {
                    S_Tool_Enter_WriteDone();
                }
                else
                {
                    S_Tool_Enter_WriteFailed();
                }
            }
        }

        /// <summary>
        /// 外部投递——P_Read（带载荷）
        /// </summary>
        public void FireRead(string path)
        {
            _path = path;
            P_Read = true;
        }

        /// <summary>
        /// 外部投递——P_Write（带载荷）
        /// </summary>
        public void FireWrite(string path, string content)
        {
            _path = path;
            _content = content;
            P_Write = true;
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
// #MAU_CHECKSUM:SHA256:0F46E2E88074FF48AC3FE745F473767AEC04B06C1F2CB3748760D6290FCAF76D