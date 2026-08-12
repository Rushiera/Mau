// ═══ S03PropositionFact 生成物 — Mau v2.0 翻译器 ═══
// 生成物由翻译器确定性输出——手工修改无效，改 .mau 后重新生成
using System;
using System.Collections.Generic;
using Mau.Runtime;

namespace Mau.Generated
{
    /// <summary>
    /// 生成物——S03PropositionFact 受控系统
    /// </summary>
    public sealed class S03PropositionFact
    {
        // [命题]
        private bool P_Go;
        private bool P_Recorded;
        private bool P_Failed;

        // [控制律 Cube]
        private readonly Cube T_Run_Cube = new Cube(5);

        /// <summary>
        /// 构造——初始状态置位
        /// </summary>
        public S03PropositionFact()
        {
        }

        /// <summary>
        /// 帧驱动——测量采样 + 控制律守卫（声明顺序）
        /// </summary>
        /// <param name="frame">全局帧号</param>
        public void Tick(int frame)
        {
            T_Run_Execute(frame);
        }

        /// <summary>
        /// T_Run 控制律——条件 + 操作 → 结果
        /// </summary>
        /// <param name="frame">全局帧号</param>
        private void T_Run_Execute(int frame)
        {
            // [段1] 条件守卫——全部成立 → 触发
            if (P_Go && T_Run_Cube.IsIdle())
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
                    P_Recorded = true;
                }
                else
                {
                    P_Failed = true;
                }
            }
            // [段7] τ 时限——Cube 步进 + 耗尽 → 次项（超时走失败分支）
            else if (P_Go && T_Run_Cube.IsRunning())
            {
                T_Run_Cube.TickFrame();
                if (T_Run_Cube.IsExpired())
                {
                    P_Failed = true;
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
// #MAU_CHECKSUM:SHA256:5203FE1B6C266077C25FB3D285DBC65E7A5FF26714E9A6FFB5902A6626B696A2
