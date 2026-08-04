// ═══════════════════════════════════════════════
// 积木: approval.request / approval.resolve / approval.reject / approval.pending
// ID:   BRIK-APPROVAL-001 ~ 004
// 作用: 人机确认审批中枢——Pending 队列 + Resolve/Reject + 超时默认项
// 引用: Mau.Bricks.Approval → Mau.Contracts（BrickRegistry）
// 原理: 线程安全 Pending 表 + 超时任务——外观层读取快照并显式 Resolve
// 常用: CH4 HumanAsk / Shell 命令确认 / 破坏性操作审批
// ═══════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Mau.Contracts;

namespace Mau.Bricks
{
    /// <summary>
    /// 审批请求——等待人类选择，可被任意外观层读取
    /// </summary>
    public sealed class ApprovalRequest
    {
        /// <summary>
        /// 请求 ID
        /// </summary>
        public string RequestId;

        /// <summary>
        /// 面向人的问题
        /// </summary>
        public string Question;

        /// <summary>
        /// 至少两个选项
        /// </summary>
        public string[] Options;

        /// <summary>
        /// 超时默认索引
        /// </summary>
        public int DefaultIndex;

        /// <summary>
        /// 超时秒数
        /// </summary>
        public int TimeoutSeconds;

        /// <summary>
        /// 风险说明
        /// </summary>
        public string RiskSummary;

        /// <summary>
        /// 创建空请求
        /// </summary>
        public ApprovalRequest()
        {
            RequestId = "";
            Question = "";
            Options = Array.Empty<string>();
            DefaultIndex = 0;
            TimeoutSeconds = 20;
            RiskSummary = "";
        }
    }

    /// <summary>
    /// 审批结果——人类选择或超时默认
    /// </summary>
    public struct ApprovalResult
    {
        /// <summary>
        /// 请求 ID
        /// </summary>
        public string RequestId;

        /// <summary>
        /// 选中索引
        /// </summary>
        public int SelectedIndex;

        /// <summary>
        /// 选中标签
        /// </summary>
        public string SelectedLabel;

        /// <summary>
        /// 是否由超时默认完成
        /// </summary>
        public bool TimedOut;
    }

    /// <summary>
    /// 审批积木——静态审批中枢。不创建窗口；外观层读取 Pending 并显式 Resolve。
    /// 由 CH3 CH_ApprovalBroker 移植（简化：静态实例 + 无 MainInput 绑定）。
    /// </summary>
    public static class ApprovalBrick
    {
        /// <summary>
        /// 审批表锁
        /// </summary>
        private static readonly object _gate = new object();

        /// <summary>
        /// 等待审批表——requestId → 请求
        /// </summary>
        private static readonly Dictionary<string, ApprovalRequest> _pending =
            new Dictionary<string, ApprovalRequest>(StringComparer.Ordinal);

        /// <summary>
        /// 请求审批——登记后外观层可读取
        /// </summary>
        /// <param name="requestId">请求 ID</param>
        /// <param name="question">问题</param>
        /// <param name="options">选项数组（≥2）</param>
        /// <param name="defaultIndex">默认索引</param>
        /// <param name="timeoutSeconds">超时秒数</param>
        /// <returns>true=登记成功</returns>
        public static bool Request(string requestId, string question,
            string[] options, int defaultIndex, int timeoutSeconds)
        {
            if (string.IsNullOrWhiteSpace(requestId)
                || string.IsNullOrWhiteSpace(question)
                || options == null || options.Length < 2)
            {
                return false;
            }
            ApprovalRequest request = new ApprovalRequest();
            request.RequestId = requestId;
            request.Question = question;
            request.Options = options;
            request.DefaultIndex = defaultIndex;
            request.TimeoutSeconds = timeoutSeconds;
            lock (_gate)
            {
                if (_pending.ContainsKey(requestId))
                {
                    return false;
                }
                _pending[requestId] = request;
            }
            return true;
        }

        /// <summary>
        /// 选择一个有效选项完成审批
        /// </summary>
        /// <param name="requestId">请求 ID</param>
        /// <param name="selectedIndex">选项索引</param>
        /// <param name="result">结果</param>
        /// <returns>true=完成仍在等待的审批</returns>
        public static bool Resolve(string requestId, int selectedIndex,
            out ApprovalResult result)
        {
            result = new ApprovalResult();
            ApprovalRequest? request;
            lock (_gate)
            {
                if (!_pending.TryGetValue(requestId, out request) || request == null
                    || selectedIndex < 0 || selectedIndex >= request.Options.Length)
                {
                    return false;
                }
                _pending.Remove(requestId);
            }
            result.RequestId = requestId;
            result.SelectedIndex = selectedIndex;
            result.SelectedLabel = request.Options[selectedIndex];
            result.TimedOut = false;
            return true;
        }

        /// <summary>
        /// 显式拒绝——不选任何选项
        /// </summary>
        /// <param name="requestId">请求 ID</param>
        /// <returns>true=完成仍等待的请求</returns>
        public static bool Reject(string requestId)
        {
            lock (_gate)
            {
                if (!_pending.ContainsKey(requestId))
                {
                    return false;
                }
                _pending.Remove(requestId);
            }
            return true;
        }

        /// <summary>
        /// 读取全部等待审批的深拷贝快照
        /// </summary>
        /// <param name="pending">请求数组（JSON 序列化友好）</param>
        /// <returns>true=成功</returns>
        public static bool GetPending(out string pending)
        {
            List<string> lines = new List<string>();
            lock (_gate)
            {
                string[] keys = new string[_pending.Count];
                _pending.Keys.CopyTo(keys, 0);
                Array.Sort(keys, StringComparer.Ordinal);
                for (int i = 0; i < keys.Length; i = i + 1)
                {
                    ApprovalRequest request = _pending[keys[i]];
                    lines.Add(keys[i] + "|" + request.Question + "|"
                        + string.Join(",", request.Options) + "|"
                        + request.DefaultIndex.ToString() + "|"
                        + request.TimeoutSeconds.ToString());
                }
            }
            pending = string.Join("\n", lines);
            return true;
        }

        /// <summary>
        /// 等待数量
        /// </summary>
        /// <param name="count">等待数量</param>
        /// <returns>true=成功</returns>
        public static bool PendingCount(out int count)
        {
            lock (_gate)
            {
                count = _pending.Count;
            }
            return true;
        }
    }

    /// <summary>
    /// 审批积木注册——进程启动时调用一次
    /// </summary>
    public static class ApprovalBrickRegistration
    {
        /// <summary>
        /// 注册全部审批积木
        /// </summary>
        public static void RegisterAll()
        {
            RegisterRequest();
            RegisterResolve();
            RegisterReject();
            RegisterPending();
        }

        /// <summary>
        /// 注册 approval.request
        /// </summary>
        private static void RegisterRequest()
        {
            BrickContract contract = new BrickContract("approval.request", "Mau.Bricks.ApprovalBrick.Request");
            contract.Inputs.Add(new BrickPort("requestId", typeof(string), "请求 ID"));
            contract.Inputs.Add(new BrickPort("question", typeof(string), "问题"));
            contract.Inputs.Add(new BrickPort("options", typeof(string[]), "选项数组"));
            contract.Inputs.Add(new BrickPort("defaultIndex", typeof(int), "默认索引"));
            contract.Inputs.Add(new BrickPort("timeoutSeconds", typeof(int), "超时秒数"));
            contract.Return = BrickReturnKind.Bool;
            contract.Duration = BrickDuration.Sync;
            contract.Thread = "main";
            BrickRegistry.Register(contract);
        }

        /// <summary>
        /// 注册 approval.resolve
        /// </summary>
        private static void RegisterResolve()
        {
            BrickContract contract = new BrickContract("approval.resolve", "Mau.Bricks.ApprovalBrick.Resolve");
            contract.Inputs.Add(new BrickPort("requestId", typeof(string), "请求 ID"));
            contract.Inputs.Add(new BrickPort("selectedIndex", typeof(int), "选项索引"));
            contract.Outputs.Add(new BrickPort("result", typeof(ApprovalResult), "结果"));
            contract.Return = BrickReturnKind.Bool;
            contract.Duration = BrickDuration.Sync;
            contract.Thread = "main";
            BrickRegistry.Register(contract);
        }

        /// <summary>
        /// 注册 approval.reject
        /// </summary>
        private static void RegisterReject()
        {
            BrickContract contract = new BrickContract("approval.reject", "Mau.Bricks.ApprovalBrick.Reject");
            contract.Inputs.Add(new BrickPort("requestId", typeof(string), "请求 ID"));
            contract.Return = BrickReturnKind.Bool;
            contract.Duration = BrickDuration.Sync;
            contract.Thread = "main";
            BrickRegistry.Register(contract);
        }

        /// <summary>
        /// 注册 approval.pending
        /// </summary>
        private static void RegisterPending()
        {
            BrickContract contract = new BrickContract("approval.pending", "Mau.Bricks.ApprovalBrick.GetPending");
            contract.Outputs.Add(new BrickPort("pending", typeof(string), "等待审批快照"));
            contract.Return = BrickReturnKind.Bool;
            contract.Duration = BrickDuration.Sync;
            contract.Thread = "main";
            BrickRegistry.Register(contract);
        }
    }
}
