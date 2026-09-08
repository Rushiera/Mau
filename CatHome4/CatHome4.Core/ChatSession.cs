using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using Mau.Runtime;
using CatHome4.Contracts;

namespace CH4
{
    /// <summary>
    /// 会话相位——P9.1 四相环（动作段 StartLlm/LlmDone/StartToolBatch/ToolBatchDone/Done 封装进相位 Enter/Exit——design-llm-streaming §6.2）。
    /// </summary>
    internal enum ChatPhase
    {
        /// <summary>空闲——无活跃轮次（可接收用户消息）</summary>
        Idle,
        /// <summary>LLM 后台流式等待——主线程轮询完成槽</summary>
        LlmRunning,
        /// <summary>工具批执行——Dog 逐帧轮询</summary>
        ToolBatchRunning,
        /// <summary>终态动作段——前文落盘 + 状态复位（一帧完成回 Idle）</summary>
        Done
    }

    /// <summary>
    /// 宿主会话实体——上下文/前文/LLM 槽/工具批/相位环（P9.1 会话对象化——design-llm-streaming §六规格）。
    /// 线程模型：相位与上下文仅主线程推进；LLM 槽后台 Task.Run 写、主线程轮询（volatile Busy 置位/复位）。
    /// SSE 推送/日志/工具直执经宿主服务引用回调——ChatSession 不持有宿主静态面。
    /// </summary>
    internal sealed partial class ChatSession
    {
        /// <summary>LLM 超时帧上限——同原 WaitForLlm MaxFramesPerRun 语义（30000 帧 ≈ 25 分钟空转上限）</summary>
        private const long LlmTimeoutFrames = 30000;

        /// <summary>工具批等待帧上限——同原 MaxDogWaitFrames（4800 最长工具 + 600 余量）</summary>
        private const long ToolBatchWaitFrames = 4800 + 600;

        /// <summary>工具轮次收敛上限——同 MaxToolRounds（P8.5b 上调 3→10 保持）</summary>
        private const int MaxToolRounds = 10;




        /// <summary>工具单 Dog owner ID——宿主 Dog 域（同 ToolOwnerId——OA 未开存活校验，多 Dog 未来可扩展独立 ID）</summary>
        private const long ToolOwnerId = 1;

        // [段1] 标识与持久化
        /// <summary>会话唯一 ID——创建时间戳注入（P9.3 协议面对外）</summary>
        private readonly string _id;

        /// <summary>显示名——P9.1 为 "majordomo"；P9.3 用户输入</summary>
        private readonly string _displayName;

        /// <summary>消息历史——会话上下文容器（第一条 system；Mau.Runtime 实体复用）</summary>
        private readonly ChatContext _context;

        /// <summary>前文落盘——P9.1 保持 Data/sessions/majordomo.json（sessions/&lt;id&gt;.json 化是 P9.4）</summary>
        private readonly SessionStore _store;
// [段2] LLM 槽 6 字段——后台 Task.Run 写、主线程轮询读
        /// <summary>LLM 后台运行中——volatile 置位（后台 finally 复位）</summary>
        private volatile bool _llmBusy;

        /// <summary>LLM 后台结果——完整回复文本</summary>
        private string _llmResultText;

        /// <summary>LLM 后台结果——完整思考文本（工具轮次回传铁律）</summary>
        private string _llmReasoning;

        /// <summary>LLM 后台结果——tool_calls JSON 数组（translate 聚合后整体）</summary>
        private string _llmToolCallsJson;

        /// <summary>LLM 后台错误标志</summary>
        private bool _llmError;

        /// <summary>LLM 后台错误文本（ERR| 前缀——失败可见性）</summary>
        private string _llmErrorText;

        /// <summary>本轮是否发生过重试——首个 Text/Reasoning 到达时回填 retry 视图 resolved（S2 §8.4）</summary>
        private bool _sawRetry;

        /// <summary>retry 气泡视图序号——多次重试复用同一气泡（replaceSeq 替换不堆叠）</summary>
        private long _retrySeq;

        /// <summary>空回复续传计数——无回复/纯空格时同上下文重发（无上限——LLM 兜底；整轮清零）</summary>
        private int _emptyReplyRetry;

        /// <summary>STREAM_CLOSED 续传标志——SSE 流未以 [DONE] 结束时置位（PumpLlm 检查；续传后复位）</summary>
        private bool _streamClosedRetry;

        // [段2b] P6 中止——暂停标志与取消令牌（LLM 流物理取消面）
        /// <summary>中止请求标志——HTTP 线程置位（volatile 跨线程可见），主线程 Pump 消费执行收尾</summary>
        private volatile bool _pauseRequested;

        /// <summary>本轮 LLM 流取消令牌——LaunchLlm 创建；Pause 时 Cancel（下一轮重建——取消不跨轮）</summary>
        private System.Threading.CancellationTokenSource _pauseCts;

        /// <summary>Token 用量整轮累计——prompt（含 cache hit）</summary>
        private long _usagePrompt;

        /// <summary>Token 用量整轮累计——completion</summary>
        private long _usageCompletion;

        /// <summary>Token 用量整轮累计——cache hit（CH2 双格式：prompt_cache_hit_tokens / cached_tokens）</summary>
        private long _usageCacheHit;

        /// <summary>最近一轮真实 usage 统计——CloseRound 落盘（info 自查/前端显示数据源；零估算）</summary>
        private SessionStats _lastStats;

        /// <summary>单次前文长度——最近一次请求的 prompt（覆盖式；非累计——前文长度数据源）</summary>
        private long _contextTokens;

        /// <summary>本轮开始时间戳——Stopwatch.GetTimestamp（roundsum 总耗时）</summary>
        private long _roundStartTick;

        /// <summary>本轮工具调用次数——工具 Dog 登记处累加（roundsum toolCount）</summary>
        private int _toolCallCount;

        /// <summary>当前计时相位——PhaseLink/PhaseThink/PhaseTool/PhaseReply（-1=无活跃）</summary>
        private int _phaseKind = -1;

        /// <summary>当前相位开始时间戳——Stopwatch.GetTimestamp</summary>
        private long _phaseStartTick;

        /// <summary>四态累计毫秒——link/think/tool/reply（相位切换结算）</summary>
        private long[] _phaseAccumMs = new long[4];

        /// <summary>计时相位常量——链路（等待首流）</summary>
        private const int PhaseLink = 0;

        /// <summary>计时相位常量——思考（reasoning 流）</summary>
        private const int PhaseThink = 1;

        /// <summary>计时相位常量——工具（工具批执行）</summary>
        private const int PhaseTool = 2;

        /// <summary>计时相位常量——回复（text 流）</summary>
        private const int PhaseReply = 3;

        // [段3] 工具批
        /// <summary>工具批执行中——reload 拒绝检查面（任一会话 TRUE 即拒绝）</summary>
        private bool _toolBatchActive;
        /// <summary>工具单列表——普通工单（OA 认领；host-* 延迟直执登记）</summary>
        private readonly List<ToolOrderDog> _dogs;
        /// <summary>host-* 延迟直执清单——批次末尾宿主直执（顺序保证：同批 mau-proj 等先完成产物落地）</summary>
        private readonly List<ToolOrderDog> _hostDogs;

        // [段4] 相位环
        /// <summary>当前相位</summary>
        private ChatPhase _phase;

        /// <summary>当前工具轮次（0 起——MaxToolRounds 收敛）</summary>
        private long _round;

        /// <summary>当前相位已运行帧数——超时计时面</summary>
        private long _phaseFrames;

        /// <summary>待处理用户消息队列——忙时排队（原同步阻塞天然排队语义保持）</summary>
        /// <summary>
        /// 待处理消息实体——内容 + 来源（user=人发送/system=系统自动）。
        /// </summary>
        private sealed class PendingMessage
        {
            /// <summary>消息内容</summary>
            public string Content;

            /// <summary>来源——user/system</summary>
            public string Source;
        }

        /// <summary>待处理用户消息队列——忙时排队（原同步阻塞天然排队语义保持）</summary>
        private readonly Queue<PendingMessage> _pending;

        // [段5] 宿主服务引用（构造注入——宿主级共享面）
        /// <summary>LLM 运行时——ChatStream 调度（宿主 Bootstrap 注入；M3 apiConfigId 切换 SwapLlmRuntime 替换）</summary>
        private ILlmRuntime _llmRuntime;

        /// <summary>OA 工单平台——Dog Post/轮询</summary>
        private readonly OA _oa;

        /// <summary>工具定义表——后台流式携带（宿主 BuildToolSpecs 产物；M3 改 toolNames 新会话 SetToolSpecs 更新）</summary>
        private ToolSpec[] _tools;

        /// <summary>宿主工具直执回调——host-* 延迟直执（批次末尾执行；OA 工具一律走 OA 认领，无直执）</summary>
        private readonly Func<string, string, string> _executeTool;

        /// <summary>猫 key——工具执行按猫裁剪（M4e 白名单；默认猫=majordomo；多猫=会话 ID）</summary>
        private string _catKey = "";

        /// <summary>
        /// 设置猫 key——构造后由创建方赋值（默认猫 majordomo / 多猫会话 ID）。
        /// </summary>
        /// <param name="catKey">猫 key</param>
        public void SetCatKey(string catKey)
        {
            _catKey = catKey;
        }

        /// <summary>
        /// 工具执行包装——设置猫上下文后调宿主直执（M4e：按猫解析；执行后恢复）。仅 host-* 延迟直执使用。
        /// </summary>
        /// <param name="name">工具名</param>
        /// <param name="args">参数 JSON</param>
        /// <returns>执行结果</returns>
        private string RunTool(string name, string args)
        {
            string prev = ToolCatContext.CurrentCatKey;
            ToolCatContext.SetCat(_catKey);
            try
            {
                return _executeTool(name, args);
            }
            finally
            {
                ToolCatContext.SetCat(prev);
            }
        }

        /// <summary>HTTP 外观层——SSE 转发面（Bootstrap 段6 宿主 HTTP 启动后 Attach 赋值——构造时宿主 HTTP 未启动）</summary>
        private IHostPush _httpHost;

        /// <summary>环境信息提供器——info 内置工具数据源（入口壳注入；空=工具返回不可用）</summary>
        private Func<string> _envInfoProvider;

        /// <summary>
        /// 建立会话实体——宿主级服务经构造注入；会话生命周期数据自持。
        /// </summary>
        /// <param name="id">会话 ID（时间戳注入）</param>
        /// <param name="displayName">显示名</param>
        /// <param name="context">消息历史容器</param>
        /// <param name="store">前文落盘</param>
        /// <param name="llmRuntime">LLM 运行时</param>
        /// <param name="oa">OA 工单平台</param>
        /// <param name="tools">工具定义表</param>
        /// <param name="executeTool">工具直执回调</param>
        /// <param name="viewStore">视图存储——F4 视图持久化</param>
        public ChatSession(string id, string displayName, ChatContext context, SessionStore store, ILlmRuntime llmRuntime, OA oa, ToolSpec[] tools, Func<string, string, string> executeTool, SessionViewStore viewStore)
        {
            if (id == null)
            {
                id = "";
            }
            if (displayName == null)
            {
                displayName = "";
            }
            _id = id;
            _displayName = displayName;
            _context = context;
            _store = store;
            _viewStore = viewStore;
            _llmRuntime = llmRuntime;
            _oa = oa;
            _tools = tools;
            _executeTool = executeTool;
            _dogs = new List<ToolOrderDog>();
            _hostDogs = new List<ToolOrderDog>();
            _pending = new Queue<PendingMessage>();
            _phase = ChatPhase.Idle;
            _round = 0;
            _phaseFrames = 0;
            _llmResultText = "";
            _llmReasoning = "";
            _llmToolCallsJson = "";
            _llmErrorText = "";
            _httpHost = null;
        }

        /// <summary>真实前文末条消息——append 后取最近一条生成视图块（F4 视图钩子）</summary>
        private LlmMessage LastMessage()
        {
            LlmMessage[] all = _context.GetMessages();
            return all[all.Length - 1];
        }

        /// <summary>当前视图时间戳——Unix 毫秒（视图排序键——真实时序权威，跨重启稳定；替代宿主帧号）</summary>
        private static long ViewTimestamp()
        {
            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        /// <summary>重建视图层——从真实前文完全重置（启动恢复后调用；真实前文绝对可用）</summary>
        public void RebuildView()
        {
            _viewStore.Rebuild(_context.GetMessages());
            // 注入报告——启动恢复时从 view.json 读回（非真实前文派生；Rebuild 不重建）
            _viewStore.LoadInjectReport();
        }

        /// <summary>清空视图层——session.new 清前文时同步（视图随生命周期清理）</summary>
        public void ClearView()
        {
            _viewStore.Clear();
        }

        /// <summary>
        /// 会话重置显式事件——session.new 清前文后推送（前端收到后清空气泡再拉 history——消除竞态；问题一修复）。
        /// </summary>
        public void PushSessionReset()
        {
            if (_httpHost != null)
            {
                string resetJson = "{\"type\":\"session_reset\"}";
                _httpHost.PushView("control", resetJson, -1, 0);
            }
        }

        /// <summary>
        /// 设置已加载统计——启动恢复时从会话文件读出（TryLoad 带 stats 重载；旧文件 null=零值）。
        /// </summary>
        /// <param name="stats">持久化统计（可空）</param>
        public void SetLoadedStats(SessionStats? stats)
        {
            if (stats != null)
            {
                _lastStats = stats.Value;
            }
        }

        /// <summary>
        /// 重置统计——session.new 清前文后调用（新会话零统计起算）。
        /// </summary>
        public void ResetStats()
        {
            _lastStats = new SessionStats();
        }

        /// <summary>
        /// 最近一轮真实 usage 统计——info 自查/前端显示数据源（零估算；CloseRound 更新）。
        /// </summary>
        public SessionStats LastStats
        {
            get
            {
                return _lastStats;
            }
        }

        /// <summary>
        /// 设置注入报告——session.new 后调用（持久化进视图：内存设置 + view.json 落盘；独立字段 Rebuild 不清）。
        /// </summary>
        /// <param name="json">注入报告 JSON（BuildInjectReportJson 产物）</param>
        public void SetInjectReport(string json)
        {
            _viewStore.SetInjectReport(json);
            _viewStore.Save();
        }

        /// <summary>内存视图块——按生成序（history 数据源；F4 视图持久化）</summary>
        public ViewBlock[] GetViewBlocks()
        {
            return _viewStore.GetBlocks();
        }

        /// <summary>会话唯一 ID</summary>
        public string Id
        {
            get
            {
                return _id;
            }
        }

        /// <summary>会话显示名（P9.3 前端可见）</summary>
        public string DisplayName
        {
            get
            {
                return _displayName;
            }
        }

        /// <summary>消息历史——会话操纵面复用（视图/清空/计数）</summary>
        public ChatContext Context
        {
            get
            {
                return _context;
            }
        }

        /// <summary>前文落盘——会话操纵面复用（session.new 重注入落盘）</summary>
        public SessionStore Store
        {
            get
            {
                return _store;
            }
        }

        /// <summary>工具批执行中——reload 拒绝检查（任一会话 TRUE 即拒绝）</summary>
        public bool ToolBatchActive
        {
            get
            {
                return _toolBatchActive;
            }
        }

        /// <summary>
        /// 更新工具声明面——session.new 时从最新 cat.cfg 重裁剪后调用（M3：改 toolNames 新会话生效；主线程 Idle 时调用）。
        /// </summary>
        /// <param name="specs">新声明面数组（FilterToolSpecs 产物）</param>
        public void SetToolSpecs(ToolSpec[] specs)
        {
            _tools = specs;
        }

        /// <summary>
        /// 换 LLM 运行时——apiConfigId 切换立即生效（M3：API 配置切换即时；主线程 Idle 时调用）。
        /// </summary>
        /// <param name="runtime">新运行时实例（按新 API 配置构造）</param>
        public void SwapLlmRuntime(ILlmRuntime runtime)
        {
            _llmRuntime = runtime;
        }

        /// <summary>会话空闲——AllIdle 判定面</summary>
        public bool IsIdle
        {
            get
            {
                return _phase == ChatPhase.Idle;
            }
        }

        /// <summary>当前相位（快照观测面）</summary>
        public ChatPhase Phase
        {
            get
            {
                return _phase;
            }
        }

        /// <summary>当前工具轮次（快照观测面——状态卡预览用）</summary>
        public long Round
        {
            get
            {
                return _round;
            }
        }

        /// <summary>消息历史条数（快照观测面——状态卡预览用）</summary>
        public int MsgCount
        {
            get
            {
                return _context.GetMessages().Length;
            }
        }

        /// <summary>待处理消息数（快照观测面——忙时排队可见）</summary>
        public int PendingCount
        {
            get
            {
                return _pending.Count;
            }
        }

        /// <summary>Note 计划激活中——任务未全完成（快照观测面——状态卡 Note 徽标）</summary>
        public bool NoteActive
        {
            get
            {
                return _noteTasks != null && _noteCurrent < _noteTasks.Length;
            }
        }

        /// <summary>
        /// 附加 HTTP 外观层——Bootstrap 段6 宿主 HTTP 启动后调用（SSE 转发面就位）。
        /// </summary>
        /// <param name="host">HTTP 外观层实例</param>
        public void AttachHost(IHostPush host)
        {
            _httpHost = host;
        }

        /// <summary>
        /// 注入环境信息提供器——info 内置工具数据源（拆分后由入口壳注入；原直接调用 Program.BuildEnvInfo）
        /// </summary>
        /// <param name="provider">环境信息构建委托</param>
        public void AttachEnvInfo(Func<string> provider)
        {
            _envInfoProvider = provider;
        }

        /// <summary>
        /// 中止当前轮次——取消 LLM 流（后台 Task 取消）/ 放弃未完成工具批；已完成内容保留 + 前文格式修复（不裁剪——裁剪唯一通道是回滚）。
        /// 线程安全：仅置位 + Cancel（volatile 标志跨线程可见）；相位推进在主线程 Pump 消费。
        /// </summary>
        public void Pause()
        {
            if (_phase == ChatPhase.Idle)
            {
                // 无进行中对话——前端按钮 idle 态禁用；防御性返回
                return;
            }
            _pauseRequested = true;
            if (_pauseCts != null)
            {
                _pauseCts.Cancel();
            }
            LogStore.Add("CatHome4", 1, "收到中止指令——正在停止本轮（已完成内容保留）", "CHAT");
        }

        /// <summary>
        /// 中止收尾——主线程 Pump 消费（相位串行）。已完成工具结果保留入 Ctx（不丢信息）；未完成放弃（格式修复补占位）；
        /// 上下文 ReplaceMessages 格式修复（S3 方案——孤儿 tool_calls 补占位/孤立结果丢弃）；前文落盘（不裁剪）；
        /// 复位 Idle + chat_state=idle + 推 paused 事件（前端 seal 流式容器 + 按钮复位）。
        /// </summary>
        private void PauseFinalize()
        {
            _pauseRequested = false;
            // [段0] 已完成工具结果保留——未完成放弃（ReplaceMessages 对未配对声明补占位）
            for (int i = 0; i < _dogs.Count; i++)
            {
                ToolOrderDog dog = _dogs[i];
                if (dog.IsClosed && dog.Result != null && dog.Result.Length > 0)
                {
                    _context.AddToolResult(dog.ToolCallId, dog.Name, dog.Result);
                }
            }
            _dogs.Clear();
            _hostDogs.Clear();
            _toolBatchActive = false;
            // [段1] 上下文格式修复——S3 ReplaceMessages 原地（幂等；孤儿 tool_calls 补占位/孤立结果丢弃）
            _context.ReplaceMessages(_context.GetMessages());
            // [段2] 前文落盘——tool 结果截断 ≤800（对齐 CloseRound——落盘副本防膨胀）
            LlmMessage[] toSave = _context.GetMessages();
            for (int i = 0; i < toSave.Length; i++)
            {
                if (toSave[i].Role == LlmRole.Tool && toSave[i].Content != null && toSave[i].Content.Length > 800)
                {
                    toSave[i].Content = TruncateText(toSave[i].Content, 800);
                }
            }
            _lastStats.EntryCount = toSave.Length;
            _store.Save(toSave, _lastStats);
            // [段3] 视图序号复位——流式容器由前端 seal（已显示内容保留）
            _textStreamSeq = 0;
            _reasonStreamSeq = 0;
            // [段4] 状态复位——Idle（不推 chatdone/roundsum/Note 拉起——中止非正常完成语义）
            _round = 0;
            _phase = ChatPhase.Idle;
            _phaseFrames = 0;
            _llmBusy = false;
            _llmError = false;
            _llmErrorText = "";
            _llmResultText = "";
            _llmReasoning = "";
            _llmToolCallsJson = "";
            _emptyReplyRetry = 0;
            _streamClosedRetry = false;
            _sawRetry = false;
            DataBox.Set<string>("global", "chat_state", "idle");
            // [段5] 前端通知——control paused 事件（seal + 按钮复位）
            if (_httpHost != null)
            {
                string pauseJson = "{\"type\":\"paused\",\"text\":\"已停止本轮（前文保留 + 格式修复）\"}";
                _httpHost.PushView("control", pauseJson, -1, 0);
            }
            LogStore.Add("CatHome4", 1, "本轮已中止——前文保留 + 格式修复（" + toSave.Length.ToString() + " 条消息）", "CHAT");
        }

        /// <summary>
        /// 用户消息入队——Idle 时 Pump 立即启动轮次；忙时排队等待（原同步阻塞天然排队语义保持）。
        /// 主线程泵消费调用（ThreadGuard：不推进相位——启动在 Pump）。
        /// </summary>
        /// <param name="content">用户消息内容</param>
        /// <param name="source">来源——user（人发送，默认）/system（系统自动）</param>
        public void PostUserMessage(string content, string source = "user")
        {
            if (content == null || content.Length == 0)
            {
                return;
            }
            if (source == null || source.Length == 0)
            {
                source = "user";
            }
            PendingMessage msg = new PendingMessage();
            msg.Content = content;
            msg.Source = source;
            _pending.Enqueue(msg);
        }

        /// <summary>
        /// 一帧推进——主线程每帧调用（PumpSessions 轮转）。相位分发：Idle 检查排队 / Llm 轮询完成槽 / 工具批轮询 Dog / 终态落盘复位。
        /// </summary>
        public void Pump()
        {
            // P6 中止——暂停请求消费（任何相位统一收尾：LLM 流取消/工具批放弃/已完成保留）
            if (_pauseRequested)
            {
                PauseFinalize();
                return;
            }
            if (_phase == ChatPhase.Idle)
            {
                if (_pending.Count > 0)
                {
                    PendingMessage next = _pending.Dequeue();
                    StartRound(next.Content, next.Source);
                }
                return;
            }
            if (_phase == ChatPhase.LlmRunning)
            {
                PumpLlm();
                return;
            }
            if (_phase == ChatPhase.ToolBatchRunning)
            {
                PumpToolBatch();
                return;
            }
            if (_phase == ChatPhase.Done)
            {
                CloseRound();
            }
        }

        /// <summary>
        /// 启动新轮次——追加用户消息 + chat_state=working + StartLlm 动作段（构造消息序列 → 后台流式消费）。
        /// </summary>
        /// <param name="content">用户消息</param>
        /// <param name="source">来源——user/system</param>
        private void StartRound(string content, string source)
        {
            // E3 Token 统计——整轮清零（工具续轮 LaunchLlm 不清——跨轮累加语义）
            _usagePrompt = 0;
            _usageCompletion = 0;
            _usageCacheHit = 0;
            // roundsum 统计——单次前文/工具计数/四态计时清零 + 轮次起表 + 进入 link 相位
            _contextTokens = 0;
            _toolCallCount = 0;
            // 空回复续传计数——整轮清零（同 CH2 [段2.3] 每轮独立语义）
            _emptyReplyRetry = 0;
            _streamClosedRetry = false;
            _roundStartTick = System.Diagnostics.Stopwatch.GetTimestamp();
            _phaseAccumMs = new long[4];
            _phaseKind = -1;
            _phaseStartTick = 0;
            PhaseEnter(PhaseLink);
            _context.AddUserMessage(content);
            _viewStore.OnUserMessage(LastMessage(), ViewTimestamp(), _context.GetMessageCount() - 1);
            // 单向数据流改造——所有进内核的消息统一出口：SSE user 事件（前端只画不判）
            if (_httpHost != null)
            {
                string userJson = "{\"content\":" + JsonSerializer.Serialize(content) + ",\"source\":\"" + source + "\"}";
                _httpHost.PushView("user", userJson, -1, 0);
            }
            DataBox.Set<string>("global", "chat_state", "working");
            LogStore.Add("CatHome4", 1, "「" + _displayName + "」开始处理消息（来源 " + source + "）", "CHAT");
            LaunchLlm();
        }

        /// <summary>
        /// StartLlm 动作段——消息序列固化 + 槽清零 + 后台 Task.Run 消费（主线程零阻塞；LlmRunning 相位帧计数归零）。
        /// </summary>
        private void LaunchLlm()
        {
            // P6 中止——每轮新取消令牌（上一轮取消不跨轮；Pause 时 Cancel 后台流）
            _pauseCts = new System.Threading.CancellationTokenSource();
            LlmMessage[] messages = _context.GetMessages();
            _llmBusy = true;
            _llmResultText = "";
            _llmReasoning = "";
            _llmToolCallsJson = "";
            _llmError = false;
            _llmErrorText = "";
            System.Threading.Tasks.Task.Run(delegate
            {
_ = ConsumeLlmStream(messages, _pauseCts.Token);
            });
            _phase = ChatPhase.LlmRunning;
            _phaseFrames = 0;
        }

        /// <summary>
        /// 空回复续传——流成功结束但无回复文本（STREAM_CLOSED 或 content 空）时同上下文重发（CH2 [段2.3] 移植；无上限——LLM 兜底）。
        /// 复用 S2 retry 视图机制：置位 _sawRetry → 续传流首个 Text/Reasoning 到达时前端自动回填 resolved。
        /// </summary>
        /// <param name="reason">续传原因——日志 + retry 视图展示</param>
        private void RetryEmptyReply(string reason)
        {
            _emptyReplyRetry = _emptyReplyRetry + 1;
            _streamClosedRetry = false;
            LogStore.Add("LLM", 2, "空回复续传 第" + _emptyReplyRetry.ToString() + "/∞ 次（" + reason + "）——同上下文重发", "LLM");
            if (_httpHost != null)
            {
                string retryView = "{\"state\":\"retrying\",\"attempt\":\"" + _emptyReplyRetry.ToString() + "\",\"max\":\"" + "∞" + "\",\"text\":" + JsonSerializer.Serialize(reason) + "}";
                _retrySeq = _httpHost.PushView("retry", retryView, -1, _retrySeq);
                _sawRetry = true;
            }
            // 同上下文重发——LaunchLlm 内部清槽 + 帧计数归零（上下文未污染：空文本未入 Ctx/无错误文本入 Ctx）
            LaunchLlm();
        }

        /// <summary>
        /// 后台消费 LLM 流——写会话槽 + SSE 转发 + 结算行。Task.Run 执行——异常全兜底（错误可见性）。
        /// </summary>
        /// <param name="messages">消息序列</param>
        private async System.Threading.Tasks.Task ConsumeLlmStream(LlmMessage[] messages, System.Threading.CancellationToken ct)
        {
            try
            {
                // [段1] 槽预置 + 后台流式消费
                StringBuilder text = new StringBuilder();
                StringBuilder reasoning = new StringBuilder();
                string toolCalls = "";
                await foreach (LlmStreamEvent ev in _llmRuntime.ChatStream(messages, _tools, _id, ct))
                {
                    if (ev.Kind == LlmStreamKind.Text)
                    {
                        // S2 §8.4——重试成功回填：本轮发生过重试且首个 Text 到达 → retry 气泡更新为 resolved（只回填一次）
                        if (_sawRetry)
                        {
                            _sawRetry = false;
                            if (_httpHost != null)
                            {
                                string resolvedJson = "{\"state\":\"resolved\",\"text\":\"已恢复\"}";
                                _httpHost.PushView("retry", resolvedJson, _retrySeq, 0);
                            }
                        }
                        text.Append(ev.Text);
                        // roundsum 四态计时——首个 Text 进入回复相位（link/think 结算）
                        PhaseEnter(PhaseReply);
                        // P6 外观层转发——LLM 增量实时推送 SSE（协议 §4.2 llm 事件）
                        if (_httpHost != null)
                        {
                            string streamTextJson = "{\"kind\":\"text\",\"text\":" + JsonSerializer.Serialize(ev.Text) + "}";
                            _textStreamSeq = _httpHost.PushView("stream", streamTextJson, -1, _textStreamSeq);
                        }
                    }
                    else if (ev.Kind == LlmStreamKind.Reasoning)
                    {
                        // S2 §8.4——重试成功回填（Reasoning 也是恢复信号）
                        if (_sawRetry)
                        {
                            _sawRetry = false;
                            if (_httpHost != null)
                            {
                                string resolvedJson = "{\"state\":\"resolved\",\"text\":\"已恢复\"}";
                                _httpHost.PushView("retry", resolvedJson, _retrySeq, 0);
                            }
                        }
                        reasoning.Append(ev.Text);
                        // roundsum 四态计时——首个 Reasoning 进入思考相位（link 结算）
                        PhaseEnter(PhaseThink);
                        if (_httpHost != null)
                        {
                            string streamReasonJson = "{\"kind\":\"reasoning\",\"text\":" + JsonSerializer.Serialize(ev.Text) + "}";
                            _reasonStreamSeq = _httpHost.PushView("stream", streamReasonJson, -1, _reasonStreamSeq);
                        }
                    }
                    else if (ev.Kind == LlmStreamKind.ToolCalls)
                    {
                        // S2 §8.4——重试成功回填（ToolCalls 也是恢复信号——工具轮场景无文本）
                        if (_sawRetry)
                        {
                            _sawRetry = false;
                            if (_httpHost != null)
                            {
                                string resolvedJson = "{\"state\":\"resolved\",\"text\":\"已恢复\"}";
                                _httpHost.PushView("retry", resolvedJson, _retrySeq, 0);
                            }
                        }
                        toolCalls = ev.Text;
                        // roundsum 四态计时——ToolCalls 到达进入工具相位（link/think 结算；工具批执行）
                        PhaseEnter(PhaseTool);
                        if (_httpHost != null)
                        {
                            // F4 视图——toolCalls 占位卡后置 F1/F2（工具卡以结果整块出现，不推占位）
                        }
                    }
                    else if (ev.Kind == LlmStreamKind.Retrying)
                    {
                        // P6 中止——暂停中忽略重试事件（取消被误判为传输错误的防御：不推 retry 气泡——根治在 Runtime catch OCE 冒泡）
                        if (_pauseRequested)
                        {
                            continue;
                        }
                        // S2 §8.4——重试可见性：独立视图条目（retry renderType）——不入会话槽/上下文/日志（Runtime 已记 L2）
                        _sawRetry = true;
                        if (_httpHost != null)
                        {
                            // RETRY|N/3|原因摘要 —— 提取尝试序号与原因
                            string retryText = ev.Text;
                            string[] parts = retryText.Split('|');
                            string attempt = "";
                            string max = "";
                            string reason = retryText;
                            if (parts.Length >= 3)
                            {
                                attempt = parts[1];
                                string[] am = parts[1].Split('/');
                                if (am.Length >= 2)
                                {
                                    attempt = am[0];
                                    max = am[1];
                                }
                                reason = parts[2];
                            }
                            string retryView = "{\"state\":\"retrying\",\"attempt\":\"" + attempt + "\",\"max\":\"" + max + "\",\"text\":" + JsonSerializer.Serialize(reason) + "}";
                            _retrySeq = _httpHost.PushView("retry", retryView, -1, _retrySeq);
                        }
                    }
                    else if (ev.Kind == LlmStreamKind.Usage)
                    {
                        // E3 Token 统计——解析 usage JSON 累计整轮（工具多轮累加）+ 单次前文覆盖 + 转发 SSE（前端覆盖式显示累计值）
                        ParseUsage(ev.Text, ref _usagePrompt, ref _usageCompletion, ref _usageCacheHit, ref _contextTokens);
                        if (_httpHost != null)
                        {
                            string usageJson = "{\"prompt\":" + _usagePrompt.ToString()
                                + ",\"completion\":" + _usageCompletion.ToString()
                                + ",\"cacheHit\":" + _usageCacheHit.ToString() + "}";
                            string usageCtrl = "{\"type\":\"usage\",\"data\":" + usageJson + "}";
                            _httpHost.PushView("control", usageCtrl, -1, 0);
                        }
                    }
                    else if (ev.Kind == LlmStreamKind.Done)
                    {
                        if (_httpHost != null)
                        {
                            // F4 视图——done 由整块 replace 表达（流式结束不单独推事件）
                        }
                    }
                    else if (ev.Kind == LlmStreamKind.Error)
                    {
                        // P6 中止——暂停中忽略错误事件（取消引发的 TaskCanceled 转 Error 不推前端/不置错误）
                        if (_pauseRequested)
                        {
                            continue;
                        }
                        _llmError = true;
                        _llmErrorText = ev.Text;
                        // 空回复续传——STREAM_CLOSED（SSE 流未以 [DONE] 结束）：置位续传标志（PumpLlm 走续传），不推前端 error（无上限——LLM 兜底）
                        if (ev.Text.StartsWith("ERR|STREAM_CLOSED", StringComparison.Ordinal))

                        {
                            _streamClosedRetry = true;
                        }
                        else if (_httpHost != null)
                        {
                            string errCtrl = "{\"type\":\"error\",\"text\":" + JsonSerializer.Serialize(ev.Text) + "}";
                            _httpHost.PushView("control", errCtrl, -1, 0);
                        }
                    }
                }
                // [段2] 结果槽落位
                _llmResultText = text.ToString();
                _llmReasoning = reasoning.ToString();
                _llmToolCallsJson = toolCalls;
                // L1-META 结算行（D5 分级——观测全链：LLM 流完成一行为准，SSE log 事件实时可见）
                string llmSummary = "模型已回复（" + text.Length.ToString() + " 字符）";
                if (toolCalls.Length > 0)
                {
                    llmSummary = llmSummary + "，本轮调用了工具";
                }
                else
                {
                    llmSummary = llmSummary + "，未调用工具";
                }
                // E3 Token 统计——结算行带 usage（CLI/日志可观测；SSE 推送仍走 usage 事件——前端覆盖式显示累计值）
                llmSummary = llmSummary + "。Token 统计：输入 " + _usagePrompt.ToString() + "（含缓存 " + _usageCacheHit.ToString() + "）· 输出 " + _usageCompletion.ToString();
                LogStore.Add("LLM", 0, llmSummary, "LLM");
            }
            catch (System.OperationCanceledException)
            {
                // P6 中止——用户暂停取消流：不置错误（PumpLlm 检查 _pauseRequested 走收尾）
            }
            catch (Exception ex)
            {
                // P6 中止——暂停中异常忽略（取消竞态窗口——取消标志未及读取时异常已抛）
                if (!_pauseRequested)
                {
                    _llmError = true;
                    _llmErrorText = "ERR|" + ex.GetType().Name + "|" + ex.Message;
                }
            }
            finally
            {
                _llmBusy = false;
            }
        }

        /// <summary>
        /// 计时相位切换——结算旧相位累计毫秒 + 进入新相位（roundsum 四态计时；同相位跳过）。
        /// </summary>
        /// <param name="kind">目标相位（PhaseLink/PhaseThink/PhaseTool/PhaseReply）</param>
        private void PhaseEnter(int kind)
        {
            long now = System.Diagnostics.Stopwatch.GetTimestamp();
            if (_phaseKind >= 0 && _phaseStartTick > 0 && _phaseKind != kind)
            {
                long ms = (now - _phaseStartTick) * 1000 / System.Diagnostics.Stopwatch.Frequency;
                if (ms < 0) { ms = 0; }
                _phaseAccumMs[_phaseKind] = _phaseAccumMs[_phaseKind] + ms;
            }
            _phaseKind = kind;
            _phaseStartTick = now;
        }

        /// <summary>
        /// 结算当前相位——roundsum 生成前调用（CloseRound 末尾相位累计归零相位标记）。
        /// </summary>
        private void PhaseSettle()
        {
            if (_phaseKind >= 0 && _phaseStartTick > 0)
            {
                long now = System.Diagnostics.Stopwatch.GetTimestamp();
                long ms = (now - _phaseStartTick) * 1000 / System.Diagnostics.Stopwatch.Frequency;
                if (ms < 0) { ms = 0; }
                _phaseAccumMs[_phaseKind] = _phaseAccumMs[_phaseKind] + ms;
                _phaseKind = -1;
                _phaseStartTick = 0;
            }
        }

        /// <summary>
        /// 构建 roundsum 载荷——本轮 Token 消耗 + 工具次数 + 总耗时 + 四态用时（CloseRound 推送/落盘数据源）。
        /// </summary>
        /// <returns>roundsum 视图载荷 JSON（{"type":"roundsum","data":{...}}）</returns>
        private string BuildRoundSumJson()
        {
            long miss = _usagePrompt - _usageCacheHit;
            if (miss < 0) { miss = 0; }
            long elapsedMs = 0;
            if (_roundStartTick > 0)
            {
                elapsedMs = (System.Diagnostics.Stopwatch.GetTimestamp() - _roundStartTick) * 1000 / System.Diagnostics.Stopwatch.Frequency;
                if (elapsedMs < 0) { elapsedMs = 0; }
            }
            return "{\"type\":\"roundsum\",\"data\":{\"prompt\":" + _usagePrompt.ToString()
                + ",\"completion\":" + _usageCompletion.ToString()
                + ",\"cacheHit\":" + _usageCacheHit.ToString()
                + ",\"miss\":" + miss.ToString()
                + ",\"toolCount\":" + _toolCallCount.ToString()
                + ",\"elapsedMs\":" + elapsedMs.ToString()
                + ",\"phases\":{\"link\":" + _phaseAccumMs[PhaseLink].ToString()
                + ",\"think\":" + _phaseAccumMs[PhaseThink].ToString()
                + ",\"tool\":" + _phaseAccumMs[PhaseTool].ToString()
                + ",\"reply\":" + _phaseAccumMs[PhaseReply].ToString() + "}}}";
        }

        /// <summary>
        /// 解析 usage JSON——prompt/completion/cacheHit 累加到整轮计数（CH2 语义：多工具轮累加）；singlePrompt 覆盖式为单次请求 prompt（前文长度）。
        /// </summary>
        /// <param name="usageJson">usage JSON 字符串（{"prompt":N,"completion":N,"cacheHit":N}）</param>
        /// <param name="prompt">prompt 累计引用</param>
        /// <param name="completion">completion 累计引用</param>
        /// <param name="cacheHit">cacheHit 累计引用</param>
        /// <param name="singlePrompt">单次 prompt 覆盖引用——最近一次请求的前文长度（非累计）</param>
        private static void ParseUsage(string usageJson, ref long prompt, ref long completion, ref long cacheHit, ref long singlePrompt)
        {
            if (usageJson == null || usageJson.Length == 0)
            {
                return;
            }
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(usageJson))
                {
                    JsonElement root = doc.RootElement;
                    if (root.TryGetProperty("prompt", out JsonElement p) && p.ValueKind == JsonValueKind.Number)
                    {
                        prompt = prompt + p.GetInt64();
                        singlePrompt = p.GetInt64();
                    }
                    if (root.TryGetProperty("completion", out JsonElement c) && c.ValueKind == JsonValueKind.Number)
                    {
                        completion = completion + c.GetInt64();
                    }
                    if (root.TryGetProperty("cacheHit", out JsonElement h) && h.ValueKind == JsonValueKind.Number)
                    {
                        cacheHit = cacheHit + h.GetInt64();
                    }
                }
            }
            catch
            {
                // 解析失败静默——观测面不受单帧畸形影响
            }
        }

        /// <summary>
        /// LlmRunning 相位推进——轮询完成槽；完成 → LlmDone 动作段三分支（错误/纯文本/工具）；帧超限 → 超时错误路径（同 WaitForLlm 语义）。
        /// </summary>
        private void PumpLlm()
        {
            _phaseFrames = _phaseFrames + 1;
            if (_llmBusy)
            {
                if (_phaseFrames < LlmTimeoutFrames)
                {
                    return;
                }
                // 超时兜底——同 WaitForLlm 超时语义（后台 Task 完成后槽再写为等价现状竞态——终态已出）
                _llmBusy = false;
                _llmError = true;
                _llmErrorText = "ERR|LLM_TIMEOUT|LLM 调用超时（帧上限 " + LlmTimeoutFrames.ToString() + "）";
            }
            // LlmDone 动作段——S2 修复：错误文本不入上下文（就地修复——断点干净，不污染 ChatContext）
            // 错误可见性：前端 error 控制事件（ConsumeLlmStream 已推）+ LogStore L3；上下文保持断点（用户消息 + 已完成 turn 保留）
            if (_llmError)
            {
                // 空回复续传——STREAM_CLOSED → 同上下文重发（无上限——LLM 兜底；StreamClosedRetry 标志由 ConsumeLlmStream 置位）
                if (_streamClosedRetry)
                {
                    RetryEmptyReply("SSE 流中断（未以 [DONE] 结束）");
                    return;
                }
                _textStreamSeq = 0;
                _reasonStreamSeq = 0;
                LogStore.Add("LLM", 3, "LLM 错误（本轮中止——上下文保持断点）: " + TrimDisplay(_llmErrorText, 300), "LLM");
                _phase = ChatPhase.Done;
                return;
            }
            if (_llmToolCallsJson.Length == 0)
            {
                // 空回复续传——流正常结束但无回复文本（有思考无回复或完全空）→ 同上下文重发（CH2 [段2.3] 移植；无上限——LLM 兜底；Trim 判空）
                if (string.IsNullOrWhiteSpace(_llmResultText))
                {
                    RetryEmptyReply("模型未生成回复文本");
                    return;
                }
                // 纯文本回复——本轮完成
                _context.AddAssistantMessage(_llmResultText);
                _viewStore.OnAssistantText(LastMessage(), ViewTimestamp(), _context.GetMessageCount() - 1);
                if (_httpHost != null)
                {
                    string textJson = "{\"content\":" + JsonSerializer.Serialize(_llmResultText) + ",\"msgIndex\":" + (_context.GetMessageCount() - 1).ToString() + "}";
                    _httpHost.PushView("text", textJson, _textStreamSeq, 0);
                }
                _textStreamSeq = 0;
                _reasonStreamSeq = 0;
                LogStore.Add("CatHome4", 1, "回复(" + _displayName + "): " + _llmResultText, "CHAT", "", "", 200);
                // 单向数据流改造——忙时插话：本轮结束有排队消息 → 插入 Ctx + user 事件 + 直接开新轮（跳过 Done/CloseRound）
                if (_pending.Count > 0)
                {
                    PendingMessage next = _pending.Dequeue();
                    _context.AddUserMessage(next.Content);
                    _viewStore.OnUserMessage(LastMessage(), ViewTimestamp(), _context.GetMessageCount() - 1);
                    if (_httpHost != null)
                    {
                        string userJson = "{\"content\":" + JsonSerializer.Serialize(next.Content) + ",\"source\":\"" + next.Source + "\"}";
                        _httpHost.PushView("user", userJson, -1, 0);
                    }
                    _round = 0;
                    LogStore.Add("CatHome4", 1, "已插入排队消息（来源 " + next.Source + "），本轮结束后直接续轮", "CHAT");
                    LaunchLlm();
                    return;
                }
                _phase = ChatPhase.Done;
                return;
            }
            // StartToolBatch 动作段——assistant tool_calls 入上下文 + chat_state=tools + 发单
            _context.AddAssistantToolCalls(_llmToolCallsJson, _llmReasoning);
            _viewStore.OnAssistantToolCalls(LastMessage(), ViewTimestamp(), _context.GetMessageCount() - 1);
            if (_httpHost != null && _llmReasoning.Length > 0)
            {
                string reasonJson = "{\"content\":" + JsonSerializer.Serialize(_llmReasoning) + "}";
                _httpHost.PushView("reason", reasonJson, _reasonStreamSeq, 0);
            }
            // 工具轮 seal——流式文本容器整块替换（对齐纯文本轮 seal 语义；空文本不推——前端不建空气泡）
            if (_httpHost != null && _llmResultText.Length > 0)
            {
                string sealTextJson = "{\"content\":" + JsonSerializer.Serialize(_llmResultText) + ",\"msgIndex\":-1}";
                _httpHost.PushView("text", sealTextJson, _textStreamSeq, 0);
            }
            _reasonStreamSeq = 0;
            _textStreamSeq = 0;
            DataBox.Set<string>("global", "chat_state", "tools");
            LogStore.Add("CatHome4", 1, "模型请求调用工具（第 " + (_round + 1).ToString() + "/" + MaxToolRounds.ToString() + " 轮）：" + SummarizeToolNames(_llmToolCallsJson), "CHAT");
            EnterToolBatch(_llmToolCallsJson);
        }
        /// <summary>
        /// StartToolBatch 动作段——解析 tool_calls → OA 发单（host-* 延迟直执登记 / 普通工单 Post / Post 失败诚实 ERR）→ ToolBatchRunning。解析失败 = 空批（allDone 立即成立——等价原 try-catch 跳过语义：续轮保持）。
        /// </summary>
        /// <param name="toolCallsJson">tool_calls JSON 数组</param>
        private void EnterToolBatch(string toolCallsJson)
        {
            _toolBatchActive = true;
            _dogs.Clear();
            _hostDogs.Clear();
            JsonDocument doc = null;
            try
            {
                doc = JsonDocument.Parse(toolCallsJson);
            }
            catch (Exception ex)
            {
                LogStore.Add("CatHome4", 3, "tool_calls 解析失败: " + ex.Message, "TOOL");
            }
            if (doc != null)
            {
                using (doc)
                {
                    JsonElement root = doc.RootElement;
                    for (int i = 0; i < root.GetArrayLength(); i = i + 1)
                    {
                        JsonElement call = root[i];
                        string id = GetStringProp(call, "id");
                        // OpenAI wire：name/arguments 在 function 嵌套对象内
                        string name = "";
                        string arguments = "";
                        JsonElement funcEl;
                        if (call.TryGetProperty("function", out funcEl))
                        {
                            name = GetStringProp(funcEl, "name");
                            arguments = GetStringProp(funcEl, "arguments");
                        }
                        // M2c 拦截——声明面外工具直接拒绝（ERR 回执不进 OA；host-* 同拦；拦截即时生效）
                        if (!IsToolAllowed(name))
                        {
                            ToolOrderDog forbiddenDog = new ToolOrderDog(id, name, arguments);
                            forbiddenDog.Result = "ERR|TOOL_FORBIDDEN|工具不在本会话声明面: " + name;
                            forbiddenDog.IsClosed = true;
                            _dogs.Add(forbiddenDog);
                            LogStore.Add("CatHome4", 2, "工具 " + name + " 被拒绝：不在本会话声明面", "TOOL");
                            continue;
                        }
                        // P9.4 per-cat 路由——载荷注入会话 ID（config.bridge 按 catId 路由每猫 ConfigStore；其他工具忽略多余字段）
                        arguments = InjectCatId(arguments);
                        // roundsum 工具计数——合法工具调用 +1（被拒工具不计）
                        _toolCallCount = _toolCallCount + 1;
                        LogStore.Add("CatHome4", 1, "工具 " + name + " 开始执行：" + SummarizeToolArgs(arguments), "TOOL");
                        ToolOrderDog dog = new ToolOrderDog(id, name, arguments);
                        if (name.StartsWith("host-", StringComparison.Ordinal))
                        {
                            // host-* 延迟直执登记——批次末尾执行（顺序保证：同批 mau-proj 等先完成产物落地）
                            _hostDogs.Add(dog);
                            dog.IsClosed = true;
                            LogStore.Add("CatHome4", 1, "工具 " + name + " 登记延迟直执（批次末尾执行）", "TOOL");
                        }
                        else if (name == "Note" || name == "time" || name == "random" || name == "info")
                        {
                            // 内置工具会话内直执——不需 OA（R0.2 分层：Note 状态在会话实例；time/random/info 宿主直执）
                            dog.Result = ExecuteBuiltin(name, arguments);
                            dog.IsClosed = true;
                            LogStore.Add("CatHome4", 1, "工具 " + name + " 会话内直接执行（内置）", "TOOL");
                        }
                        else
                        {
                            dog.Post(_oa, ToolOwnerId);
                            if (dog.OfficeId == 0)
                            {
                                // Post 失败——诚实失败（直执面已移除 2026-09-04——OA 不可用即 ERR，不静默降级）
                                dog.Result = "ERR|OA_POST_FAIL|工单提交失败（OA 不可用）: " + name;
                                dog.IsClosed = true;
                                LogStore.Add("CatHome4", 2, "工具 " + name + " 工单提交失败（OA 不可用）——诚实 ERR", "TOOL");
                            }
                            else
                            {
                                LogStore.Add("CatHome4", 1, "工具 " + name + " 已提交工单 #" + dog.OfficeId + "（超时上限 " + dog.TimeoutFrames + " 帧）", "TOOL");
                            }
                        }
                        _dogs.Add(dog);
                    }
                }
            }
            _phase = ChatPhase.ToolBatchRunning;
            _phaseFrames = 0;
        }

        /// <summary>
        /// 工具声明面比对——线性扫描本会话 _tools（M2c 拦截：名单外直接拒绝；21 件量级线性够用）。
        /// </summary>
        /// <param name="name">工具名</param>
        /// <returns>true=在声明面内</returns>
        private bool IsToolAllowed(string name)
        {
            for (int i = 0; i < _tools.Length; i++)
            {
                if (string.Equals(_tools[i].Name, name, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 载荷注入会话 ID——arguments JSON 合并 catId 字段（P9.4 per-cat 配置路由；非对象/解析失败原样透传）。
        /// </summary>
        /// <param name="arguments">LLM 原始参数 JSON</param>
        /// <returns>注入后 JSON</returns>
        private string InjectCatId(string arguments)
        {
            if (arguments == null || arguments.Length == 0)
            {
                return arguments;
            }
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(arguments))
                {
                    if (doc.RootElement.ValueKind != JsonValueKind.Object)
                    {
                        return arguments;
                    }
                    Dictionary<string, object> map = new Dictionary<string, object>();
                    foreach (JsonProperty p in doc.RootElement.EnumerateObject())
                    {
                        map[p.Name] = p.Value.Clone();
                    }
                    map["catId"] = _id;
                    return JsonSerializer.Serialize(map);
                }
            }
            catch (Exception)
            {
                // 参数 JSON 损坏——原样透传（下游 BAD_ARGS 校验可见拒绝）
                return arguments;
            }
        }
        /// <summary>
        /// ToolBatchRunning 相位推进——逐 Dog 帧轮询；allDone 或帧超限 → ToolBatchDone 动作段（host-* 延迟直执 → 收集回执 → 续轮/收敛）。
        /// </summary>
        private void PumpToolBatch()
        {
            _phaseFrames = _phaseFrames + 1;
            bool allDone = true;
            for (int i = 0; i < _dogs.Count; i = i + 1)
            {
                _dogs[i].Tick(_oa);
                if (!_dogs[i].IsClosed && !_dogs[i].IsTimedOut)
                {
                    allDone = false;
                }
            }
            if (!allDone && _phaseFrames < ToolBatchWaitFrames)
            {
                return;
            }
            // [段2b] host-* 延迟直执——批次其他工具完成后宿主直执（忙时豁免：主线程串行——宿主 ExecuteReload 事务三段式兜底）
            for (int h = 0; h < _hostDogs.Count; h = h + 1)
            {
                ToolOrderDog dog = _hostDogs[h];
                bool savedBusy = _toolBatchActive;
                _toolBatchActive = false;
                string hr = RunTool(dog.Name, dog.ArgsJson);
                _toolBatchActive = savedBusy;
                if (hr == null || hr.Length == 0)
                {
                    hr = "ERR|EMPTY_RESULT|工具执行无结果";
                }
                dog.Result = hr;
                LogStore.Add("CatHome4", 1, "工具 " + dog.Name + " 延迟直执完成：" + TrimDisplay(hr, 100), "TOOL");
            }
            // [段3] 收集——Closed 取回执；TimeOut/帧超限 → 诚实 ERR（OA 链路失败即报错，不直执）
            for (int i = 0; i < _dogs.Count; i = i + 1)
            {
                ToolOrderDog dog = _dogs[i];
                if (!dog.IsClosed && !dog.IsTimedOut)
                {
                    dog.IsTimedOut = true;
                }
                if (dog.IsTimedOut)
                {
                    LogStore.Add("CatHome4", 2, "工具 " + dog.Name + " 工单 #" + dog.OfficeId + " 超时（无人认领）——诚实 ERR", "TOOL");
                    dog.Result = "ERR|OA_TIMEOUT|工单超时无人认领: " + dog.Name;
                    dog.IsClosed = true;
                }
                if (dog.Result == null || dog.Result.Length == 0)
                {
                    dog.Result = "ERR|EMPTY_RESULT|工具执行无结果";
                }
                // O 系列：工具结果入 Log 截断 100 字符——完整结果在会话消息
                LogStore.Add("CatHome4", 1, "工具 " + dog.Name + " 执行完成：" + dog.Result, "TOOL", "", "", 100);
                // B4 对话区：工具结果实时推送 SSE（tool 事件——前端按执行序填充占位卡；参数/结果视图截断同 history）
                if (_httpHost != null)
                {
                    string toolSummary = ToolSummaryFormatter.Build(dog.Name, dog.ArgsJson, dog.Result);
                    string toolJson = "{\"name\":" + JsonSerializer.Serialize(dog.Name) + ",\"arguments\":" + JsonSerializer.Serialize(TruncateText(dog.ArgsJson, 200)) + ",\"result\":" + JsonSerializer.Serialize(TruncateText(dog.Result, 300)) + ",\"summary\":" + JsonSerializer.Serialize(toolSummary) + ",\"toolIndex\":" + (i + 1).ToString() + ",\"toolTotal\":" + _dogs.Count.ToString() + "}";
                _httpHost.PushView("toolcard", toolJson, -1, 0);
                }
                _context.AddToolResult(dog.ToolCallId, dog.Name, dog.Result);
                _viewStore.OnToolResult(LastMessage(), ViewTimestamp(), _context.GetMessageCount() - 1);
            }
            _toolBatchActive = false;
            // 单向数据流改造——忙时插话：工具批完成有排队消息 → 插入 Ctx + user 事件 + 直接续轮（工具结果 + 插话同轮可见）
            if (_pending.Count > 0)
            {
                PendingMessage next = _pending.Dequeue();
                _context.AddUserMessage(next.Content);
                _viewStore.OnUserMessage(LastMessage(), ViewTimestamp(), _context.GetMessageCount() - 1);
                if (_httpHost != null)
                {
                    string userJson = "{\"content\":" + JsonSerializer.Serialize(next.Content) + ",\"source\":\"" + next.Source + "\"}";
                _httpHost.PushView("user", userJson, -1, 0);
                }
                _round = 0;
                LogStore.Add("CatHome4", 1, "已插入排队消息（来源 " + next.Source + "），工具批后直接续轮", "CHAT");
                LaunchLlm();
                return;
            }
            LogStore.Add("CatHome4", 1, "工具结果已回传，续轮", "CHAT");
            // 收敛判定——MaxToolRounds 轮满 → Done（收敛语义由 LLM 判断完成）
            if (_round + 1 >= MaxToolRounds)
            {
                _phase = ChatPhase.Done;
                return;
            }
            _round = _round + 1;
            // 续轮——StartLlm 动作段
            LaunchLlm();
        }
        /// <summary>
        /// Done 相位——前文落盘（tool 截断 ≤800——D7 落盘副本防膨胀）+ chat_state=idle + PushChatDone + 复位（原 HandleChat 段4）。
        /// </summary>
        private void CloseRound()
        {
            LlmMessage[] toSave = _context.GetMessages();
            for (int i = 0; i < toSave.Length; i = i + 1)
            {
                if (toSave[i].Role == LlmRole.Tool && toSave[i].Content != null && toSave[i].Content.Length > 800)
                {
                    toSave[i].Content = TruncateText(toSave[i].Content, 800);
                }
            }
            // E3 真实 usage 统计——CloseRound 落盘（info 自查/前端显示数据源；零估算）
            _lastStats.EntryCount = _context.GetMessageCount();
            _lastStats.LastPromptTokens = _usagePrompt;
            _lastStats.LastCacheHitTokens = _usageCacheHit;
            _lastStats.LastCompletionTokens = _usageCompletion;
            _lastStats.LastContextTokens = _contextTokens;
            _store.Save(toSave, _lastStats);
            // roundsum 轮末统计——相位结算 + 载荷构建 + 视图落盘 + SSE 推送（本轮 Token 消耗 + 工具次数 + 总耗时 + 四态用时）
            PhaseSettle();
            string roundsumJson = BuildRoundSumJson();
            _viewStore.AppendRoundSummary(roundsumJson, ViewTimestamp());
            _viewStore.Save();
            if (_httpHost != null)
            {
                _httpHost.PushView("roundsum", roundsumJson, -1, 0);
            }
            DataBox.Set<string>("global", "chat_state", "idle");
            // B4 对话区：会话终态事件——前端定型（llm done 仅一轮结束；chatdone 才是整次会话结束；count = 原始消息数——实时同步状态区）
            // E3 扩展——chatdone 带真实 usage（命中/非命中/输出/前文长度；前端状态栏同步显示）
            if (_httpHost != null)
            {
                string doneJson = "{\"type\":\"chatdone\",\"count\":" + _context.GetMessages().Length.ToString()
                    + ",\"stats\":{\"prompt\":" + _usagePrompt.ToString()
                    + ",\"cacheHit\":" + _usageCacheHit.ToString()
                    + ",\"completion\":" + _usageCompletion.ToString()
                    + ",\"context\":" + _contextTokens.ToString() + "}}";
                _httpHost.PushView("control", doneJson, -1, 0);
            }
            LogStore.Add("CatHome4", 1, "会话前文已落盘（" + _context.GetMessageCount().ToString() + " 条消息）", "SYS");
            // M4a Note 自动拉起——剩余 ≥2 条以 user 名义推下一轮；仅剩 1 条清空（防无限循环闸门——CH2 语义）
            if (_noteTasks != null && _noteTasks.Length > 0 && _noteCurrent < _noteTasks.Length - 1)
            {
                int remain = _noteTasks.Length - _noteCurrent;
                PostUserMessage("[Note 未完成] 剩余 " + remain + " 条\n当前任务：" + _noteTasks[_noteCurrent], "system");
                LogStore.Add("CatHome4", 1, "Note 自动拉起：剩余 " + remain + " 条任务", "CHAT");
            }
            else
            {
                _noteTasks = null;
                _noteCurrent = 0;
                _noteDone = 0;
                // M4c 前端面板——清空状态推送
                PushNoteState();
            }
            _round = 0;
            _phase = ChatPhase.Idle;
        }

        /// <summary>
        /// 回滚——从指定正式回复节点重新开始（P6b：裁剪唯一通道；该节点后消息全部丢弃）。
        /// 校验：仅 Idle；msgIndex 指向 assistant 正式回复（Content>0——工具声明轮天然排除）。执行：截断上下文 → 落盘 → 统计/视图/Note 复位 → 视图重建 → session_reset 推送。
        /// </summary>
        /// <param name="msgIndex">真实前文消息索引（指向 assistant 正式回复——前端 text 块 MsgIndex）</param>
        /// <returns>ERR| 前缀失败 / 成功摘要</returns>
        public string Rollback(int msgIndex)
        {
            if (_phase != ChatPhase.Idle)
            {
                return "ERR|ROLLBACK_BUSY|会话忙——回滚仅空闲时执行";
            }
            LlmMessage[] all = _context.GetMessages();
            if (msgIndex < 0 || msgIndex >= all.Length)
            {
                return "ERR|ROLLBACK_INDEX|节点索引越界: " + msgIndex.ToString();
            }
            LlmMessage target = all[msgIndex];
            if (target.Role != LlmRole.Assistant || target.Content == null || target.Content.Length == 0)
            {
                return "ERR|ROLLBACK_NODE|节点不是正式回复（仅 assistant 回复可作切点）";
            }
            // [段1] 截断上下文——保留 [0..msgIndex]（ReplaceMessages 安全面——格式修复幂等）
            LlmMessage[] keep = new LlmMessage[msgIndex + 1];
            for (int i = 0; i <= msgIndex; i = i + 1)
            {
                keep[i] = all[i];
            }
            _context.ReplaceMessages(keep);
            // [段2] 前文落盘——tool 结果截断 ≤800（复用 CloseRound 副本逻辑）
            LlmMessage[] toSave = _context.GetMessages();
            for (int i = 0; i < toSave.Length; i = i + 1)
            {
                if (toSave[i].Role == LlmRole.Tool && toSave[i].Content != null && toSave[i].Content.Length > 800)
                {
                    toSave[i].Content = TruncateText(toSave[i].Content, 800);
                }
            }
            _lastStats.EntryCount = toSave.Length;
            _store.Save(toSave, _lastStats);
            // [段3] 统计与运行期参数复位——新起点零统计起算
            ResetStats();
            _usagePrompt = 0;
            _usageCompletion = 0;
            _usageCacheHit = 0;
            _contextTokens = 0;
            _toolCallCount = 0;
            // [段4] 视图——从新前文完全重建 + roundsum 清空（roundsum 非真实前文派生；RebuildView 的 LoadInjectReport 会读回旧 view.json 统计——重建后清空并落盘，防下次启动读回）
            RebuildView();
            _viewStore.ClearRoundSums();
            _viewStore.Save();
            // [段5] Note 任务清空——防旧任务自动拉起新轮
            _noteTasks = null;
            _noteCurrent = 0;
            _noteDone = 0;
            PushNoteState();
            // [段6] 前端通知——session_reset（前端清空气泡重拉 history；渲染层零改动）
            PushSessionReset();
            DataBox.Set<string>("global", "chat_state", "idle");
            LogStore.Add("CatHome4", 1, "已回滚到节点 " + msgIndex.ToString() + "（保留 " + (msgIndex + 1).ToString() + " 条消息）", "CHAT");
            return "rollback | 已截断到节点 " + msgIndex.ToString() + "（保留 " + (msgIndex + 1).ToString() + " 条消息）";
        }

        /// <summary>
        /// 读取 JSON 对象字符串属性——防御式（缺字段返回空串）。
        /// </summary>
        /// <param name="obj">JSON 对象</param>
        /// <param name="prop">属性名</param>
        /// <returns>属性值</returns>
        private static string GetStringProp(JsonElement obj, string prop)
        {
            JsonElement value;
            if (obj.TryGetProperty(prop, out value) && value.ValueKind == JsonValueKind.String)
            {
                string got = value.GetString();
                if (got != null)
                {
                    return got;
                }
            }
            return "";
        }

        /// <summary>
        /// 截断显示文本——日志防刷屏（保留头部 + 截断提示）。
        /// </summary>
        /// <param name="text">原文</param>
        /// <param name="max">上限</param>
        /// <returns>截断文本</returns>
        private static string TrimDisplay(string text, int max)
        {
            if (text == null || text.Length <= max)
            {
                if (text == null)
                {
                    return "";
                }
                return text;
            }
            return text.Substring(0, max) + "...";
        }

        /// <summary>
        /// 工具调用摘要——解析 tool_calls JSON 数组，提取各调用工具名（日志自然语言化：不落裸 JSON）。
        /// </summary>
        /// <param name="toolCallsJson">tool_calls JSON 数组</param>
        /// <returns>工具名列表（逗号拼接）；解析失败回落截断原文</returns>
        private static string SummarizeToolNames(string toolCallsJson)
        {
            if (toolCallsJson == null || toolCallsJson.Length == 0)
            {
                return "空";
            }
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(toolCallsJson))
                {
                    JsonElement root = doc.RootElement;
                    if (root.ValueKind != JsonValueKind.Array)
                    {
                        return TrimDisplay(toolCallsJson, 120);
                    }
                    System.Text.StringBuilder sb = new System.Text.StringBuilder();
                    for (int i = 0; i < root.GetArrayLength(); i++)
                    {
                        JsonElement call = root[i];
                        string name = "";
                        JsonElement funcEl;
                        if (call.TryGetProperty("function", out funcEl))
                        {
                            name = GetStringProp(funcEl, "name");
                        }
                        if (name.Length == 0)
                        {
                            name = GetStringProp(call, "name");
                        }
                        if (sb.Length > 0)
                        {
                            sb.Append("、");
                        }
                        if (name.Length > 0)
                        {
                            sb.Append(name);
                        }
                        else
                        {
                            sb.Append("未知工具");
                        }
                    }
                    string result = sb.ToString();
                    if (result.Length > 0)
                    {
                        return result;
                    }
                }
            }
            catch (Exception)
            {
                // 解析失败——回落原文
            }
            return TrimDisplay(toolCallsJson, 120);
        }

        /// <summary>
        /// 工具参数摘要——解析 arguments JSON，提取 path/file/proj/key 等关键参数（日志自然语言化：不落裸 JSON）。
        /// </summary>
        /// <param name="arguments">工具参数 JSON</param>
        /// <returns>关键参数摘要；无关键参数时回落截断</returns>
        private static string SummarizeToolArgs(string arguments)
        {
            if (arguments == null || arguments.Length == 0)
            {
                return "";
            }
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(arguments))
                {
                    JsonElement root = doc.RootElement;
                    if (root.ValueKind != JsonValueKind.Object)
                    {
                        return TrimDisplay(arguments, 120);
                    }
                    string[] keys = new string[] { "path", "file", "proj", "key", "name", "dir", "id" };
                    System.Text.StringBuilder sb = new System.Text.StringBuilder();
                    for (int i = 0; i < keys.Length; i++)
                    {
                        JsonElement value;
                        if (root.TryGetProperty(keys[i], out value) && value.ValueKind == JsonValueKind.String)
                        {
                            string got = value.GetString();
                            if (got != null && got.Length > 0)
                            {
                                if (sb.Length > 0)
                                {
                                    sb.Append(", ");
                                }
                                sb.Append(keys[i]);
                                sb.Append("=");
                                sb.Append(TrimDisplay(got, 60));
                            }
                        }
                    }
                    if (sb.Length > 0)
                    {
                        return sb.ToString();
                    }
                }
            }
            catch (Exception)
            {
                // 解析失败——回落原文
            }
            return TrimDisplay(arguments, 120);
        }

        /// <summary>
        /// 文本截断——超长保留头部 + 截断提示（SSE 视图/落盘共用）。
        /// </summary>
        /// <param name="text">原文</param>
        /// <param name="max">上限字符数</param>
        /// <returns>截断文本</returns>
        private static string TruncateText(string text, int max)
        {
            if (text == null)
            {
                return "";
            }
            if (text.Length <= max)
            {
                return text;
            }
            return text.Substring(0, max) + "…[截断:原" + text.Length.ToString() + "字符]";
        }
    }
}