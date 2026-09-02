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

        /// <summary>Token 用量整轮累计——prompt（含 cache hit）</summary>
        private long _usagePrompt;

        /// <summary>Token 用量整轮累计——completion</summary>
        private long _usageCompletion;

        /// <summary>Token 用量整轮累计——cache hit（CH2 双格式：prompt_cache_hit_tokens / cached_tokens）</summary>
        private long _usageCacheHit;

        // [段3] 工具批
        /// <summary>工具批执行中——reload 拒绝检查面（任一会话 TRUE 即拒绝）</summary>
        private bool _toolBatchActive;
        /// <summary>工具单列表——普通工单（OA 认领 / FALLBACK 直执，含 host-* 延迟直执登记）</summary>
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

        /// <summary>工具直执回调——宿主 ExecuteTool（FALLBACK/延迟直执）</summary>
        private readonly Func<string, string, string> _executeTool;

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

        /// <summary>当前宿主帧号——F4 帧号全局盒（DataBox global.frame——数据面内聚；未写入回退 0）</summary>
        private long CurrentFrame()
        {
            long frame;
            if (DataBox.TryGet<long>("global", "frame", out frame))
            {
                return frame;
            }
            return 0;
        }

        /// <summary>重建视图层——从真实前文完全重置（启动恢复后调用；真实前文绝对可用）</summary>
        public void RebuildView()
        {
            _viewStore.Rebuild(_context.GetMessages(), CurrentFrame());
        }

        /// <summary>清空视图层——session.new 清前文时同步（视图随生命周期清理）</summary>
        public void ClearView()
        {
            _viewStore.Clear();
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
            _context.AddUserMessage(content);
            _viewStore.OnUserMessage(LastMessage(), CurrentFrame());
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
            LlmMessage[] messages = _context.GetMessages();
            _llmBusy = true;
            _llmResultText = "";
            _llmReasoning = "";
            _llmToolCallsJson = "";
            _llmError = false;
            _llmErrorText = "";
            System.Threading.Tasks.Task.Run(delegate
            {
_ = ConsumeLlmStream(messages);
            });
            _phase = ChatPhase.LlmRunning;
            _phaseFrames = 0;
        }

        /// <summary>
        /// 后台消费 LLM 流——写会话槽 + SSE 转发 + 结算行。Task.Run 执行——异常全兜底（错误可见性）。
        /// </summary>
        /// <param name="messages">消息序列</param>
        private async System.Threading.Tasks.Task ConsumeLlmStream(LlmMessage[] messages)
        {
            try
            {
                // [段1] 槽预置 + 后台流式消费
                StringBuilder text = new StringBuilder();
                StringBuilder reasoning = new StringBuilder();
                string toolCalls = "";
                await foreach (LlmStreamEvent ev in _llmRuntime.ChatStream(messages, _tools, _id))
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
                        if (_httpHost != null)
                        {
                            // F4 视图——toolCalls 占位卡后置 F1/F2（工具卡以结果整块出现，不推占位）
                        }
                    }
                    else if (ev.Kind == LlmStreamKind.Retrying)
                    {
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
                        // E3 Token 统计——解析 usage JSON 累计整轮（工具多轮累加）+ 转发 SSE（前端覆盖式显示累计值）
                        ParseUsage(ev.Text, ref _usagePrompt, ref _usageCompletion, ref _usageCacheHit);
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
                        _llmError = true;
                        _llmErrorText = ev.Text;
                        if (_httpHost != null)
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
            catch (Exception ex)
            {
                _llmError = true;
                _llmErrorText = "ERR|" + ex.GetType().Name + "|" + ex.Message;
            }
            finally
            {
                _llmBusy = false;
            }
        }

        /// <summary>
        /// 解析 usage JSON——prompt/completion/cacheHit 累加到整轮计数（CH2 语义：多工具轮累加）。
        /// </summary>
        /// <param name="usageJson">usage JSON 字符串（{"prompt":N,"completion":N,"cacheHit":N}）</param>
        /// <param name="prompt">prompt 累计引用</param>
        /// <param name="completion">completion 累计引用</param>
        /// <param name="cacheHit">cacheHit 累计引用</param>
        private static void ParseUsage(string usageJson, ref long prompt, ref long completion, ref long cacheHit)
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
                _textStreamSeq = 0;
                _reasonStreamSeq = 0;
                LogStore.Add("LLM", 3, "LLM 错误（本轮中止——上下文保持断点）: " + TrimDisplay(_llmErrorText, 300), "LLM");
                _phase = ChatPhase.Done;
                return;
            }
            if (_llmToolCallsJson.Length == 0)
            {
                // 纯文本回复——本轮完成
                _context.AddAssistantMessage(_llmResultText);
                _viewStore.OnAssistantText(LastMessage(), CurrentFrame());
                if (_httpHost != null)
                {
                    string textJson = "{\"content\":" + JsonSerializer.Serialize(_llmResultText) + "}";
                    _httpHost.PushView("text", textJson, _textStreamSeq, 0);
                }
                _textStreamSeq = 0;
                _reasonStreamSeq = 0;
                Console.WriteLine("[" + _displayName + "] " + _llmResultText);
                // 单向数据流改造——忙时插话：本轮结束有排队消息 → 插入 Ctx + user 事件 + 直接开新轮（跳过 Done/CloseRound）
                if (_pending.Count > 0)
                {
                    PendingMessage next = _pending.Dequeue();
                    _context.AddUserMessage(next.Content);
                    _viewStore.OnUserMessage(LastMessage(), CurrentFrame());
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
            _viewStore.OnAssistantToolCalls(LastMessage(), CurrentFrame());
            if (_httpHost != null && _llmReasoning.Length > 0)
            {
                string reasonJson = "{\"content\":" + JsonSerializer.Serialize(_llmReasoning) + "}";
                _httpHost.PushView("reason", reasonJson, _reasonStreamSeq, 0);
            }
            _reasonStreamSeq = 0;
            _textStreamSeq = 0;
            DataBox.Set<string>("global", "chat_state", "tools");
            LogStore.Add("CatHome4", 1, "模型请求调用工具（第 " + (_round + 1).ToString() + "/" + MaxToolRounds.ToString() + " 轮）：" + SummarizeToolNames(_llmToolCallsJson), "CHAT");
            EnterToolBatch(_llmToolCallsJson);
        }
        /// <summary>
        /// StartToolBatch 动作段——解析 tool_calls → OA 发单（host-* 延迟直执登记 / 普通工单 Post / Post 失败 FALLBACK）→ ToolBatchRunning。解析失败 = 空批（allDone 立即成立——等价原 try-catch 跳过语义：续轮保持）。
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
                        // M2c 拦截——声明面外工具直接拒绝（ERR 回执不进 OA 不 FALLBACK；host-* 同拦；拦截即时生效）
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
                                // Post 失败——直接 FALLBACK 直执（错误可见性）
                                string fb = _executeTool(name, arguments);
                                if (fb == null || fb.Length == 0)
                                {
                                    fb = "ERR|EMPTY_RESULT|工具执行无结果";
                                }
                                dog.Result = "[FALLBACK] " + fb;
                                dog.IsClosed = true;
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
                string hr = _executeTool(dog.Name, dog.ArgsJson);
                _toolBatchActive = savedBusy;
                if (hr == null || hr.Length == 0)
                {
                    hr = "ERR|EMPTY_RESULT|工具执行无结果";
                }
                dog.Result = hr;
                LogStore.Add("CatHome4", 1, "工具 " + dog.Name + " 延迟直执完成：" + TrimDisplay(hr, 100), "TOOL");
            }
            // [段3] 收集——Closed 取回执；TimeOut/帧超限 → FALLBACK 直执（执行器不变；[FALLBACK] 前缀注明）
            for (int i = 0; i < _dogs.Count; i = i + 1)
            {
                ToolOrderDog dog = _dogs[i];
                if (!dog.IsClosed && !dog.IsTimedOut)
                {
                    dog.IsTimedOut = true;
                }
                if (dog.IsTimedOut)
                {
                    LogStore.Add("CatHome4", 2, "工具 " + dog.Name + " 工单 #" + dog.OfficeId + " 超时，已转 FALLBACK 直执", "TOOL");
                    string result = _executeTool(dog.Name, dog.ArgsJson);
                    if (result == null || result.Length == 0)
                    {
                        result = "ERR|EMPTY_RESULT|工具执行无结果";
                    }
                    dog.Result = "[FALLBACK] " + result;
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
                    string toolJson = "{\"name\":" + JsonSerializer.Serialize(dog.Name) + ",\"arguments\":" + JsonSerializer.Serialize(TruncateText(dog.ArgsJson, 200)) + ",\"result\":" + JsonSerializer.Serialize(TruncateText(dog.Result, 300)) + "}";
                _httpHost.PushView("toolcard", toolJson, -1, 0);
                }
                _context.AddToolResult(dog.ToolCallId, dog.Name, dog.Result);
                _viewStore.OnToolResult(LastMessage(), CurrentFrame());
            }
            _toolBatchActive = false;
            // 单向数据流改造——忙时插话：工具批完成有排队消息 → 插入 Ctx + user 事件 + 直接续轮（工具结果 + 插话同轮可见）
            if (_pending.Count > 0)
            {
                PendingMessage next = _pending.Dequeue();
                _context.AddUserMessage(next.Content);
                _viewStore.OnUserMessage(LastMessage(), CurrentFrame());
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
            _store.Save(toSave);
            _viewStore.Save();
            DataBox.Set<string>("global", "chat_state", "idle");
            // B4 对话区：会话终态事件——前端定型（llm done 仅一轮结束；chatdone 才是整次会话结束；count = 原始消息数——实时同步状态区）
            if (_httpHost != null)
            {
                string doneJson = "{\"type\":\"chatdone\",\"count\":" + _context.GetMessages().Length.ToString() + "}";
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