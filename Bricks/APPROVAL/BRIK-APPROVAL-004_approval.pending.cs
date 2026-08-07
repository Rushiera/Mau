// ═══════════════════════════════════════════════════
// 积木: approval.pending
// ID:   BRIK-APPROVAL-004
// 类别: APPROVAL
// 作用: 读取全部等待审批的深拷贝快照
// 依赖: 无
// 引用: System · System.Collections.Generic
// 原理: 锁内遍历 → 按 requestId 排序 → 文本行序列化（JSON 友好）
// 常用: 外观层轮询等待审批
// ═══════════════════════════════════════════════════
using System;
using System.Collections.Generic;

using Mau.Runtime;
namespace Mau.Bricks
{
    /// <summary>
    /// 审批积木——approval.pending 读取等待快照（依赖 ApprovalStore）
    /// </summary>
    public static class ApprovalPendingBrick
    {
        /// <summary>
        /// 读取全部等待审批的深拷贝快照
        /// </summary>
        /// <param name="pending">请求数组（JSON 序列化友好）</param>
        /// <returns>true=成功</returns>
        public static bool GetPending(out string pending)
        {
            List<string> lines = new List<string>();
            lock (ApprovalStore.Gate)
            {
                string[] keys = new string[ApprovalStore.Pending.Count];
                ApprovalStore.Pending.Keys.CopyTo(keys, 0);
                Array.Sort(keys, StringComparer.Ordinal);
                for (int i = 0; i < keys.Length; i = i + 1)
                {
                    ApprovalRequest request = ApprovalStore.Pending[keys[i]];
                    lines.Add(keys[i] + "|" + request.Question + "|"
                        + string.Join(",", request.Options) + "|"
                        + request.DefaultIndex.ToString() + "|"
                        + request.TimeoutSeconds.ToString());
                }
            }
            pending = string.Join("\n", lines);
            return true;
        }
    }
}
// #MAU_CHECKSUM:SHA256:A195F93039FC194181929B5E73E9FDCD42EEAFCA6C73B3EF5F7C5888EECC68E8
