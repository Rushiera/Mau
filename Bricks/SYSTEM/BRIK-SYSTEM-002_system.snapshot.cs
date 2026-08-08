// ═══════════════════════════════════════════════════
// 积木: system.snapshot
// ID:   BRIK-SYSTEM-002
// 类别: SYSTEM
// 作用: Runtime 快照大纲——FlowRunner 实体/OA/Command/DataBox 汇总（细则未来新增指令）
// 依赖: 无
// 包: 无
// 引用: Mau.Runtime · System
// 原理: FlowRunner.GetStatus + DataBox.Capture 聚合为大纲文本
// 常用: SystemCat 工具 Cat——运行时状态总览（M2c 六+一域 System 域）
// ═══════════════════════════════════════════════════
using System;
using System.Text;

namespace Mau.Bricks
{
    /// <summary>
    /// 系统积木——system.snapshot Runtime 快照大纲（纯函数无状态）
    /// </summary>
    public static class SystemSnapshotBrick
    {
        /// <summary>
        /// Runtime 快照大纲——FlowRunner/OA/Command/DataBox 汇总
        /// </summary>
        /// <param name="snapshot">快照文本</param>
        /// <returns>true=成功</returns>
        public static bool Snapshot(out string snapshot)
        {
            Mau.Runtime.FlowRunner? runner;
            Mau.Runtime.DataBox.TryResolve<Mau.Runtime.FlowRunner>(out runner);
            if (runner == null)
            {
                snapshot = "ERR|SNAPSHOT_NO_RUNNER";
                return false;
            }
            Mau.Runtime.HostSnapshot status = runner.GetStatus();
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("帧号: " + status.Frame);
            sb.AppendLine("主线程: " + (status.IsMainThread ? "是" : "否"));
            // 实体大纲
            if (status.Flows != null && status.Flows.Length > 0)
            {
                sb.AppendLine("实体 (" + status.Flows.Length + "):");
                for (int i = 0; i < status.Flows.Length; i = i + 1)
                {
                    sb.AppendLine("  #" + status.Flows[i].Id + " " + status.Flows[i].Name
                        + "（" + status.Flows[i].TypeName + " / " + status.Flows[i].Kind + "）");
                }
            }
            else
            {
                sb.AppendLine("实体: (0)");
            }
            // OA 大纲
            if (status.OA != null)
            {
                sb.AppendLine("OA: Open " + status.OA.Value.OpenCount
                    + "  Work " + status.OA.Value.WorkCount
                    + "  Closed " + status.OA.Value.ClosedCount
                    + "  TimeOut " + status.OA.Value.TimeoutCount);
            }
            // Command 大纲
            if (status.Command != null)
            {
                sb.AppendLine("Command: 注册者 " + status.Command.Value.RegisteredOwnerCount
                    + "  key " + status.Command.Value.RegisteredKeyCount
                    + "  待消费 " + status.Command.Value.TotalInputKeyCount);
            }
            // DataBox 大纲
            Mau.Runtime.DataBoxSnapshot box = Mau.Runtime.DataBox.Capture();
            sb.AppendLine("DataBox: 服务 " + box.Services.Length + "  数据条目 " + box.Data.Length);
            snapshot = sb.ToString().TrimEnd('\n');
            return true;
        }
    }
}
// #MAU_CHECKSUM:SHA256:17F0D8BC347C201812B46E45D7CC2AC284419F3F9CB1E935A08EA82AA98C972A
