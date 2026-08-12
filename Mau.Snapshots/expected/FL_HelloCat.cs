// ═══ HelloCat 生成物 — Mau v2.0 翻译器 ═══
// 生成物由翻译器确定性输出——手工修改无效，改 .mau 后重新生成
using System;
using System.Collections.Generic;
using Mau.Runtime;
using System.IO;
using System.Text;

namespace Mau.Generated
{
    /// <summary>
    /// 生成物——HelloCat 受控系统
    /// </summary>
    public sealed class HelloCat
    {
        // [注入字段]
        private string? _module;
        private string? _message;

        /// <summary>
        /// 注入字段设置——module（纯赋值，不置位信号）
        /// </summary>
        /// <param name="value">注入值</param>
        public void SetModule(string? value) { _module = value; }

        /// <summary>
        /// 注入字段设置——message（纯赋值，不置位信号）
        /// </summary>
        /// <param name="value">注入值</param>
        public void SetMessage(string? value) { _message = value; }

        private enum S_Hello_State { Idle, Done, Failed }
        private S_Hello_State _S_Hello_State;

        // [命题]
        private bool P_Ask;

        // [控制律 Cube]

        /// <summary>
        /// 构造——初始状态置位
        /// </summary>
        public HelloCat()
        {
            S_Hello_Enter_Idle();
        }

        /// <summary>
        /// 进入 S_Hello.Idle——单值赋值（互斥由类型系统保证）
        /// </summary>
        private void S_Hello_Enter_Idle()
        {
            _S_Hello_State = S_Hello_State.Idle;
        }

        /// <summary>
        /// 进入 S_Hello.Done——单值赋值（互斥由类型系统保证）
        /// </summary>
        private void S_Hello_Enter_Done()
        {
            _S_Hello_State = S_Hello_State.Done;
        }

        /// <summary>
        /// 进入 S_Hello.Failed——单值赋值（互斥由类型系统保证）
        /// </summary>
        private void S_Hello_Enter_Failed()
        {
            _S_Hello_State = S_Hello_State.Failed;
        }

        /// <summary>
        /// S_Hello 是否处于 Idle
        /// </summary>
        public bool IsHelloIdle() { return _S_Hello_State == S_Hello_State.Idle; }

        /// <summary>
        /// S_Hello 是否处于 Done
        /// </summary>
        public bool IsHelloDone() { return _S_Hello_State == S_Hello_State.Done; }

        /// <summary>
        /// S_Hello 是否处于 Failed
        /// </summary>
        public bool IsHelloFailed() { return _S_Hello_State == S_Hello_State.Failed; }

        /// <summary>
        /// S_Hello 当前状态名
        /// </summary>
        public string GetHelloState()
        {
            switch (_S_Hello_State)
            {
                case S_Hello_State.Idle: return "Idle";
                case S_Hello_State.Done: return "Done";
                case S_Hello_State.Failed: return "Failed";
                default: return "Unknown";
            }
        }

        /// <summary>
        /// 帧驱动——测量采样 + 控制律守卫（声明顺序）
        /// </summary>
        /// <param name="frame">全局帧号</param>
        public void Tick(int frame)
        {
            T_Hello_Execute(frame);
        }

        /// <summary>
        /// T_Hello 控制律——条件 + 操作 → 结果
        /// </summary>
        /// <param name="frame">全局帧号</param>
        private void T_Hello_Execute(int frame)
        {
            // [段1] 条件守卫——全部成立 → 触发
            if (P_Ask)
            {
                // [段2] 信号消费——触发即清除
                P_Ask = false;
                // [段4] 操作——顺序执行 + try-catch 隔离 + 自动审计
                bool ok = false;
                try
                {
                    AuditBrick("invoke", "T_Hello", "log.write", frame);
                    ok = Mau.Bricks.BRIK_LOG_001.Write(_module, 0, _message);
                    AuditBrick("ok", "T_Hello", "log.write", frame);
                }
                catch (Exception ex)
                {
                    ok = false;
                    AuditBrick("error", "T_Hello", "log.write", frame);
                }
                // [段5] 结果转移——bool 驱动首项成功 / 次项失败 / 多路 switch 分发
                if (ok)
                {
                    S_Hello_Enter_Done();
                }
                else
                {
                    S_Hello_Enter_Failed();
                }
            }
        }

        /// <summary>
        /// 外部投递——P_Ask（带载荷）
        /// </summary>
        public void FireAsk(string module, string message)
        {
            _module = module;
            _message = message;
            P_Ask = true;
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
// #MAU_CHECKSUM:SHA256:34E9D93C176D04933B6E396BE7FFB97D7E86AD4D50155F3D247002872267C0F7