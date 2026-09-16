using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Mau.Runtime;

namespace CatHome4.QQ
{
    /// <summary>
    /// QQ 管理器——附属功能组件 / 全局插件（R2.3.3-R2.3.4）。
    /// 扫描 Bot 池（CH_QqBotConfigStore）→ 每个注册 Bot 建立 WS 连接（注册即建，Cat 配置不影响连接存在）。
    /// 输入路由（R2.3.4）：消息到达 → 强匹配指令直执 / 查绑定 Cat → 广播注入所有绑定且启用的 Cat（[来自QQ] 前缀 + 来源入队）。
    /// 未绑定 → 返回「无猫」；绑定但未启用 → 返回「目标 Cat 未启用 qqbot 转发功能」。
    /// 注入来源队列（FIFO）——R2.3.5 输出转发消费（回复目标 = 触发来源）。
    /// </summary>
    internal static class QQBotService
    {
        /// <summary>Bot 连接集合——QqBotId → 连接实例</summary>
        private static readonly Dictionary<Guid, QQBotConnection> _connections = new Dictionary<Guid, QQBotConnection>();

        /// <summary>注入来源队列——猫 Key → QqSource FIFO（注入时入队，轮结束 roundsum 出队消费）</summary>
        private static readonly Dictionary<string, Queue<QqSource>> _sourceQueues = new Dictionary<string, Queue<QqSource>>();

        /// <summary>本轮即时转发计数——猫 Key → 已即时转发数（3+1 预算：≤3 即时，第 4 次轮末汇总）</summary>
        private static readonly Dictionary<string, int> _immediateCounts = new Dictionary<string, int>();

        /// <summary>累计内容——猫 Key → 超 3 条后累计的 text 块（轮末汇总第 4 次转发）</summary>
        private static readonly Dictionary<string, System.Text.StringBuilder> _accumulated = new Dictionary<string, System.Text.StringBuilder>();
        /// <summary>服务事件队列——WS 线程入队 / 主线程 Tick 事件泵消费（R6-P1-01 单线程化：三字典访问面收敛主线程）</summary>
        private static readonly System.Collections.Concurrent.ConcurrentQueue<QqServiceEvent> _eventQueue = new System.Collections.Concurrent.ConcurrentQueue<QqServiceEvent>();
        /// <summary>启动标志——防重复 Start</summary>
        private static bool _started = false;

        /// <summary>启动时刻——/info 运行时长统计</summary>
        private static DateTime _startTime = DateTime.Now;

        /// <summary>绑定目标收集面——入口壳注入（S3 解耦：不直接引用 Program 静态面）</summary>
        private static IQqTargetCollector _collector;

        /// <summary>文件缓存根——入口壳注入（P9 接收落盘；空=附件功能禁用）</summary>
        private static string _fileCacheRoot = "";

        /// <summary>
        /// 注入绑定目标收集面——入口壳 Bootstrap 调用（S3：默认猫 + 多猫注册表实现；null=禁用 QQ 转发——未注入静默）。
        /// </summary>
        /// <param name="collector">目标收集实现</param>
        public static void SetCollector(IQqTargetCollector collector)
        {
            _collector = collector;
        }

        /// <summary>
        /// 注入文件缓存根——入口壳 Bootstrap 调用（P9：Data/qqbot-files；未注入=附件缓存禁用）。
        /// </summary>
        /// <param name="root">缓存根目录</param>
        public static void SetFileCacheRoot(string root)
        {
            if (root == null)
            {
                _fileCacheRoot = "";
                return;
            }
            _fileCacheRoot = root;
            // A33——转发态文件随之定位（同 Data 根：缓存根父目录 + qq-forward.json）；显式覆盖优先（SetStatePath）——
            // 本派生仅为不改动入口壳 Bootstrap 注入点。
            if (_statePath.Length == 0)
            {
                try
                {
                    string dir = Path.GetDirectoryName(root);
                    if (dir != null && dir.Length > 0)
                    {
                        _statePath = Path.Combine(dir, "qq-forward.json");
                    }
                }
                catch (Exception e)
                {
                    LogStore.Add("QQBot", 2, "转发态路径派生失败（转发态禁用）: " + e.Message, "QQBOT");
                }
            }
        }
        /// <summary>
        /// 注入转发态文件路径——入口壳 Bootstrap 调用（A33：Data/qq-forward.json；空=转发态禁用——行为退回纯内存）。
        /// </summary>
        /// <param name="path">转发态文件绝对路径</param>
        public static void SetStatePath(string path)
        {
            if (path == null)
            {
                _statePath = "";
            }
            else
            {
                _statePath = path;
            }
        }
        /// <summary>接力来源一次性豁免——启动后首个残留来源免「异常中止轮」兜底清理（A33：回执轮据此转发回 QQ）</summary>
        private static bool _relayPending = false;
        /// <summary>
        /// 转发态落盘——独立文件（A33：挂载功能自持，不介入核心基座与真实会话）。
        /// 载荷：每猫 {游标、残留来源、3+1 计数/累计} + 每连接 msg_seq 水位（不续号则重启后首条被官方去重拒）。
        /// 时机：变更即落盘（来源入队 / 游标推进 / 轮结束 / 残留清理）——T4 收尾不调 QQ Stop，退出前 flush 不可依赖。
        /// </summary>
        public static void SaveForwardState()
        {
            if (_statePath.Length == 0 || !_stateLoaded)
            {
                return;
            }
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.Append("{\"at\":" + JsonSerializer.Serialize(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")) + ",\"seqs\":{");
                bool first = true;
                foreach (KeyValuePair<Guid, QQBotConnection> kv in _connections)
                {
                    if (!first)
                    {
                        sb.Append(",");
                    }
                    first = false;
                    sb.Append(JsonSerializer.Serialize(kv.Key.ToString()) + ":" + kv.Value.MsgSeq.ToString());
                }
                sb.Append("},\"cats\":{");
                first = true;
                foreach (KeyValuePair<string, int> kv in _cursors)
                {
                    string key = kv.Key;
                    int imm;
                    if (!_immediateCounts.TryGetValue(key, out imm))
                    {
                        imm = 0;
                    }
                    QqSource src;
                    string srcJson = "null";
                    if (PeekSource(key, out src) && src != null)
                    {
                        srcJson = "{\"type\":" + JsonSerializer.Serialize(src.Type) + ",\"targetId\":" + JsonSerializer.Serialize(src.TargetId)
                            + ",\"msgId\":" + JsonSerializer.Serialize(src.MsgId) + ",\"displayName\":" + JsonSerializer.Serialize(src.DisplayName)
                            + ",\"role\":" + JsonSerializer.Serialize(src.Role) + "}";
                    }
                    System.Text.StringBuilder acc;
                    string accText = "";
                    if (_accumulated.TryGetValue(key, out acc) && acc.Length > 0)
                    {
                        accText = acc.ToString();
                    }
                    if (!first)
                    {
                        sb.Append(",");
                    }
                    first = false;
                    sb.Append(JsonSerializer.Serialize(key) + ":{\"cursor\":" + kv.Value.ToString() + ",\"imm\":" + imm.ToString()
                        + ",\"acc\":" + JsonSerializer.Serialize(accText) + ",\"src\":" + srcJson + "}");
                }
                sb.Append("}}");
                File.WriteAllText(_statePath, sb.ToString(), Encoding.UTF8);
            }
            catch (Exception e)
            {
                LogStore.Add("QQBot", 2, "转发态落盘失败: " + e.Message, "QQBOT");
            }
        }
        /// <summary>
        /// 读 JSON 字符串属性——缺失/类型不符返回空串（转发态解析用）。
        /// </summary>
        /// <param name="el">JSON 对象</param>
        /// <param name="name">属性名</param>
        /// <returns>属性值或空串</returns>
        private static string ReadJsonStr(JsonElement el, string name)
        {
            JsonElement v;
            if (el.ValueKind == JsonValueKind.Object && el.TryGetProperty(name, out v) && v.ValueKind == JsonValueKind.String)
            {
                return v.GetString() ?? "";
            }
            return "";
        }
        /// <summary>
        /// 转发态加载与续接——启动时调用（此时前文/视图已恢复，避免读到半成品块数）。
        /// 续接判据（防重放）：块数 == 落盘游标 → 直接续接；块数不等 → 保守前看（不追发历史；/new 清空另走 Tick 既有归零分支）。
        /// 残留来源恢复入队——重启后回执轮的回复据此转发回 QQ。
        /// </summary>
        /// <param name="targets">当前绑定目标集合</param>
        private static void LoadForwardState(List<QqTarget> targets)
        {
            if (_statePath.Length == 0 || !File.Exists(_statePath))
            {
                return;
            }
            string raw = "";
            try
            {
                raw = File.ReadAllText(_statePath, Encoding.UTF8);
            }
            catch (Exception e)
            {
                LogStore.Add("QQBot", 2, "转发态加载失败（空态启动）: " + e.Message, "QQBOT");
                return;
            }
            if (raw.Length == 0)
            {
                return;
            }
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(raw))
                {
                    JsonElement root = doc.RootElement;
                    // ① msg_seq 水位续接——同 msg_id 重复 seq 会被官方去重拒（40054005）
                    JsonElement seqs;
                    if (root.TryGetProperty("seqs", out seqs) && seqs.ValueKind == JsonValueKind.Object)
                    {
                        foreach (JsonProperty p in seqs.EnumerateObject())
                        {
                            Guid botId;
                            QQBotConnection conn;
                            if (Guid.TryParse(p.Name, out botId) && _connections.TryGetValue(botId, out conn) && p.Value.ValueKind == JsonValueKind.Number)
                            {
                                conn.MsgSeq = p.Value.GetInt64();
                            }
                        }
                    }
                    // ② 每猫——游标续接 + 残留来源入队 + 3+1 状态恢复
                    JsonElement cats;
                    if (!root.TryGetProperty("cats", out cats) || cats.ValueKind != JsonValueKind.Object)
                    {
                        return;
                    }
                    foreach (JsonProperty p in cats.EnumerateObject())
                    {
                        QqTarget tg = null;
                        for (int i = 0; i < targets.Count; i = i + 1)
                        {
                            if (targets[i].Key == p.Name)
                            {
                                tg = targets[i];
                                break;
                            }
                        }
                        if (tg == null)
                        {
                            continue;
                        }
                        JsonElement c = p.Value;
                        JsonElement numEl;
                        int cursor = 0;
                        if (c.ValueKind == JsonValueKind.Object && c.TryGetProperty("cursor", out numEl) && numEl.ValueKind == JsonValueKind.Number)
                        {
                            cursor = numEl.GetInt32();
                        }
                        QqViewItem[] items = tg.GetViewItems != null ? tg.GetViewItems() : new QqViewItem[0];
                        int resolved = cursor == items.Length ? cursor : items.Length;
                        _cursors[p.Name] = resolved;
                        JsonElement src;
                        if (c.ValueKind == JsonValueKind.Object && c.TryGetProperty("src", out src) && src.ValueKind == JsonValueKind.Object)
                        {
                            QqSource s = new QqSource(ReadJsonStr(src, "type"), ReadJsonStr(src, "targetId"), ReadJsonStr(src, "msgId"), ReadJsonStr(src, "displayName"), ReadJsonStr(src, "role"));
                            if (s.Type.Length > 0 && s.TargetId.Length > 0)
                            {
                                EnqueueSource(p.Name, s);
                            }
                        }
                        int imm = 0;
                        if (c.ValueKind == JsonValueKind.Object && c.TryGetProperty("imm", out numEl) && numEl.ValueKind == JsonValueKind.Number)
                        {
                            imm = numEl.GetInt32();
                        }
                        if (imm > 0)
                        {
                            _immediateCounts[p.Name] = imm;
                        }
                        string accText = ReadJsonStr(c, "acc");
                        if (accText.Length > 0)
                        {
                            _accumulated[p.Name] = new System.Text.StringBuilder(accText);
                        }
                        LogStore.Add("QQBot", 1, "转发态续接 | " + p.Name + " 游标 " + cursor.ToString() + " → " + resolved.ToString() + "（块数 " + items.Length.ToString() + "）", "QQBOT");
                    }
                }
            }
            catch (Exception e)
            {
                LogStore.Add("QQBot", 2, "转发态解析失败（空态启动）: " + e.Message, "QQBOT");
            }
        }

        /// <summary>
        /// 启动管理器——扫描 Bot 池建立全部连接。
        /// </summary>
        public static void Start()
        {
            if (_started)
            {
                return;
            }
            _started = true;
            _startTime = DateTime.Now;
            Refresh();
            // A33——转发态续接：连接就位后加载（游标 / 残留来源 / 3+1 计数 / msg_seq 水位）——重启后接续转发回 QQ
            _relayPending = true;
            if (_collector != null)
            {
                LoadForwardState(_collector.CollectAll());
            }
            // 装载流程结束——置「转发态就绪」（SaveForwardState 门：未就绪不落盘，防空态覆写已有快照）
            _stateLoaded = true;
        }

        /// <summary>
        /// 停止管理器——停止全部连接并清空。
        /// </summary>
        public static void Stop()
        {
            _started = false;
            List<Guid> keys = new List<Guid>(_connections.Keys);
            for (int i = 0; i < keys.Count; i = i + 1)
            {
                QQBotConnection conn;
                if (_connections.TryGetValue(keys[i], out conn))
                {
                    conn.Stop();
                }
            }
            _connections.Clear();
        }

        /// <summary>
        /// 刷新连接集合——扫描 Bot 池：新增缺失连接 / 停止已删除 Bot 连接。
        /// Bot 池 CRUD 变更后调用（注册即建连接语义）。
        /// </summary>
        public static void Refresh()
        {
            CH_QqBotConfigStore store = null;
            DataBox.TryResolve<CH_QqBotConfigStore>(out store);
            if (store == null)
            {
                return;
            }
            CH_QqBotConfig[] configs = store.GetAll();
            HashSet<Guid> active = new HashSet<Guid>();
            for (int i = 0; i < configs.Length; i = i + 1)
            {
                CH_QqBotConfig c = configs[i];
                active.Add(c.QqBotId);
                QQBotConnection existing;
                if (_connections.TryGetValue(c.QqBotId, out existing))
                {
                    // 配置变更检测——sandbox/appId/displayName 任一变化 → 重建连接（edit 后立即生效；沙箱↔正式切换关键）
                    if (existing.Sandbox != c.Sandbox || existing.AppId != c.AppId || existing.DisplayName != c.DisplayName)
                    {
                        existing.Stop();
                        _connections.Remove(c.QqBotId);
                        string secret = store.GetSecret(c.QqBotId);
                        QQBotConnection conn = new QQBotConnection(
                            c.QqBotId, c.DisplayName, c.AppId, secret, c.Sandbox,
                            delegate (string raw) { OnMessage(c.QqBotId, raw); });
                        _connections[c.QqBotId] = conn;
                        conn.Start();
                        string sandboxText = "false";
                        if (c.Sandbox)
                        {
                            sandboxText = "true";
                        }
                        LogStore.Add("QQBot", 1, "配置变更重建连接 | " + c.DisplayName + "（sandbox=" + sandboxText + "）", "QQBOT");
                    }
                    continue;
                }
                string newSecret = store.GetSecret(c.QqBotId);
                QQBotConnection newConn = new QQBotConnection(
                    c.QqBotId, c.DisplayName, c.AppId, newSecret, c.Sandbox,
                    delegate (string raw) { OnMessage(c.QqBotId, raw); });
                _connections[c.QqBotId] = newConn;
                newConn.Start();
            }
            // 停止已删除的 Bot 连接
            List<Guid> toRemove = new List<Guid>();
            foreach (KeyValuePair<Guid, QQBotConnection> kv in _connections)
            {
                if (!active.Contains(kv.Key))
                {
                    toRemove.Add(kv.Key);
                }
            }
            for (int i = 0; i < toRemove.Count; i = i + 1)
            {
                QQBotConnection conn;
                if (_connections.TryGetValue(toRemove[i], out conn))
                {
                    conn.Stop();
                    _connections.Remove(toRemove[i]);
                }
            }
        }
        /// <summary>
        /// 消息到达回调——WS 层投递（raw JSON）。
        /// 路由链：解析消息 → 强匹配指令直执（不走 LLM）→ 查绑定 Cat → 广播注入所有绑定且启用的 Cat。
        /// </summary>
        /// <param name="qqBotId">Bot 配置身份</param>
        /// <param name="raw">原始消息 JSON</param>
        private static void OnMessage(Guid qqBotId, string raw)
        {
            string t = "";
            QqSource source = null;
            string text = "";
            List<QqAttachment> attachments = null;
            if (!TryParseMessage(raw, out t, out source, out text, out attachments))
            {
                return;
            }
            // P9 附件——私聊+群@都下载缓存（URL 时效短——立即下载；注入文本带缓存路径；v0.96.1 放开群聊）
            string attachText = "";
            if (attachments != null && attachments.Count > 0)
            {
                bool hasFace = _faceTagRegex.IsMatch(text);
                attachText = DownloadAttachments(qqBotId, attachments);
                if (hasFace)
                {
                    // v0.96.1 简化——faceType 原位嵌入真实本地路径，agent 心智负担更低；成功时尾部不重复
                    text = NormalizeFaceTags(text, attachments);
                    if (attachText.Length > 0 && !attachText.Contains("|超限拒绝") && !attachText.Contains("|下载失败") && !attachText.Contains("|缓存未配置"))
                    {
                        attachText = "";
                    }
                }
            }
            if (text.Length == 0 && attachText.Length == 0)
            {
                return;
            }
            // 强匹配指令——不走 LLM 路由，代码直执（R2.3.8；Q5 扩充 /new /info）
            string cmdReply = HandleSlashCommand(text, qqBotId);
            if (cmdReply != null)
            {
                SendToSource(qqBotId, source, cmdReply);
                return;
            }
            // 查绑定该 Bot 的猫（默认猫 + 多猫）
            List<QqTarget> targets = new List<QqTarget>();
            if (_collector != null)
            {
                targets = _collector.CollectByBot(qqBotId);
            }
            if (targets.Count == 0)
            {
                SendToSource(qqBotId, source, "无猫");
                return;
            }
            // 广播注入所有绑定且启用的 Cat——消息头区分渠道+来源（v0.96.1：私聊硬编码雾理莎/群@昵称+角色）
            // 🔴 三字典单线程化（R6-P1-01）——WS 线程只入队事件，EnqueueSource/ResetRound 由主线程 Tick 事件泵统一消费
            bool anyInjected = false;
            for (int i = 0; i < targets.Count; i = i + 1)
            {
                QqTarget tg = targets[i];
                if (!tg.Enable)
                {
                    continue;
                }
                tg.Inject("[来自QQ]" + BuildHeader(source) + " " + text + attachText);
                _eventQueue.Enqueue(new QqServiceEvent { Kind = "source", CatKey = tg.Key, Source = source });
                _eventQueue.Enqueue(new QqServiceEvent { Kind = "reset", CatKey = tg.Key });
                anyInjected = true;
            }
            if (!anyInjected)
            {
                SendToSource(qqBotId, source, "目标 Cat 未启用 qqbot 转发功能");
            }
        }
        /// <summary>
        /// 解析消息——C2C 私聊 / GROUP_AT 群@。
        /// </summary>
        /// <param name="raw">原始消息 JSON</param>
        /// <param name="t">事件类型</param>
        /// <param name="source">来源——private:uid / group:gid:mid</param>
        /// <param name="text">消息文本（剥离 @Bot 前缀）</param>
        /// <param name="attachments">附件列表（P9——attachments 非空即含附件）；无附件 null</param>
        /// <returns>解析成功 true</returns>
        private static bool TryParseMessage(string raw, out string t, out QqSource source, out string text, out List<QqAttachment> attachments)
        {
            t = "";
            source = null;
            text = "";
            attachments = null;
            try
            {
                using (JsonDocument d = JsonDocument.Parse(raw))
                {
                    if (!d.RootElement.TryGetProperty("t", out JsonElement tp))
                    {
                        return false;
                    }
                    t = tp.GetString() ?? "";
                    if (t == "C2C_MESSAGE_CREATE")
                    {
                        JsonElement dd = d.RootElement.GetProperty("d");
                        JsonElement authorE = dd.GetProperty("author");
                        string uid = authorE.GetProperty("user_openid").GetString() ?? "";
                        string authorName = "";
                        if (authorE.TryGetProperty("username", out JsonElement un))
                        {
                            authorName = un.GetString() ?? "";
                        }
                        string content = "";
                        if (dd.TryGetProperty("content", out JsonElement c))
                        {
                            content = c.GetString() ?? "";
                        }
                        string msgId = "";
                        if (dd.TryGetProperty("id", out JsonElement idE))
                        {
                            msgId = idE.GetString() ?? "";
                        }
                        attachments = ParseAttachments(dd);
                        source = new QqSource("private", uid, msgId, authorName, "");
                        text = StripAtMention(content);
                        return true;
                    }
                    if (t == "GROUP_AT_MESSAGE_CREATE")
                    {
                        JsonElement dd = d.RootElement.GetProperty("d");
                        JsonElement authorE = dd.GetProperty("author");
                        string gid = dd.GetProperty("group_openid").GetString() ?? "";
                        string mid = authorE.GetProperty("member_openid").GetString() ?? "";
                        string authorName = "";
                        if (authorE.TryGetProperty("username", out JsonElement un))
                        {
                            authorName = un.GetString() ?? "";
                        }
                        string memberRole = "";
                        if (authorE.TryGetProperty("member_role", out JsonElement mr))
                        {
                            memberRole = mr.GetString() ?? "";
                        }
                        string content = "";
                        if (dd.TryGetProperty("content", out JsonElement c))
                        {
                            content = c.GetString() ?? "";
                        }
                        string msgId = "";
                        if (dd.TryGetProperty("id", out JsonElement idE))
                        {
                            msgId = idE.GetString() ?? "";
                        }
                        attachments = ParseAttachments(dd);
                        source = new QqSource("group", gid + ":" + mid, msgId, authorName, memberRole);
                        text = StripAtMention(content);
                        return true;
                    }
                }
            }
            catch (Exception)
            {
            }
            return false;
        }        /// <summary>附件大小硬限制——200MB（官方硬限制）</summary>
        private const long MaxAttachmentBytes = 200L * 1024 * 1024;

        /// <summary>
        /// 附件下载落盘——逐项：超限拒绝 / 下载 / 失败标记；返回注入文本段（P9 接收）。
        /// </summary>
        /// <param name="qqBotId">Bot 配置身份</param>
        /// <param name="atts">附件列表</param>
        /// <returns>注入文本段（[附件: ...] 拼接；无附件空串）</returns>
        private static string DownloadAttachments(Guid qqBotId, List<QqAttachment> atts)
        {
            QQBotConnection conn;
            if (!_connections.TryGetValue(qqBotId, out conn))
            {
                return "";
            }
            string dir = GetFileCacheDir();
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < atts.Count; i = i + 1)
            {
                QqAttachment a = atts[i];
                if (a.Size > MaxAttachmentBytes)
                {
                    sb.Append(" [附件: " + a.FileName + "|" + a.Size + "|超限拒绝]");
                    LogStore.Add("QQBot", 2, "附件超限拒绝 | " + a.FileName + " | " + a.Size, "QQBOT");
                    continue;
                }
                if (dir.Length == 0)
                {
                    sb.Append(" [附件: " + a.FileName + "|" + a.Size + "|缓存未配置]");
                    continue;
                }
                string dest = Path.Combine(dir, DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_" + SanitizeFileName(a.FileName));
                if (conn.DownloadAttachment(a.Url, dest))
                {
                    a.LocalPath = dest;
                    // v0.96.1 简化——成功路径含文件名，不重复 size；faceType 原位嵌入路径时尾部 attachText 清空
                    sb.Append(" [附件: " + dest + "]");
                }
                else
                {
                    sb.Append(" [附件: " + a.FileName + "|" + a.Size + "|下载失败]");
                }
            }
            return sb.ToString();
        }
        /// <summary>
        /// 解析附件数组——attachments 非空即含附件（消息事件与文本同构）。
        /// </summary>
        /// <param name="dd">事件 d 对象</param>
        /// <returns>附件列表；无附件 null</returns>
        private static List<QqAttachment> ParseAttachments(JsonElement dd)
        {
            if (!dd.TryGetProperty("attachments", out JsonElement attE) || attE.ValueKind != JsonValueKind.Array)
            {
                return null;
            }
            List<QqAttachment> result = new List<QqAttachment>();
            for (int i = 0; i < attE.GetArrayLength(); i = i + 1)
            {
                JsonElement a = attE[i];
                QqAttachment att = new QqAttachment();
                if (a.TryGetProperty("url", out JsonElement u))
                {
                    att.Url = u.GetString() ?? "";
                }
                if (a.TryGetProperty("filename", out JsonElement f))
                {
                    att.FileName = f.GetString() ?? "";
                }
                if (a.TryGetProperty("size", out JsonElement s) && s.ValueKind == JsonValueKind.Number)
                {
                    att.Size = s.GetInt64();
                }
                if (a.TryGetProperty("content_type", out JsonElement ct))
                {
                    att.ContentType = ct.GetString() ?? "";
                }
                if (att.Url.Length > 0)
                {
                    result.Add(att);
                }
            }
            if (result.Count > 0)
            {
                return result;
            }
            return null;
        }

        /// <summary>文件名净化——去非法字符（落盘安全）</summary>
        private static string SanitizeFileName(string name)
        {
            string s = "file";
            if (name != null)
            {
                s = name;
            }
            char[] invalid = Path.GetInvalidFileNameChars();
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < s.Length; i = i + 1)
            {
                bool bad = false;
                for (int j = 0; j < invalid.Length; j = j + 1)
                {
                    if (s[i] == invalid[j])
                    {
                        bad = true;
                        break;
                    }
                }
                if (bad)
                {
                    sb.Append('_');
                }
                else
                {
                    sb.Append(s[i]);
                }
            }
            string result = sb.ToString().Trim();
            if (result.Length == 0)
            {
                return "file";
            }
            return result;
        }

        /// <summary>缓存目录——Ensure 创建（未配置返回空串）</summary>
        private static string GetFileCacheDir()
        {
            if (_fileCacheRoot.Length == 0)
            {
                return "";
            }
            try
            {
                Directory.CreateDirectory(_fileCacheRoot);
            }
            catch (Exception)
            {
                return "";
            }
            return _fileCacheRoot;
        }

        /// <summary>
        /// 剥离群@的 &lt;@!bot_openid&gt; 前缀——强匹配指令需要纯净文本。
        /// </summary>
        /// <param name="content">原始内容</param>
        /// <returns>剥离后的文本</returns>
        private static string StripAtMention(string content)
        {
            string s = "";
            if (content != null)
            {
                s = content.Trim();
            }
            if (s.StartsWith("<@"))
            {
                int end = s.IndexOf('>');
                if (end >= 0)
                {
                    s = s.Substring(end + 1).Trim();
                }
            }
            return s;
        }
        /// <summary>
        /// 归一化 QQ 富文本 faceType 标记——&lt;faceType=X,...&gt; → [本地缓存路径] 原位嵌入。
        /// 多图嵌长文本可区分图片与文字的对应关系；agent 直接可调 image-analyze（v0.96.1）。
        /// </summary>
        /// <param name="content">原始内容（已剥离 @ 前缀）</param>
        /// <param name="attachments">附件列表——按序对应 faceType 标记；降级链：本地路径→文件名→[图片]</param>
        /// <returns>归一化后的文本</returns>
        private static string NormalizeFaceTags(string content, List<QqAttachment> attachments)
        {
            // [段1] 空内容——原样回退（?? 语法糖展开）
            if (string.IsNullOrEmpty(content))
            {
                if (content == null)
                {
                    return "";
                }
                return content;
            }
            // [段2] 匹配收集——正则整体匹配（替代 MatchEvaluator 委托闭包；无匹配直接返回原文本）
            MatchCollection matches = _faceTagRegex.Matches(content);
            if (matches.Count == 0)
            {
                return content;
            }
            // [段3] 逐匹配原位替换——附件按序对应（降级链：本地路径→文件名→[图片]）
            StringBuilder sb = new StringBuilder();
            int cursor = 0;
            int idx = 0;
            for (int i = 0; i < matches.Count; i = i + 1)
            {
                Match m = matches[i];
                sb.Append(content.Substring(cursor, m.Index - cursor));
                string mark = "";
                if (attachments != null && idx < attachments.Count)
                {
                    QqAttachment a = attachments[idx];
                    if (!string.IsNullOrEmpty(a.LocalPath))
                    {
                        mark = a.LocalPath;
                    }
                    else if (!string.IsNullOrEmpty(a.FileName))
                    {
                        mark = a.FileName;
                    }
                }
                idx = idx + 1;
                if (string.IsNullOrEmpty(mark))
                {
                    sb.Append("[图片]");
                }
                else
                {
                    sb.Append("[" + mark + "]");
                }
                cursor = m.Index + m.Length;
            }
            sb.Append(content.Substring(cursor));
            return sb.ToString();
        }        /// <summary>
                 /// 构建消息头——区分渠道+来源（v0.96.1）。
                 /// 私聊：硬编码雾理莎（莎本人私聊 ID，User.md 补身份）；群@：昵称+角色（事件白拿字段）。
                 /// </summary>
                 /// <param name="source">消息来源</param>
                 /// <returns>消息头——[私聊|雾理莎] / [群@|昵称(角色)]</returns>
        private static string BuildHeader(QqSource source)
        {
            if (source.Type == "private")
            {
                return "[私聊|雾理莎]";
            }
            string name = source.DisplayName ?? "";
            string role = source.Role ?? "";
            if (name.Length == 0)
            {
                return "[群@]";
            }
            if (role.Length == 0)
            {
                return "[群@|" + name + "]";
            }
            return "[群@|" + name + "(" + RoleText(role) + ")]";
        }

        /// <summary>群角色中文映射——owner=群主 / admin=管理员 / member=成员；未知原值</summary>
        /// <param name="role">事件 member_role 原值</param>
        /// <returns>中文角色文本</returns>
        private static string RoleText(string role)
        {
            switch (role)
            {
                case "owner": return "群主";
                case "admin": return "管理员";
                case "member": return "成员";
                default: return role;
            }
        }
        /// <summary>
        /// 强匹配指令处理——/ 开头指令完全一致才命中；命中返回回复文本，未命中返回 null（走 LLM 路由）。
        /// Q5：/new 重开新会话（前端 session.new 同款）+ /info 查看状态（qqbot 管理器 + 合并猫 info）。
        /// </summary>
        /// <param name="text">消息文本</param>
        /// <param name="qqBotId">Bot 配置身份——按 bot 收集绑定猫</param>
        /// <returns>指令回复；非指令 null</returns>
        private static string HandleSlashCommand(string text, Guid qqBotId)
        {
            if (text == "/ping")
            {
                return "pong";
            }
            if (text == "/info")
            {
                return BuildInfoText(qqBotId);
            }
            if (text == "/new")
            {
                return HandleNewSession(qqBotId);
            }
            // A32——兜底拉取：重启后补感知（默认 2 条 / 上限 4 条；design-qqbot-forward §五）
            if (text == "/last" || text.StartsWith("/last "))
            {
                return BuildLastText(qqBotId, text);
            }
            if (text.StartsWith("/"))
            {
                return "未知指令: " + text + "（可用: /ping /info /new /last）";
            }
            return null;
        }
        /// <summary>
        /// 取视图层尾部 text 块——倒序收集至多 n 条（RenderType=text 且内容非空），返回时间正序列表。
        /// </summary>
        /// <param name="tg">绑定目标</param>
        /// <param name="n">条数上限</param>
        /// <param name="maxChars">单块字符上限（超长头尾保留）</param>
        /// <returns>按时间正序的文本块</returns>
        private static List<string> LastTextBlocks(QqTarget tg, int n, int maxChars)
        {
            List<string> picked = new List<string>();
            if (tg.GetViewItems == null)
            {
                return picked;
            }
            QqViewItem[] items = tg.GetViewItems();
            for (int i = items.Length - 1; i >= 0 && picked.Count < n; i = i - 1)
            {
                QqViewItem it = items[i];
                if (it.RenderType != "text" || it.Content.Length == 0)
                {
                    continue;
                }
                string s = it.Content;
                if (s.Length > maxChars)
                {
                    int head = maxChars * 2 / 3;
                    int tail = maxChars - head;
                    s = s.Substring(0, head) + "\n…（本块已截断，共 " + s.Length + " 字符）…\n" + s.Substring(s.Length - tail);
                }
                picked.Add(s);
            }
            picked.Reverse();
            return picked;
        }
        /// <summary>
        /// 构建 /last 回复——取绑定猫视图层尾部 text 块（兜底链：重启后补感知——design-qqbot-forward §五）。
        /// 默认 2 条 / 上限 4 条；单块 900 字符、总量 1500 字符按头尾截断（直执通道不经过 3+1 截断保护）。
        /// </summary>
        /// <param name="qqBotId">Bot 配置身份——按 bot 收集绑定猫</param>
        /// <param name="text">指令原文（/last 或 /last N）</param>
        /// <returns>回复文本</returns>
        private static string BuildLastText(Guid qqBotId, string text)
        {
            int maxBlocks = 4;
            int maxBlockChars = 900;
            int maxTotalChars = 1500;
            int n = 2;
            string arg = text.Length > 5 ? text.Substring(5).Trim() : "";
            if (arg.Length > 0)
            {
                int parsed;
                if (int.TryParse(arg, out parsed) && parsed > 0)
                {
                    n = parsed > maxBlocks ? maxBlocks : parsed;
                }
            }
            if (_collector == null)
            {
                return "无视图数据（绑定目标收集面未注入）";
            }
            List<QqTarget> targets = _collector.CollectByBot(qqBotId);
            if (targets.Count == 0)
            {
                return "无猫";
            }
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < targets.Count; i = i + 1)
            {
                QqTarget tg = targets[i];
                if (sb.Length > 0)
                {
                    sb.Append("\n\n");
                }
                List<string> blocks = LastTextBlocks(tg, n, maxBlockChars);
                if (blocks.Count == 0)
                {
                    sb.Append("【" + tg.DisplayName + "】无已生成回复");
                    continue;
                }
                sb.Append("【" + tg.DisplayName + "】最近 " + blocks.Count + " 条：");
                for (int j = 0; j < blocks.Count; j = j + 1)
                {
                    sb.Append("\n--- " + (j + 1) + " ---\n" + blocks[j]);
                }
            }
            string body = sb.ToString();
            if (body.Length > maxTotalChars)
            {
                int head = maxTotalChars * 2 / 3;
                int tail = maxTotalChars - head;
                body = body.Substring(0, head) + "\n…（总长 " + body.Length + " 字符，已截断）…\n" + body.Substring(body.Length - tail);
            }
            return body;
        }

        /// <summary>
        /// /new 指令执行——等同前端 session.new（置位标志投递总线，主线程泵异步执行）+ 注入摘要同步返回（Q5：注入前文与工具气泡同步到 qqbot）。
        /// </summary>
        /// <param name="qqBotId">Bot 配置身份</param>
        /// <returns>回复文本</returns>
        private static string HandleNewSession(Guid qqBotId)
        {
            if (_collector == null)
            {
                return "无法执行：qqbot 收集器未就绪";
            }
            List<QqTarget> targets = _collector.CollectByBot(qqBotId);
            if (targets.Count == 0)
            {
                return "无绑定猫";
            }
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < targets.Count; i = i + 1)
            {
                QqTarget tg = targets[i];
                if (i > 0)
                {
                    sb.Append("\n\n");
                }
                sb.Append("【" + tg.DisplayName + "】");
                if (tg.NewSession != null)
                {
                    sb.Append("\n" + tg.NewSession());
                }
                else
                {
                    sb.Append("\n不支持新会话");
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// 构建 /info 状态文本——运行总时长 + 当前 qqbot 数 + 可用指令 + 合并绑定猫 info（Q5）。
        /// </summary>
        /// <param name="qqBotId">Bot 配置身份</param>
        /// <returns>状态文本</returns>
        private static string BuildInfoText(Guid qqBotId)
        {
            TimeSpan up = DateTime.Now - _startTime;
            string upText = "";
            if (up.TotalHours >= 1)
            {
                upText = (int)up.TotalHours + "h" + up.Minutes + "m";
            }
            else
            {
                upText = up.Minutes + "m" + up.Seconds + "s";
            }
            StringBuilder sb = new StringBuilder();
            sb.Append("【QQ 管理器状态】\n运行时长: " + upText + "\n当前 qqbot 数: " + _connections.Count + "\n可用指令: /ping /info /new /last");
            // Q5——合并绑定猫的 info（直接调用工具函数，不经 LLM/OA）；A31——附会话状态（兜底判据：IDLE = 安全拉取窗口）
            if (_collector != null)
            {
                List<QqTarget> targets = _collector.CollectByBot(qqBotId);
                for (int i = 0; i < targets.Count; i = i + 1)
                {
                    QqTarget tg = targets[i];
                    string state = "未知";
                    if (tg.IsIdle != null)
                    {
                        state = tg.IsIdle() ? "IDLE" : "运行中";
                    }
                    sb.Append("\n\n【猫: " + tg.DisplayName + "】\n会话状态: " + state);
                    if (tg.GetInfo != null)
                    {
                        sb.Append("\n" + tg.GetInfo());
                    }
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// 发送回复到来源——通过 Bot 连接（QqSource 载荷；指令回复/错误提示走纯文本通道）。
        /// </summary>
        /// <param name="qqBotId">Bot 配置身份</param>
        /// <param name="source">来源</param>
        /// <param name="text">回复文本</param>
        private static void SendToSource(Guid qqBotId, QqSource source, string text)
        {
            QQBotConnection conn;
            if (!_connections.TryGetValue(qqBotId, out conn))
            {
                return;
            }
            // 指令回复/错误提示——MD 通道（Q4 全 MD），携带被动 msg_id；失败降级主动消息一次（A34）
            SendWithFallback(conn, source.Type, source.TargetId, text, source.MsgId, true);
        }

        /// <summary>
        /// 注入来源入队——猫 Key → QqSource FIFO（R2.3.5 输出转发消费）。
        /// </summary>
        /// <param name="catKey">猫标识（majordomo / cat id）</param>
        /// <param name="source">来源</param>
        private static void EnqueueSource(string catKey, QqSource source)
        {
            Queue<QqSource> q;
            if (!_sourceQueues.TryGetValue(catKey, out q))
            {
                q = new Queue<QqSource>();
                _sourceQueues[catKey] = q;
            }
            q.Enqueue(source);
        }
        /// <summary>
        /// 出队来源——R2.3.5 输出转发：回复目标 = 触发来源（FIFO 对齐注入顺序）。
        /// </summary>
        /// <param name="catKey">猫标识（majordomo / cat id）</param>
        /// <param name="source">出队来源</param>
        /// <returns>有来源 true</returns>
        internal static bool TryDequeueSource(string catKey, out QqSource source)
        {
            Queue<QqSource> q;
            if (_sourceQueues.TryGetValue(catKey, out q) && q.Count > 0)
            {
                source = q.Dequeue();
                return true;
            }
            source = null;
            return false;
        }
        /// <summary>
        /// 队首来源读取——不消费（整轮转发同一来源；roundsum 轮结束才出队）。
        /// </summary>
        /// <param name="catKey">猫标识</param>
        /// <param name="source">队首来源</param>
        /// <returns>有来源 true</returns>
        private static bool PeekSource(string catKey, out QqSource source)
        {
            Queue<QqSource> q;
            if (_sourceQueues.TryGetValue(catKey, out q) && q.Count > 0)
            {
                source = q.Peek();
                return true;
            }
            source = null;
            return false;
        }
        /// <summary>
        /// 来源队列非空判定——异常中止轮残留清理触发条件之一。
        /// </summary>
        /// <param name="catKey">猫标识</param>
        /// <returns>队列非空 true</returns>
        private static bool HasPendingSource(string catKey)
        {
            Queue<QqSource> q;
            return _sourceQueues.TryGetValue(catKey, out q) && q.Count > 0;
        }                /// <summary>
                         /// 重置交互轮状态——新用户消息注入时调用（3+1 计数归零 + 累计清空）。
                         /// </summary>
                         /// <param name="catKey">猫标识</param>
        private static void ResetRound(string catKey)
        {
            _immediateCounts[catKey] = 0;
            _accumulated.Remove(catKey);
        }        /// <summary>
                 /// 清空交互轮状态——异常中止轮残留来源兜底（会话 Idle 且队列残留 = 上轮无 roundsum 结束）：来源丢弃 + 计数/累计清零。
                 /// </summary>
                 /// <param name="catKey">猫标识</param>
        private static void ClearRoundState(string catKey)
        {
            Queue<QqSource> q;
            if (_sourceQueues.TryGetValue(catKey, out q))
            {
                while (q.Count > 0)
                {
                    q.Dequeue();
                }
            }
            ResetRound(catKey);
            LogStore.Add("QQBot", 2, "残留来源清理 | " + catKey + "（异常中止轮兜底）", "QQBOT");
            // A33——残留清理即落盘（防重启后恢复已清理来源）
            SaveForwardState();
        }
        /// <summary>
        /// 即时转发——3+1 预算：计数 &lt; 3 立即转发；否则累计到轮末汇总（第 4 次）。
        /// 无 qqbot 来源（前端对话）→ 不转发（被动机制——只有用户主动输入后才启用回复）。
        /// </summary>
        /// <param name="tg">绑定目标</param>
        /// <param name="content">text 块内容</param>
        private static void ForwardText(QqTarget tg, string content)
        {
            QqSource source;
            if (!PeekSource(tg.Key, out source))
            {
                // A34——无来源不转发但必留痕（原静默 return：重启轮/前端轮的漏转发无从诊断）
                LogStore.Add("QQBot", 1, "转发跳过（无来源） | " + tg.Key + " ← " + Truncate(content, 30), "QQBOT");
                return;
            }
            int n;
            if (!_immediateCounts.TryGetValue(tg.Key, out n))
            {
                n = 0;
            }
            if (n < MaxImmediateReplies)
            {
                _immediateCounts[tg.Key] = n + 1;
                SendReplyToTarget(tg, source, content);
            }
            else
            {
                System.Text.StringBuilder sb;
                if (!_accumulated.TryGetValue(tg.Key, out sb))
                {
                    sb = new System.Text.StringBuilder();
                    _accumulated[tg.Key] = sb;
                }
                if (sb.Length > 0)
                {
                    sb.Append("\n\n");
                }
                sb.Append(content);
            }
        }
        /// <summary>
        /// 轮结束——roundsum 块哨兵：累计非空 → 第 4 次汇总转发（单条发送）；出队消费本轮来源；重置 3+1 状态。
        /// </summary>
        /// <param name="tg">绑定目标</param>
        private static void FinishRound(QqTarget tg)
        {
            QqSource source;
            if (TryDequeueSource(tg.Key, out source))
            {
                System.Text.StringBuilder sb;
                if (_accumulated.TryGetValue(tg.Key, out sb) && sb.Length > 0)
                {
                    SendReplyToTarget(tg, source, sb.ToString());
                }
            }
            ResetRound(tg.Key);
            // A33——来源出队即落盘（防重启后恢复已消费来源 → 错配转发）
            SaveForwardState();
        }
        /// <summary>游标——猫 Key → 已处理消息数（R2.3.5 只转发启用后新块）</summary>
        private static readonly Dictionary<string, int> _cursors = new Dictionary<string, int>();
        /// <summary>
        /// 事件泵——WS 线程入队事件统一在主线程消费（R6-P1-01 单线程化）。
        /// source 事件 → 注入来源入队；reset 事件 → 轮状态重置（3+1 计数归零 + 累计清空）。
        /// </summary>
        private static void DrainEvents()
        {
            QqServiceEvent ev;
            bool changed = false;
            while (_eventQueue.TryDequeue(out ev))
            {
                if (ev.Kind == "source")
                {
                    EnqueueSource(ev.CatKey, ev.Source);
                    changed = true;
                }
                else if (ev.Kind == "reset")
                {
                    ResetRound(ev.CatKey);
                    changed = true;
                }
            }
            // A33——来源入队 / 轮状态重置即落盘（残留来源是重启接力的关键载荷）
            if (changed)
            {
                SaveForwardState();
            }
        }
        /// <summary>输出转发轮询——主线程每帧调用（宿主主循环接入）。视图块游标增量：text 块 → 即时转发 ≤3 / 超限累计；roundsum 块 → 轮结束哨兵（第 4 次汇总转发 + 出队来源 + 重置）。无 qqbot 来源（前端对话）不转发——被动机制。</summary>
        public static void Tick()
        {
            if (!_started)
            {
                return;
            }
            // 事件泵——WS 线程入队事件统一在主线程消费（R6-P1-01：三字典访问面收敛单线程）
            DrainEvents();
            List<QqTarget> targets = new List<QqTarget>();
            if (_collector != null)
            {
                targets = _collector.CollectAll();
            }
            for (int i = 0; i < targets.Count; i = i + 1)
            {
                QqTarget tg = targets[i];
                QqViewItem[] items = tg.GetViewItems();
                int count = items.Length;
                int cursor = GetCursor(tg.Key);
                if (count < cursor)
                {
                    // 会话重建（/new 清空后视图块数回退）——游标归零重新扫描
                    cursor = 0;
                    _cursors[tg.Key] = 0;
                }
                if (count <= cursor)
                {
                    // 无新块——异常中止轮残留来源兜底清理（会话 Idle 且队列残留 = 上轮无 roundsum 结束）
                    if (HasPendingSource(tg.Key) && tg.IsIdle())
                    {
                        if (_relayPending)
                        {
                            // A33——接力来源一次性豁免：启动后首个残留来源是跨重启接力载荷，不做兜底清理（回执轮据此转发）
                            _relayPending = false;
                            LogStore.Add("QQBot", 1, "接力来源豁免兜底清理 | " + tg.Key, "QQBOT");
                        }
                        else
                        {
                            ClearRoundState(tg.Key);
                        }
                    }
                    continue;
                }
                for (int j = cursor; j < count; j = j + 1)
                {
                    QqViewItem it = items[j];
                    if (it.RenderType == "roundsum")
                    {
                        // 轮结束哨兵——第 4 次汇总转发 + 出队来源 + 重置 3+1 状态
                        FinishRound(tg);
                    }
                    else if (it.RenderType == "text" && it.Content.Length > 0)
                    {
                        // 即时转发 ≤3；超限累计到轮末汇总
                        ForwardText(tg, it.Content);
                    }
                }
                _cursors[tg.Key] = count;
                // A33——游标推进即落盘（T4 收尾不调 QQ Stop，退出前 flush 不可依赖）
                SaveForwardState();
            }
        }
        /// <summary>读取游标——缺省 0</summary>
        private static int GetCursor(string catKey)
        {
            int c;
            if (_cursors.TryGetValue(catKey, out c))
            {
                return c;
            }
            return 0;
        }

        /// <summary>
        /// 转发回复到来源——私聊走 P8 链路（MD 检测 + 切分）；群聊保持 msg_type=0 直发。
        /// </summary>
        /// <param name="tg">绑定目标</param>
        /// <param name="source">来源（QqSource）</param>
        /// <param name="text">回复文本</param>
        private static void SendReplyToTarget(QqTarget tg, QqSource source, string text)
        {
            QQBotConnection conn;
            if (!_connections.TryGetValue(tg.QqBotId, out conn))
            {
                return;
            }
            if (source.Type == "private")
            {
                // 私聊 P8 链路——MD 自动检测 + 标题分块切分（被动上限 4 条）
                SendPrivateReply(conn, tg, source, text);
                return;
            }
            // 群聊——MD 通道直发（Q4 全 MD——msg_type=2；携带被动 msg_id；官方群 MD 失败 L2 留痕）
            string formatted = tg.DisplayName + "：\n" + text;
            conn.SendReply(source.Type, source.TargetId, formatted, source.MsgId, true);
        }

        /// <summary>
        /// 私聊回复——MD 自动检测 + 标题分块切分 + 逐条发送（失败停止后续）。
        /// </summary>
        /// <param name="conn">Bot 连接</param>
        /// <param name="tg">绑定目标</param>
        /// <param name="source">来源</param>
        /// <param name="text">回复正文</param>
        private static void SendPrivateReply(QQBotConnection conn, QqTarget tg, QqSource source, string text)
        {
            // P9 文件标记——拆出 [文件:path] 列表，正文去除标记后走既有链路
            List<string> files = ExtractFileMarks(text, out text);
            if (text.Length > 0)
            {
                // 3+1 预算——每条转发单次 API 调用（整轮 ≤4 不超被动回复上限）；长文截断；失败降级主动消息一次（A34）
                string piece = tg.DisplayName + "：\n" + Truncate(text, MaxChunkChars);
                SendWithFallback(conn, "private", source.TargetId, piece, source.MsgId, true);
            }
            // P9 文件发送——成功静默；失败错误文本嵌入消息渠道
            for (int i = 0; i < files.Count; i = i + 1)
            {
                string err = conn.SendFile(source.TargetId, files[i], source.MsgId);
                if (err.Length > 0)
                {
                    conn.SendReply("private", source.TargetId, err, source.MsgId, false);
                }
            }
        }
        /// <summary>
        /// 发送回复（被动优先 + 超窗降级）——首次失败且携带 msg_id 时，清 msg_id 走主动消息重试一次（design-qqbot-forward §六）。
        /// </summary>
        /// <param name="conn">Bot 连接</param>
        /// <param name="msgType">消息类型——private / group</param>
        /// <param name="targetId">目标 ID</param>
        /// <param name="text">回复正文</param>
        /// <param name="msgId">被动回复引用（空=主动消息，不再降级）</param>
        /// <param name="isMarkdown">true=MD 通道</param>
        /// <returns>发送成功 true</returns>
        private static bool SendWithFallback(QQBotConnection conn, string msgType, string targetId, string text, string msgId, bool isMarkdown)
        {
            if (conn.SendReply(msgType, targetId, text, msgId, isMarkdown))
            {
                return true;
            }
            if (msgId == null || msgId.Length == 0)
            {
                return false;
            }
            // 被动失败（超窗/去重/次数超限）——降级主动消息一次；成败均 L2 留痕（失败响应体由 SendReply 留）
            bool ok = conn.SendReply(msgType, targetId, text, "", isMarkdown);
            LogStore.Add("QQBot", 2, (ok ? "被动回复失败 → 主动消息降级成功 | " : "被动回复失败 → 主动消息降级亦失败 | ") + msgType + ":" + targetId, "QQBOT");
            return ok;
        }
        /// <summary>
        /// 提取文件标记——[文件:path] 列表；正文移除标记段（P9 零新工具触发面）。
        /// </summary>
        /// <param name="text">猫回复正文</param>
        /// <param name="cleanText">移除标记后的正文</param>
        /// <returns>文件路径列表</returns>
        private static List<string> ExtractFileMarks(string text, out string cleanText)
        {
            List<string> files = new List<string>();
            StringBuilder sb = new StringBuilder();
            int idx = 0;
            while (idx < text.Length)
            {
                int start = text.IndexOf("[文件:", idx);
                if (start < 0)
                {
                    sb.Append(text.Substring(idx));
                    break;
                }
                sb.Append(text.Substring(idx, start - idx));
                int end = text.IndexOf(']', start + 4);
                if (end < 0)
                {
                    sb.Append(text.Substring(start));
                    break;
                }
                string path = text.Substring(start + 4, end - start - 4).Trim();
                if (path.Length > 0)
                {
                    files.Add(path);
                }
                idx = end + 1;
            }
            cleanText = sb.ToString().Trim();
            return files;
        }

        /// <summary>切分上限——单条字符数（社区经验 ~2000，留余量）</summary>
        private const int MaxChunkChars = 1800;

        /// <summary>即时转发上限——3+1 预算：≤3 条即时转发，第 4 次轮末汇总（对齐官方被动回复 4 次上限）</summary>
        private const int MaxImmediateReplies = 3;
        /// <summary>QQ 富文本 faceType 标记正则——表情/大表情图片标记（v0.96.1 简化）</summary>
        private static readonly Regex _faceTagRegex = new Regex("<faceType=[^>]*>", RegexOptions.Compiled);
        /// <summary>转发态文件路径——入口壳注入（A33：Data/qq-forward.json；空=转发态不落盘）</summary>
        private static string _statePath = "";
        /// <summary>转发态加载标志——首帧懒加载（此时前文/视图已恢复，避免读到半成品块数）</summary>
        private static bool _stateLoaded = false;
        /// <summary>截断文本——日志展示</summary>
        private static string Truncate(string s, int max)
        {
            if (s.Length <= max)
            {
                return s;
            }
            return s.Substring(0, max);
        }
        /// <summary>
        /// QQ 服务事件——WS 线程入队 / 主线程 Tick 事件泵消费（R6-P1-01 单线程化）。
        /// Kind：source=注入来源入队（Source 有效） / reset=轮状态重置。
        /// </summary>
        private sealed class QqServiceEvent
        {
            /// <summary>事件类型——source / reset</summary>
            public string Kind;

            /// <summary>猫标识——majordomo / cat id（来源队列键）</summary>
            public string CatKey;

            /// <summary>来源——Kind=source 时有效</summary>
            public QqSource Source;
        }
    }

    /// <summary>
    /// QQ 消息来源——结构化载荷（P8：msg_id 被动回复扩展）。
    /// </summary>
    internal sealed class QqSource
    {
        /// <summary>消息类型——private / group</summary>
        public string Type;

        /// <summary>目标 ID——私聊 user_openid / 群聊 gid:mid</summary>
        public string TargetId;

        /// <summary>被动回复引用——事件 d.id；空=非被动</summary>
        public string MsgId;

        /// <summary>发送者昵称——群@ author.username；私聊事件为空串（私聊 header 硬编码雾理莎）</summary>
        public string DisplayName;

        /// <summary>群内角色——owner/admin/member（事件白拿字段）；私聊空</summary>
        public string Role;

        /// <summary>构造来源。</summary>
        /// <param name="type">消息类型</param>
        /// <param name="targetId">目标 ID</param>
        /// <param name="msgId">被动回复 msg_id</param>
        public QqSource(string type, string targetId, string msgId, string displayName = "", string role = "")
        {
            Type = type;
            TargetId = targetId;
            MsgId = msgId;
            DisplayName = displayName;
            Role = role;
        }

        /// <summary>显示——private:uid / group:gid:mid（日志兼容）</summary>
        /// <returns>来源文本</returns>
        public override string ToString()
        {
            return Type + ":" + TargetId;
        }
    }

    /// <summary>
    /// QQ 附件——事件 attachments[] 载荷（P9 接收面）。
    /// </summary>
    internal sealed class QqAttachment
    {
        /// <summary>下载 URL——带时效签名（rkey），收到后尽快下载</summary>
        public string Url;

        /// <summary>文件名</summary>
        public string FileName;

        /// <summary>本地缓存路径——下载成功后写回；原位嵌入用（v0.96.1）</summary>
        public string LocalPath;

        /// <summary>大小（字节）</summary>
        public long Size;

        /// <summary>内容类型（image/jpeg / video/mp4 / voice / file 等）</summary>
        public string ContentType;
    }

    /// <summary>
    /// qqbot 绑定目标——默认猫 / 多猫统一抽象（路由注入面 + 输出转发面）。
    /// </summary>
    public sealed class QqTarget
    {
        /// <summary>猫标识——majordomo / cat id（来源队列键）</summary>
        public string Key;

        /// <summary>显示名——输出转发【Cat名：】前缀</summary>
        public string DisplayName;

        /// <summary>绑定的 qqbot 配置身份——Guid.Empty=未绑定</summary>
        public Guid QqBotId;

        /// <summary>启用标志——false=不注入不转发</summary>
        public bool Enable;

        /// <summary>注入回调——PostUserMessage</summary>
        public Action<string> Inject;

        /// <summary>视图块流读取——游标增量轮询（QQBot 转发数据源：text=转发候选 / roundsum=轮结束哨兵）</summary>
        public Func<QqViewItem[]> GetViewItems;

        /// <summary>会话空闲判定——异常中止轮残留来源兜底清理（null=永不空闲）</summary>
        public Func<bool> IsIdle;

        /// <summary>新会话桥——触发 session.new（异步置位）+ 返回注入摘要文本（Q5 /new 指令；null=不支持）</summary>
        public Func<string> NewSession;

        /// <summary>会话信息桥——猫 info 工具文本（Q5 /info 指令合并；null=不支持）</summary>
        public Func<string> GetInfo;
    }

    /// <summary>
    /// QQ 转发视图项——视图块窄 DTO（转发面只消费 RenderType + Content；装配侧从 SessionViewStore 转换——解耦 QQ 域与视图层内部结构）。
    /// </summary>
    public sealed class QqViewItem
    {
        /// <summary>渲染类型——text=转发候选 / roundsum=轮结束哨兵 / 其他跳过</summary>
        public string RenderType;

        /// <summary>文本内容——text 块内容（已解析 payload.content）；其他类型空串</summary>
        public string Content;
    }
}
