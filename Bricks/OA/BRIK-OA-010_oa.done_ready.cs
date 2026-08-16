// ═══════════════════════════════════════════════════
// 积木: oa.done_ready
// ID:   BRIK-OA-010
// 类别: OA
// 作用: 工单完成沿探测——指定工单完成（Closed/TimeOut）→ true（沿语义：同一工单同一槽只触发一次）
// 依赖: 无
// 引用: Mau.Runtime（DataBox/OA/OfficeState）
// 原理: 按槽静态缓存（slot → lastId/lastDoneConsumed）——新工单 ID 重置判定；完成瞬间返回 true 一次
//       slot 参数区分多个探测点（read/ask 双线各自独立状态——防共享静态互相污染）
// 盒子: 无（纯判定）
// 常用: CH4 P5 major_domo_cat 语料——读线/问线终态探测（防上一批 @rDone 旧值残留误收集）
// 注意: 静态缓存跨实例共享——P5 单猫语义；P9 多猫并发时 scope 化改造
// ═══════════════════════════════════════════════════
using System.Collections.Generic;
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// OA 积木——oa.done_ready 工单完成沿探测（按槽独立状态——同一工单同一槽完成只触发一次）
    /// </summary>
    public static class OaDoneReadyBrick
    {
        /// <summary>
        /// 槽 → 上次判定的工单 ID（P5 单猫静态；P9 多猫 scope 化）
        /// </summary>
        private static readonly Dictionary<string, long> _lastIds = new Dictionary<string, long>();

        /// <summary>
        /// 槽 → 上次判定是否已消费完成沿
        /// </summary>
        private static readonly Dictionary<string, bool> _consumed = new Dictionary<string, bool>();

        /// <summary>
        /// 工单完成沿探测——新工单完成瞬间返回 true（同一槽同一工单只触发一次）
        /// </summary>
        /// <param name="officeId">工单 ID</param>
        /// <param name="slot">探测槽（read/ask 等——多探测点隔离状态）</param>
        /// <returns>true=工单刚完成（沿）</returns>
        public static bool DoneReady(long officeId, string slot)
        {
            if (officeId <= 0)
            {
                return false;
            }
            string safeSlot = slot ?? "";
            long lastId;
            if (!_lastIds.TryGetValue(safeSlot, out lastId))
            {
                lastId = -1;
            }
            bool consumed = false;
            _consumed.TryGetValue(safeSlot, out consumed);
            if (officeId != lastId)
            {
                // 新工单——重置判定状态
                _lastIds[safeSlot] = officeId;
                consumed = false;
            }
            if (consumed)
            {
                // 完成沿已消费——同工单不再触发（防残留 true 反复误触发）
                return false;
            }
            IOA? oa;
            DataBox.TryResolve<IOA>(out oa);
            if (oa == null)
            {
                return false;
            }
            OfficeState state = oa.GetStatus(officeId);
            bool done = state == OfficeState.Closed || state == OfficeState.TimeOut;
            if (done)
            {
                _consumed[safeSlot] = true;
                return true;
            }
            return false;
        }
    }
}
// #MAU_CHECKSUM:SHA256:8E3CAAEA17E9B835A3FC4FB1F0F98AFB9F1C85C40B7C1411A420C5886572EB75
