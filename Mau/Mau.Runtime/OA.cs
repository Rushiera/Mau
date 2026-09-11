using System;
using System.Collections.Generic;

namespace Mau.Runtime
{
    /// <summary>
    /// 主线程 OA 工单撮合器。所有状态转换均发生在宿主 Tick 时序内，
    /// 挂单方与执行方只通过工单状态交汇，不直接回调彼此。
    /// 撮合主流程（Post/Set/Get/Cancel/ListOpen/Claim/Complete/Relist/Tick）在本文件；
    /// 查询/回收/工具面分部在 OA.Support.cs（P7b partial 拆分）。
    /// </summary>
    public sealed partial class OA : IOA
    {
        /// <summary>
        /// 宿主线程归属守卫
        /// </summary>
        private readonly ThreadGuard _threadGuard;

        /// <summary>
        /// 日志写入器
        /// </summary>
        private readonly Action<string, int>? _logWriter;

        /// <summary>
        /// 宿主提供的存活挂单方身份校验。
        /// </summary>
        private readonly Func<long, bool>? _isLivingOwner;

        /// <summary>
        /// 宿主提供的存活执行方身份校验。
        /// </summary>
        private readonly Func<long, bool>? _isLivingWorker;

        /// <summary>
        /// Office ID 到工单的主存储
        /// </summary>
        private readonly Dictionary<long, Office> _offices;

        /// <summary>
        /// 下一个 Office ID
        /// </summary>
        private long _nextOfficeId;

        /// <summary>
        /// OA 自身帧号
        /// </summary>
        private long _tickNumber;

        /// <summary>
        /// 累计完成数量
        /// </summary>
        private long _totalDone;

        /// <summary>
        /// 累计超时数量
        /// </summary>
        private long _totalTimeout;

        /// <summary>
        /// 累计重挂数量
        /// </summary>
        private long _totalRelist;

        /// <summary>
        /// OA 可观察业务内容变化版本
        /// </summary>
        private long _version;

        /// <summary>
        /// 创建空 OA
        /// </summary>
        /// <param name="threadGuard">宿主线程守卫</param>
        public OA(ThreadGuard threadGuard)
        {
            if (threadGuard == null)
            {
                throw new ArgumentNullException("threadGuard");
            }
            _threadGuard = threadGuard;
            _logWriter = null;
            _isLivingOwner = null;
            _isLivingWorker = null;
            _offices = new Dictionary<long, Office>();
            _nextOfficeId = 1;
            _tickNumber = 0;
            _totalDone = 0;
            _totalTimeout = 0;
            _totalRelist = 0;
            _version = 0;
        }

        /// <summary>
        /// 创建带日志与存活校验的 OA
        /// </summary>
        /// <param name="threadGuard">宿主线程守卫</param>
        /// <param name="logWriter">日志写入器</param>
        /// <param name="isLivingOwner">挂单方存活校验，null=跳过</param>
        /// <param name="isLivingWorker">执行方存活校验，null=跳过</param>
        public OA(ThreadGuard threadGuard, Action<string, int>? logWriter,
            Func<long, bool>? isLivingOwner, Func<long, bool>? isLivingWorker)
        {
            if (threadGuard == null)
            {
                throw new ArgumentNullException("threadGuard");
            }
            _threadGuard = threadGuard;
            _logWriter = logWriter;
            _isLivingOwner = isLivingOwner;
            _isLivingWorker = isLivingWorker;
            _offices = new Dictionary<long, Office>();
            _nextOfficeId = 1;
            _tickNumber = 0;
            _totalDone = 0;
            _totalTimeout = 0;
            _totalRelist = 0;
            _version = 0;
        }

        /// <summary>
        /// 上架一个工单——空双字典载荷随单生成
        /// </summary>
        /// <param name="ownerId">挂单方 ID</param>
        /// <param name="officeType">工单大类</param>
        /// <param name="officeName">工单名称</param>
        /// <param name="timeoutTicks">超时帧数</param>
        /// <returns>Office ID</returns>
        public long Post(long ownerId, string officeType, string officeName,
            long timeoutTicks)
        {
            Office office;
            long officeId;

            _threadGuard.AssertMainThread("OA.Post");
            if (_isLivingOwner != null && !_isLivingOwner(ownerId))
            {
                throw new ArgumentException(
                    "OA owner must be a living owner in this host.", "ownerId");
            }
            officeId = _nextOfficeId;
            _nextOfficeId = _nextOfficeId + 1;
            office = new Office();
            office.OfficeId = officeId;
            office.OfficeType = officeType;
            office.OfficeName = officeName;
            office.OwnerId = ownerId;
            office.Data = OfficeData.Empty();
            office.Status = OfficeState.Open;
            office.ClaimByWorkerId = 0;
            office.PostFrame = _tickNumber;
            office.ClaimFrame = 0;
            office.TimeoutFrames = timeoutTicks;
            office.Result = OfficeData.Empty();
            _offices[officeId] = office;
            _version = _version + 1;
            if (Audit != null)
            {
                Audit.Record("OA", "oa.post", -1, new AuditProp[] {
                    new AuditProp("officeId", officeId.ToString()),
                    new AuditProp("ownerId", ownerId.ToString()),
                    new AuditProp("type", officeType),
                    new AuditProp("name", officeName),
                    new AuditProp("timeout", timeoutTicks.ToString())
                });
            }
            // O 类 Log——提单（INFO 分支 Category=OA）
            LogStore.Add("OA", 0, "工单 #" + officeId + " 已提交：" + officeType + "/" + officeName + "（超时 " + timeoutTicks.ToString() + " 帧）", "OA");
            return officeId;
        }

        /// <summary>
        /// 写入请求载荷 int 值——仅 Open 状态 + 本人（挂单方）可操作
        /// </summary>
        /// <param name="officeId">Office ID</param>
        /// <param name="ownerId">挂单方 ID</param>
        /// <param name="key">Key</param>
        /// <param name="value">int 值</param>
        /// <returns>true=写入成功</returns>
        public bool SetInt(long officeId, long ownerId, string key, int value)
        {
            Office office;

            _threadGuard.AssertMainThread("OA.SetInt");
            if (!_offices.TryGetValue(officeId, out office))
            {
                return false;
            }
            if (office.Status != OfficeState.Open || office.OwnerId != ownerId)
            {
                return false;
            }
            office.Data.Ints[key] = value;
            _offices[officeId] = office;
            _version = _version + 1;
            return true;
        }

        /// <summary>
        /// 写入请求载荷 str 值——仅 Open 状态 + 本人（挂单方）可操作
        /// </summary>
        /// <param name="officeId">Office ID</param>
        /// <param name="ownerId">挂单方 ID</param>
        /// <param name="key">Key</param>
        /// <param name="value">str 值</param>
        /// <returns>true=写入成功</returns>
        public bool SetStr(long officeId, long ownerId, string key, string value)
        {
            Office office;

            _threadGuard.AssertMainThread("OA.SetStr");
            if (!_offices.TryGetValue(officeId, out office))
            {
                return false;
            }
            if (office.Status != OfficeState.Open || office.OwnerId != ownerId)
            {
                return false;
            }
            // OA 全文持久化——先落盘再推 OA（落盘 = 兜底真相源；Log 侧 SummarizeToolArgs 摘要属运行可见度另一路径——两机制独立）
            LogStore.Add("OA", 0, "工单 #" + officeId + " 载荷 " + key + "：" + value, "OA");
            office.Data.Strs[key] = value;
            _offices[officeId] = office;
            _version = _version + 1;
            return true;
        }        /// <summary>
        /// 读取请求载荷 int 值——执行方消费
        /// </summary>
        /// <param name="officeId">Office ID</param>
        /// <param name="key">Key</param>
        /// <param name="value">int 值</param>
        /// <returns>true=Key 存在</returns>
        public bool GetInt(long officeId, string key, out int value)
        {
            Office office;

            _threadGuard.AssertMainThread("OA.GetInt");
            value = 0;
            if (!_offices.TryGetValue(officeId, out office))
            {
                return false;
            }
            return office.Data.Ints.TryGetValue(key, out value);
        }

        /// <summary>
        /// 读取请求载荷 str 值——执行方消费
        /// </summary>
        /// <param name="officeId">Office ID</param>
        /// <param name="key">Key</param>
        /// <param name="value">str 值</param>
        /// <returns>true=Key 存在</returns>
        public bool GetStr(long officeId, string key, out string value)
        {
            Office office;

            _threadGuard.AssertMainThread("OA.GetStr");
            value = "";
            if (!_offices.TryGetValue(officeId, out office))
            {
                return false;
            }
            string? raw;
            if (!office.Data.Strs.TryGetValue(key, out raw) || raw == null)
            {
                return false;
            }
            value = raw;
            return true;
        }

        /// <summary>
        /// 取消挂单方自己尚未被认领的工单
        /// </summary>
        /// <param name="officeId">Office ID</param>
        /// <param name="ownerId">请求方 ID</param>
        /// <returns>是否成功</returns>
        public bool Cancel(long officeId, long ownerId)
        {
            Office office;

            _threadGuard.AssertMainThread("OA.Cancel");
            if (!_offices.TryGetValue(officeId, out office))
            {
                return false;
            }
            if (office.OwnerId != ownerId || office.Status != OfficeState.Open)
            {
                return false;
            }
            _offices.Remove(officeId);
            _version = _version + 1;
            WriteLog("工单 #" + officeId + " 已取消（挂单方 #" + ownerId + "）", 0);
            return true;
        }

        /// <summary>
        /// 列出某一大类的全部开放工单
        /// </summary>
        /// <param name="officeType">工单大类</param>
        /// <returns>按 Office ID 排序的工单副本</returns>
        public List<Office> ListOpen(string officeType)
        {
            return ListOpenInternal(officeType, null);
        }

        /// <summary>
        /// 列出某一大类且名称匹配的开放工单
        /// </summary>
        /// <param name="officeType">工单大类</param>
        /// <param name="officeNames">允许的工单名称</param>
        /// <returns>按 Office ID 排序的工单副本</returns>
        public List<Office> ListOpen(string officeType, string[] officeNames)
        {
            return ListOpenInternal(officeType, officeNames);
        }

        /// <summary>
        /// 按传入顺序尝试批量认领工单
        /// </summary>
        /// <param name="workerId">认领方 ID</param>
        /// <param name="officeIds">待认领 ID</param>
        /// <returns>成功认领的工单副本</returns>
        public List<Office> ClaimBatch(long workerId, long[] officeIds)
        {
            List<Office> claimed = new List<Office>();

            _threadGuard.AssertMainThread("OA.ClaimBatch");
            if (officeIds == null || (_isLivingWorker != null && !_isLivingWorker(workerId)))
            {
                return claimed;
            }

            // [段1] 每个 ID 独立检查，先到先得且不回滚已成功项
            for (int i = 0; i < officeIds.Length; i = i + 1)
            {
                Office office;
                if (!_offices.TryGetValue(officeIds[i], out office))
                {
                    continue;
                }
                if (office.Status != OfficeState.Open)
                {
                    continue;
                }
                office.Status = OfficeState.Work;
                office.ClaimByWorkerId = workerId;
                office.ClaimFrame = _tickNumber;
                _offices[office.OfficeId] = office;
                _version = _version + 1;
                if (Audit != null)
                {
                    Audit.Record("OA", "oa.claim", -1, new AuditProp[] {
                        new AuditProp("officeId", office.OfficeId.ToString()),
                        new AuditProp("workerId", workerId.ToString()),
                        new AuditProp("result", "claimed")
                    });
                }
                // O 类 Log——接单（P3c 观测全链：Post/Claim/Complete/超时四态专属 Log 补齐）
                LogStore.Add("OA", 0, "工单 #" + office.OfficeId + " 已被 #" + workerId + " 认领", "OA");
                claimed.Add(CopyOffice(office));
            }
            return claimed;
        }

        /// <summary>
        /// 由持单执行方完成工单——写入回执双字典载荷 → 状态变 Closed
        /// </summary>
        /// <param name="officeId">Office ID</param>
        /// <param name="workerId">持单执行方 ID</param>
        /// <param name="result">回执双字典载荷</param>
        public void Complete(long officeId, long workerId, OfficeData result)
        {
            Office office;

            _threadGuard.AssertMainThread("OA.Complete");
            if (_isLivingWorker != null && !_isLivingWorker(workerId))
            {
                WriteLog("完成工单被拒：执行方无效（WORKER_OWNER_INVALID）", 2);
                return;
            }
            if (!_offices.TryGetValue(officeId, out office))
            {
                WriteLog("完成工单被拒：工单 #" + officeId + " 不存在", 2);
                return;
            }
            if (office.Status != OfficeState.Work || office.ClaimByWorkerId != workerId)
            {
                WriteLog("完成工单被拒：工单 #" + officeId + " 状态或权限不符", 2);
                return;
            }
            office.Result = result.Copy();
            office.Status = OfficeState.Closed;
            _offices[officeId] = office;
            _totalDone = _totalDone + 1;
            _version = _version + 1;
            // O 类 Log——单结束（成功完成；INFO 分支 Category=OA）
            LogStore.Add("OA", 0, "工单 #" + officeId + " 已完成（执行方 #" + workerId + "）", "OA");
            if (Audit != null)
            {
                Audit.Record("OA", "oa.complete", -1, new AuditProp[] {
                    new AuditProp("officeId", officeId.ToString()),
                    new AuditProp("workerId", workerId.ToString()),
                    new AuditProp("result", "ints:" + result.Ints.Count + ",strs:" + result.Strs.Count)
                });
            }
        }

        /// <summary>
        /// 由持单执行方把工单重新开放
        /// </summary>
        /// <param name="officeId">Office ID</param>
        /// <param name="workerId">持单执行方 ID</param>
        public void Relist(long officeId, long workerId)
        {
            Office office;

            _threadGuard.AssertMainThread("OA.Relist");
            if (_isLivingWorker != null && !_isLivingWorker(workerId))
            {
                return;
            }
            if (!_offices.TryGetValue(officeId, out office))
            {
                return;
            }
            if (office.Status != OfficeState.Work || office.ClaimByWorkerId != workerId)
            {
                return;
            }
            office.Status = OfficeState.Open;
            office.ClaimByWorkerId = 0;
            office.ClaimFrame = 0;
            _offices[officeId] = office;
            _totalRelist = _totalRelist + 1;
            _version = _version + 1;
        }

        /// <summary>
        /// 推进 OA 帧并结算超时
        /// </summary>
        public void Tick()
        {
            long[] officeIds;
            List<long> timeoutIDs = new List<long>();

            _threadGuard.AssertMainThread("OA.Tick");
            _tickNumber = _tickNumber + 1;
            officeIds = new long[_offices.Count];
            _offices.Keys.CopyTo(officeIds, 0);

            // [段1] 在稳定 ID 快照上检查，不在遍历中修改字典
            for (int i = 0; i < officeIds.Length; i = i + 1)
            {
                Office office = _offices[officeIds[i]];
                long elapsed;
                if (office.Status == OfficeState.Open)
                {
                    elapsed = _tickNumber - office.PostFrame;
                }
                else if (office.Status == OfficeState.Work)
                {
                    elapsed = _tickNumber - office.ClaimFrame;
                }
                else
                {
                    continue;
                }
                // 超时判据差异（有意为之——MechanismTests OAOpenWorkKeepsOneClaimOpportunityBeforeTimeout 背书）：
                // Open 用 >——PostFrame 当帧不算，至少保留一帧认领机会；Work 用 >=——认领即开始计时，达时限即超时。
                bool reachedTimeout = elapsed >= office.TimeoutFrames;
                if (office.Status == OfficeState.Open)
                {
                    reachedTimeout = elapsed > office.TimeoutFrames;
                }
                if (reachedTimeout)
                {
                    timeoutIDs.Add(office.OfficeId);
                }
            }

            // [段2] 统一写回超时终态
            for (int i = 0; i < timeoutIDs.Count; i = i + 1)
            {
                Office office = _offices[timeoutIDs[i]];
                office.Status = OfficeState.TimeOut;
                _offices[office.OfficeId] = office;
                _totalTimeout = _totalTimeout + 1;
                _version = _version + 1;
                // O 类 Log——单结束（超时结算；INFO 分支 Category=OA）
                LogStore.Add("OA", 0, "工单 #" + office.OfficeId + " 超时结算", "OA");
                if (Audit != null)
                {
                    Audit.Record("OA", "oa.settle", -1, new AuditProp[] {
                        new AuditProp("officeId", office.OfficeId.ToString()),
                        new AuditProp("reason", "timeout")
                    });
                }
            }
        }
    }
}