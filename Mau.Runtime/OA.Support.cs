using System;
using System.Collections.Generic;

namespace Mau.Runtime
{
    /// <summary>
    /// OA 查询/回收/工具面分部——状态查询、快照、回收清理、开放筛选与日志工具。
    /// P7b partial 拆分——自 OA.cs 原样搬移，逻辑零改动。
    /// </summary>
    public sealed partial class OA : IOA
    {
        /// <summary>
        /// 获取工单状态
        /// </summary>
        /// <param name="officeId">Office ID</param>
        /// <returns>状态，不存在时返回 TimeOut</returns>
        public OfficeState GetStatus(long officeId)
        {
            Office office;

            _threadGuard.AssertMainThread("OA.GetStatus");
            if (_offices.TryGetValue(officeId, out office))
            {
                return office.Status;
            }
            return OfficeState.TimeOut;
        }

        /// <summary>
        /// 获取工单完整副本
        /// </summary>
        /// <param name="officeId">Office ID</param>
        /// <returns>工单副本，不存在时返回 TimeOut 哨兵</returns>
        public Office GetOffice(long officeId)
        {
            Office office;

            _threadGuard.AssertMainThread("OA.GetOffice");
            if (_offices.TryGetValue(officeId, out office))
            {
                return CopyOffice(office);
            }
            office = new Office();
            office.OfficeType = "";
            office.OfficeName = "";
            office.Data = OfficeData.Empty();
            office.Result = OfficeData.Empty();
            office.Status = OfficeState.TimeOut;
            return office;
        }
        /// <summary>
        /// 生成只包含数量和变化版本的 OA 快照——透明度暴露。线程契约：仅主线程（OA 单线程模型无内部锁——外部线程查询经 FlowRunner.InvokeOnMain）
        /// </summary>
        ///
        
                ///
        
public OAView GetSnapshot()
        {
            OAView snapshot = new OAView();
            long[] officeIds = new long[_offices.Count];

            _threadGuard.AssertMainThread("OA.GetSnapshot");
            snapshot.Version = _version;
            _offices.Keys.CopyTo(officeIds, 0);
            for (int i = 0; i < officeIds.Length; i = i + 1)
            {
                OfficeState status = _offices[officeIds[i]].Status;
                if (status == OfficeState.Open)
                {
                    snapshot.OpenCount = snapshot.OpenCount + 1;
                }
                else if (status == OfficeState.Work)
                {
                    snapshot.WorkCount = snapshot.WorkCount + 1;
                }
                else if (status == OfficeState.Closed)
                {
                    snapshot.ClosedCount = snapshot.ClosedCount + 1;
                }
                else
                {
                    snapshot.TimeoutCount = snapshot.TimeoutCount + 1;
                }
            }
            return snapshot;
        }

        /// <summary>
        /// 所属挂单方回收时移除它的全部工单
        /// </summary>
        /// <param name="ownerId">已经由宿主回收的全局 ID</param>
        public void RemoveByOwner(long ownerId)
        {
            List<long> ownedOfficeIds = new List<long>();
            long[] officeIds = new long[_offices.Count];

            _threadGuard.AssertMainThread("OA.RemoveByOwner");
            _offices.Keys.CopyTo(officeIds, 0);
            Array.Sort(officeIds);
            for (int i = 0; i < officeIds.Length; i = i + 1)
            {
                if (_offices[officeIds[i]].OwnerId == ownerId)
                {
                    ownedOfficeIds.Add(officeIds[i]);
                }
            }
            for (int i = 0; i < ownedOfficeIds.Count; i = i + 1)
            {
                Office office = _offices[ownedOfficeIds[i]];
                _offices.Remove(ownedOfficeIds[i]);
                _version = _version + 1;
                WriteLog("OA | OWNER_RECYCLED | Owner#" + ownerId + " | Office#"
                    + office.OfficeId + " | Status=" + office.Status.ToString(), 0);
            }
        }

        /// <summary>
        /// 执行方回收时释放其全部 Work 工单，重新开放给后续执行方。
        /// </summary>
        /// <param name="workerId">回收的全局 ID</param>
        public void ReleaseByWorker(long workerId)
        {
            long[] officeIds = new long[_offices.Count];

            _threadGuard.AssertMainThread("OA.ReleaseByWorker");
            _offices.Keys.CopyTo(officeIds, 0);
            Array.Sort(officeIds);
            for (int i = 0; i < officeIds.Length; i = i + 1)
            {
                Office office = _offices[officeIds[i]];
                if (office.Status != OfficeState.Work
                    || office.ClaimByWorkerId != workerId)
                {
                    continue;
                }
                office.Status = OfficeState.Open;
                office.ClaimByWorkerId = 0;
                office.ClaimFrame = 0;
                _offices[office.OfficeId] = office;
                _totalRelist = _totalRelist + 1;
                _version = _version + 1;
                WriteLog("OA | WORKER_RECYCLED | Worker#" + workerId.ToString()
                    + " | Office#" + office.OfficeId.ToString(), 0);
            }
        }

        /// <summary>
        /// 获取 OA 调试摘要
        /// </summary>
        /// <returns>状态统计与活跃工单文本</returns>
        public string[] GetDebugInfo()
        {
            List<string> lines = new List<string>();
            long[] officeIds = new long[_offices.Count];
            int openCount = 0;
            int workCount = 0;
            int closedCount = 0;
            int timeoutCount = 0;

            _threadGuard.AssertMainThread("OA.GetDebugInfo");
            _offices.Keys.CopyTo(officeIds, 0);
            Array.Sort(officeIds);
            for (int i = 0; i < officeIds.Length; i = i + 1)
            {
                OfficeState status = _offices[officeIds[i]].Status;
                if (status == OfficeState.Open)
                {
                    openCount = openCount + 1;
                }
                else if (status == OfficeState.Work)
                {
                    workCount = workCount + 1;
                }
                else if (status == OfficeState.Closed)
                {
                    closedCount = closedCount + 1;
                }
                else
                {
                    timeoutCount = timeoutCount + 1;
                }
            }
            lines.Add("[OA] Open=" + openCount + " Work=" + workCount
                + " Closed=" + closedCount + " TimeOut=" + timeoutCount);
            lines.Add("  Done=" + _totalDone + " Relist=" + _totalRelist
                + " Timeout=" + _totalTimeout + " Frame=" + _tickNumber);
            return lines.ToArray();
        }

        /// <summary>
        /// 执行开放工单筛选
        /// </summary>
        /// <param name="officeType">工单大类</param>
        /// <param name="officeNames">可选名称列表</param>
        /// <returns>排序后的工单副本</returns>
        private List<Office> ListOpenInternal(string officeType, string[]? officeNames)
        {
            List<Office> result = new List<Office>();
            long[] officeIds = new long[_offices.Count];

            _threadGuard.AssertMainThread("OA.ListOpen");
            _offices.Keys.CopyTo(officeIds, 0);
            Array.Sort(officeIds);
            for (int i = 0; i < officeIds.Length; i = i + 1)
            {
                Office office = _offices[officeIds[i]];
                if (office.Status != OfficeState.Open || office.OfficeType != officeType)
                {
                    continue;
                }
                if (officeNames != null && !ContainsText(officeNames, office.OfficeName))
                {
                    continue;
                }
                result.Add(CopyOffice(office));
            }
            return result;
        }

        /// <summary>
        /// 检查数组是否包含指定文本
        /// </summary>
        /// <param name="values">文本数组</param>
        /// <param name="target">目标文本</param>
        /// <returns>是否包含</returns>
        private bool ContainsText(string[] values, string target)
        {
            for (int i = 0; i < values.Length; i = i + 1)
            {
                if (values[i] == target)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 深复制工单中的双字典载荷字段
        /// </summary>
        /// <param name="source">源工单</param>
        /// <returns>工单副本</returns>
        private Office CopyOffice(Office source)
        {
            source.Data = source.Data.Copy();
            source.Result = source.Result.Copy();
            return source;
        }

        /// <summary>
        /// 写入可选日志
        /// </summary>
        /// <param name="message">日志正文</param>
        /// <param name="level">日志等级</param>
        private void WriteLog(string message, int level)
        {
            if (_logWriter != null)
            {
                _logWriter(message, level);
            }
        }
        /// <summary>
        /// 审计存储——宿主注入后机制事件写入（null = 不审计）。零业务侵入：仅记录，不改流程。
        /// </summary>
        public AuditStore? Audit
        {
            get;
            set;
        }
    }
}