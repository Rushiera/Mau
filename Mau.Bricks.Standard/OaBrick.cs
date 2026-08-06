// ═══════════════════════════════════════════════
// 积木: oa.post / oa.list / oa.claim / oa.complete / oa.settle
// ID:   BRIK-OA-001 ~ 005
// 作用: OA 工单机制积木——语料声明拓扑动作（谁发布/谁认领/什么工具响应），宿主注入 OA 实例
// 引用: Mau.Bricks.Standard → Mau.Runtime（IOA/Office/OfficeState）· Mau.Contracts
// 原理: 静态宿主桥 Configure(IOA) 注入单例；积木方法包装 Mau.Runtime.OA 操作
// 常用: oa_flow.mau 工单撮合拓扑——CH4 P1.3 核心
// ═══════════════════════════════════════════════
using System;
using System.Collections.Generic;
using Mau.Contracts;
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// OA 机制积木——语料变迁动作的 OA 操作通道。宿主启动时注入实例。
    /// 线程约束：全部 main——OA 持有线程守卫，仅主线程可操作。
    /// </summary>
    public static class OaBrick
    {
        /// <summary>
        /// 宿主注入的 OA 实例——CH4_Host.Init 时 Configure；未注入时全部返回 false
        /// </summary>
        private static IOA? _instance;

        /// <summary>
        /// 注入 OA 实例——宿主启动时调用一次
        /// </summary>
        /// <param name="oa">OA 工单平台</param>
        public static void Configure(IOA oa)
        {
            _instance = oa;
        }

        /// <summary>
        /// 上架工单——空双字典载荷随单生成，挂单方 set_* 逐 Key 写
        /// </summary>
        /// <param name="dogId">所有者 LongId</param>
        /// <param name="officeType">工单大类</param>
        /// <param name="officeName">固定词汇——执行方据此判断能不能干</param>
        /// <param name="timeoutTicks">超时帧数</param>
        /// <param name="officeId">新 Office 的 ID</param>
        /// <returns>true=上架成功</returns>
        public static bool Post(long dogId, string officeType, string officeName, long timeoutTicks, out long officeId)
        {
            IOA? oa = _instance;
            if (oa == null)
            {
                officeId = 0;
                return false;
            }
            officeId = oa.Post(dogId, officeType, officeName, timeoutTicks);
            return true;
        }

        /// <summary>
        /// 写入请求载荷 int 值——仅 Open 状态 + 本人（挂单方）可操作
        /// </summary>
        /// <param name="officeId">Office ID</param>
        /// <param name="dogId">所有者 LongId</param>
        /// <param name="key">Key</param>
        /// <param name="value">int 值</param>
        /// <returns>true=写入成功</returns>
        public static bool SetInt(long officeId, long dogId, string key, int value)
        {
            IOA? oa = _instance;
            if (oa == null)
            {
                return false;
            }
            return oa.SetInt(officeId, dogId, key, value);
        }

        /// <summary>
        /// 写入请求载荷 str 值——仅 Open 状态 + 本人（挂单方）可操作
        /// </summary>
        /// <param name="officeId">Office ID</param>
        /// <param name="dogId">所有者 LongId</param>
        /// <param name="key">Key</param>
        /// <param name="value">str 值</param>
        /// <returns>true=写入成功</returns>
        public static bool SetStr(long officeId, long dogId, string key, string value)
        {
            IOA? oa = _instance;
            if (oa == null)
            {
                return false;
            }
            return oa.SetStr(officeId, dogId, key, value);
        }

        /// <summary>
        /// 读取请求载荷 int 值——执行方消费
        /// </summary>
        /// <param name="officeId">Office ID</param>
        /// <param name="key">Key</param>
        /// <param name="value">int 值</param>
        /// <returns>true=Key 存在</returns>
        public static bool GetInt(long officeId, string key, out int value)
        {
            IOA? oa = _instance;
            if (oa == null)
            {
                value = 0;
                return false;
            }
            return oa.GetInt(officeId, key, out value);
        }

        /// <summary>
        /// 读取请求载荷 str 值——执行方消费
        /// </summary>
        /// <param name="officeId">Office ID</param>
        /// <param name="key">Key</param>
        /// <param name="value">str 值</param>
        /// <returns>true=Key 存在</returns>
        public static bool GetStr(long officeId, string key, out string value)
        {
            IOA? oa = _instance;
            if (oa == null)
            {
                value = "";
                return false;
            }
            return oa.GetStr(officeId, key, out value);
        }

        /// <summary>
        /// 查单——某大类下、OfficeName 在候选列表中的 Open 单
        /// </summary>
        /// <param name="officeType">工单大类</param>
        /// <param name="officeNames">执行方能干的 OfficeName 候选数组</param>
        /// <param name="offices">匹配的 Open 单列表</param>
        /// <returns>true=查询成功</returns>
        public static bool List(string officeType, string[] officeNames, out Office[] offices)
        {
            IOA? oa = _instance;
            if (oa == null)
            {
                offices = new Office[0];
                return false;
            }
            offices = oa.ListOpen(officeType, officeNames).ToArray();
            return true;
        }

        /// <summary>
        /// 锁单——逐个尝试认领，已被别人取走的跳过，返回锁成功的名单
        /// </summary>
        /// <param name="catId">认领者 LongId</param>
        /// <param name="officeIds">待锁的 OfficeId 数组</param>
        /// <param name="claimed">锁成功的 Office 列表</param>
        /// <returns>true=认领操作成功（可能部分成功）</returns>
        public static bool Claim(long catId, long[] officeIds, out Office[] claimed)
        {
            IOA? oa = _instance;
            if (oa == null)
            {
                claimed = new Office[0];
                return false;
            }
            claimed = oa.ClaimBatch(catId, officeIds).ToArray();
            return true;
        }

        /// <summary>
        /// 单单认领——语料展开工具循环用（dispatch_one 输出单值 officeId，无需组数组）
        /// </summary>
        /// <param name="catId">认领者 LongId</param>
        /// <param name="officeId">待锁的单个 OfficeId</param>
        /// <param name="claimed">锁成功的 Office（失败为默认值）</param>
        /// <returns>true=锁单成功</returns>
        public static bool ClaimOne(long catId, long officeId, out Office claimed)
        {
            IOA? oa = _instance;
            if (oa == null)
            {
                claimed = new Office();
                return false;
            }
            List<Office> result = oa.ClaimBatch(catId, new long[] { officeId });
            if (result.Count > 0)
            {
                claimed = result[0];
                return true;
            }
            claimed = new Office();
            return false;
        }

        /// <summary>
        /// 完成工单——写入回执双字典载荷 → 状态变 Closed
        /// </summary>
        /// <param name="officeId">Office ID</param>
        /// <param name="catId">认领者 LongId</param>
        /// <param name="result">回执双字典载荷</param>
        /// <returns>true=完成成功</returns>
        public static bool Complete(long officeId, long catId, OfficeData result)
        {
            IOA? oa = _instance;
            if (oa == null)
            {
                return false;
            }
            oa.Complete(officeId, catId, result);
            return true;
        }

        /// <summary>
        /// 判断单状态——返回 true=已 Closed（判断结果，语料轮询用）
        /// </summary>
        /// <param name="officeId">Office ID</param>
        /// <param name="closed">是否已 Closed</param>
        /// <returns>true=已 Closed</returns>
        public static bool IsClosed(long officeId, out bool closed)
        {
            closed = false;
            IOA? oa = _instance;
            if (oa == null)
            {
                return false;
            }
            closed = oa.GetStatus(officeId) == OfficeState.Closed;
            return closed;
        }

        /// <summary>
        /// 结算——失败/超时工单的释放处理。Work 单退回 Open（可重投）；已终结单确认返回。
        /// </summary>
        /// <param name="officeId">Office ID</param>
        /// <param name="catId">请求者 LongId</param>
        /// <returns>true=已终结或已重投</returns>
        public static bool Settle(long officeId, long catId)
        {
            IOA? oa = _instance;
            if (oa == null)
            {
                return false;
            }
            OfficeState state = oa.GetStatus(officeId);
            if (state == OfficeState.Work)
            {
                oa.Relist(officeId, catId);
                return true;
            }
            return state == OfficeState.TimeOut || state == OfficeState.Closed;
        }
    }

    /// <summary>
    /// OA 积木注册——进程启动时调用一次
    /// </summary>
    public static class OaBrickRegistration
    {
        /// <summary>
        /// 注册全部 OA 积木
        /// </summary>
        public static void RegisterAll()
        {
            RegisterPost();
            RegisterSetInt();
            RegisterSetStr();
            RegisterGetInt();
            RegisterGetStr();
            RegisterList();
            RegisterClaim();
            RegisterClaimOne();
            RegisterIsClosed();
            RegisterComplete();
            RegisterSettle();
        }

        /// <summary>
        /// 注册 oa.is_closed——单状态判断（轮询用）
        /// </summary>
        private static void RegisterIsClosed()
        {
            BrickContract contract = new BrickContract("oa.is_closed", "Mau.Bricks.OaBrick.IsClosed");
            contract.Inputs.Add(new BrickPort("officeId", typeof(long), "Office ID"));
            contract.Outputs.Add(new BrickPort("closed", typeof(bool), "是否已 Closed"));
            contract.Return = BrickReturnKind.Bool;
            contract.Duration = BrickDuration.Sync;
            contract.Thread = "main";
            BrickRegistry.Register(contract);
        }

        /// <summary>
        /// 注册 oa.post——建空单，载荷经 set_* 逐 Key 写
        /// </summary>
        private static void RegisterPost()
        {
            BrickContract contract = new BrickContract("oa.post", "Mau.Bricks.OaBrick.Post");
            contract.Inputs.Add(new BrickPort("dogId", typeof(long), "所有者 LongId"));
            contract.Inputs.Add(new BrickPort("officeType", typeof(string), "工单大类"));
            contract.Inputs.Add(new BrickPort("officeName", typeof(string), "固定词汇——执行方据此判断"));
            contract.Inputs.Add(new BrickPort("timeoutTicks", typeof(long), "超时帧数"));
            contract.Outputs.Add(new BrickPort("officeId", typeof(long), "新 Office 的 ID"));
            contract.Return = BrickReturnKind.Bool;
            contract.Duration = BrickDuration.Sync;
            contract.Thread = "main";
            BrickRegistry.Register(contract);
        }

        /// <summary>
        /// 注册 oa.set_int——写请求载荷 int 值（本人/Open）
        /// </summary>
        private static void RegisterSetInt()
        {
            BrickContract contract = new BrickContract("oa.set_int", "Mau.Bricks.OaBrick.SetInt");
            contract.Inputs.Add(new BrickPort("officeId", typeof(long), "Office ID"));
            contract.Inputs.Add(new BrickPort("dogId", typeof(long), "所有者 LongId"));
            contract.Inputs.Add(new BrickPort("key", typeof(string), "Key"));
            contract.Inputs.Add(new BrickPort("value", typeof(int), "int 值"));
            contract.Return = BrickReturnKind.Bool;
            contract.Duration = BrickDuration.Sync;
            contract.Thread = "main";
            BrickRegistry.Register(contract);
        }

        /// <summary>
        /// 注册 oa.set_str——写请求载荷 str 值（本人/Open）
        /// </summary>
        private static void RegisterSetStr()
        {
            BrickContract contract = new BrickContract("oa.set_str", "Mau.Bricks.OaBrick.SetStr");
            contract.Inputs.Add(new BrickPort("officeId", typeof(long), "Office ID"));
            contract.Inputs.Add(new BrickPort("dogId", typeof(long), "所有者 LongId"));
            contract.Inputs.Add(new BrickPort("key", typeof(string), "Key"));
            contract.Inputs.Add(new BrickPort("value", typeof(string), "str 值"));
            contract.Return = BrickReturnKind.Bool;
            contract.Duration = BrickDuration.Sync;
            contract.Thread = "main";
            BrickRegistry.Register(contract);
        }

        /// <summary>
        /// 注册 oa.get_int——读请求载荷 int 值
        /// </summary>
        private static void RegisterGetInt()
        {
            BrickContract contract = new BrickContract("oa.get_int", "Mau.Bricks.OaBrick.GetInt");
            contract.Inputs.Add(new BrickPort("officeId", typeof(long), "Office ID"));
            contract.Inputs.Add(new BrickPort("key", typeof(string), "Key"));
            contract.Outputs.Add(new BrickPort("value", typeof(int), "int 值"));
            contract.Return = BrickReturnKind.Bool;
            contract.Duration = BrickDuration.Sync;
            contract.Thread = "main";
            BrickRegistry.Register(contract);
        }

        /// <summary>
        /// 注册 oa.get_str——读请求载荷 str 值
        /// </summary>
        private static void RegisterGetStr()
        {
            BrickContract contract = new BrickContract("oa.get_str", "Mau.Bricks.OaBrick.GetStr");
            contract.Inputs.Add(new BrickPort("officeId", typeof(long), "Office ID"));
            contract.Inputs.Add(new BrickPort("key", typeof(string), "Key"));
            contract.Outputs.Add(new BrickPort("value", typeof(string), "str 值"));
            contract.Return = BrickReturnKind.Bool;
            contract.Duration = BrickDuration.Sync;
            contract.Thread = "main";
            BrickRegistry.Register(contract);
        }

        /// <summary>
        /// 注册 oa.list
        /// </summary>
        private static void RegisterList()
        {
            BrickContract contract = new BrickContract("oa.list", "Mau.Bricks.OaBrick.List");
            contract.Inputs.Add(new BrickPort("officeType", typeof(string), "工单大类"));
            contract.Inputs.Add(new BrickPort("officeNames", typeof(string[]), "执行方能干的候选"));
            contract.Outputs.Add(new BrickPort("offices", typeof(Office[]), "匹配的 Open 单"));
            contract.Return = BrickReturnKind.Bool;
            contract.Duration = BrickDuration.Sync;
            contract.Thread = "main";
            BrickRegistry.Register(contract);
        }

        /// <summary>
        /// 注册 oa.claim
        /// </summary>
        private static void RegisterClaim()
        {
            BrickContract contract = new BrickContract("oa.claim", "Mau.Bricks.OaBrick.Claim");
            contract.Inputs.Add(new BrickPort("catId", typeof(long), "认领者 LongId"));
            contract.Inputs.Add(new BrickPort("officeIds", typeof(long[]), "待锁的 OfficeId 数组"));
            contract.Outputs.Add(new BrickPort("claimed", typeof(Office[]), "锁成功的 Office 列表"));
            contract.Return = BrickReturnKind.Bool;
            contract.Duration = BrickDuration.Sync;
            contract.Thread = "main";
            BrickRegistry.Register(contract);
        }

        /// <summary>
        /// 注册 oa.claim_one——单单认领（工具循环展开用）
        /// </summary>
        private static void RegisterClaimOne()
        {
            BrickContract contract = new BrickContract("oa.claim_one", "Mau.Bricks.OaBrick.ClaimOne");
            contract.Inputs.Add(new BrickPort("catId", typeof(long), "认领者 LongId"));
            contract.Inputs.Add(new BrickPort("officeId", typeof(long), "待锁的 OfficeId"));
            contract.Outputs.Add(new BrickPort("claimed", typeof(Office), "锁成功的 Office"));
            contract.Return = BrickReturnKind.Bool;
            contract.Duration = BrickDuration.Sync;
            contract.Thread = "main";
            BrickRegistry.Register(contract);
        }

        /// <summary>
        /// 注册 oa.complete——回执双字典载荷
        /// </summary>
        private static void RegisterComplete()
        {
            BrickContract contract = new BrickContract("oa.complete", "Mau.Bricks.OaBrick.Complete");
            contract.Inputs.Add(new BrickPort("officeId", typeof(long), "Office ID"));
            contract.Inputs.Add(new BrickPort("catId", typeof(long), "认领者 LongId"));
            contract.Inputs.Add(new BrickPort("result", typeof(OfficeData), "回执双字典载荷"));
            contract.Return = BrickReturnKind.Bool;
            contract.Duration = BrickDuration.Sync;
            contract.Thread = "main";
            BrickRegistry.Register(contract);
        }

        /// <summary>
        /// 注册 oa.settle
        /// </summary>
        private static void RegisterSettle()
        {
            BrickContract contract = new BrickContract("oa.settle", "Mau.Bricks.OaBrick.Settle");
            contract.Inputs.Add(new BrickPort("officeId", typeof(long), "Office ID"));
            contract.Inputs.Add(new BrickPort("catId", typeof(long), "请求者 LongId"));
            contract.Return = BrickReturnKind.Bool;
            contract.Duration = BrickDuration.Sync;
            contract.Thread = "main";
            BrickRegistry.Register(contract);
        }
    }
}
