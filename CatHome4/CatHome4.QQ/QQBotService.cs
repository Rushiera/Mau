using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
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

        /// <summary>注入来源队列——猫 Key → QqSource FIFO（注入时入队，转发时出队）</summary>
        private static readonly Dictionary<string, Queue<QqSource>> _sourceQueues = new Dictionary<string, Queue<QqSource>>();

        /// <summary>最后来源——猫 Key → 最近一次 QQ 交互来源（无条件转发回退目标：前端对话回复也转发）</summary>
        private static readonly Dictionary<string, QqSource> _lastSources = new Dictionary<string, QqSource>();

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
            _fileCacheRoot = root == null ? "" : root;
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
        }

        /// <summary>
        /// 停止管理器——停止全部连接并清空。
        /// </summary>
        public static void Stop()
        {
            _started = false;
            List<Guid> keys = new List<Guid>(_connections.Keys);
            for (int i = 0; i < keys.Count; i++)
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
            for (int i = 0; i < configs.Length; i++)
            {
                CH_QqBotConfig c = configs[i];
                active.Add(c.QqBotId);
                if (!_connections.ContainsKey(c.QqBotId))
                {
                    string secret = store.GetSecret(c.QqBotId);
                    QQBotConnection conn = new QQBotConnection(
                        c.QqBotId, c.DisplayName, c.AppId, secret, c.Sandbox,
                        delegate(string raw) { OnMessage(c.QqBotId, raw); });
                    _connections[c.QqBotId] = conn;
                    conn.Start();
                }
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
            for (int i = 0; i < toRemove.Count; i++)
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
            // P9 附件——私聊先下载缓存（URL 时效短——立即下载；注入文本带缓存路径）
            string attachText = "";
            if (source.Type == "private" && attachments != null && attachments.Count > 0)
            {
                attachText = DownloadAttachments(qqBotId, attachments);
            }
            if (text.Length == 0 && attachText.Length == 0)
            {
                return;
            }
            LogStore.Add("QQBot", 1, "收: " + t + " | " + source + " → " + Truncate(text, 30) + attachText, "QQBOT");
            // 强匹配指令——不走 LLM 路由，代码直执（R2.3.8）
            string cmdReply = HandleSlashCommand(text);
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
            // 广播注入所有绑定且启用的 Cat
            bool anyInjected = false;
            for (int i = 0; i < targets.Count; i++)
            {
                QqTarget tg = targets[i];
                if (!tg.Enable)
                {
                    continue;
                }
                tg.Inject("[来自QQ] " + text + attachText);
                EnqueueSource(tg.Key, source);
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
                        string uid = dd.GetProperty("author").GetProperty("user_openid").GetString() ?? "";
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
                        source = new QqSource("private", uid, msgId);
                        text = StripAtMention(content);
                        return true;
                    }
                    if (t == "GROUP_AT_MESSAGE_CREATE")
                    {
                        JsonElement dd = d.RootElement.GetProperty("d");
                        string gid = dd.GetProperty("group_openid").GetString() ?? "";
                        string mid = dd.GetProperty("author").GetProperty("member_openid").GetString() ?? "";
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
                        source = new QqSource("group", gid + ":" + mid, msgId);
                        text = StripAtMention(content);
                        return true;
                    }
                }
            }
            catch (Exception)
            {
            }
            return false;
        }

        /// <summary>附件大小硬限制——200MB（官方硬限制）</summary>
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
                    sb.Append(" [附件: " + a.FileName + "|" + a.Size + "|" + dest + "]");
                    LogStore.Add("QQBot", 1, "附件缓存 | " + dest + " | " + a.Size, "QQBOT");
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
            return result.Count > 0 ? result : null;
        }

        /// <summary>文件名净化——去非法字符（落盘安全）</summary>
        private static string SanitizeFileName(string name)
        {
            string s = name == null ? "file" : name;
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
                sb.Append(bad ? '_' : s[i]);
            }
            string result = sb.ToString().Trim();
            return result.Length == 0 ? "file" : result;
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
            string s = content == null ? "" : content.Trim();
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
        /// 强匹配指令处理——/ 开头指令完全一致才命中；命中返回回复文本，未命中返回 null（走 LLM 路由）。
        /// </summary>
        /// <param name="text">消息文本</param>
        /// <returns>指令回复；非指令 null</returns>
        private static string HandleSlashCommand(string text)
        {
            if (text == "/ping")
            {
                return "pong";
            }
            if (text == "/info")
            {
                return BuildInfoText();
            }
            if (text.StartsWith("/"))
            {
                return "未知指令: " + text + "（可用: /ping /info）";
            }
            return null;
        }

        /// <summary>
        /// 构建 /info 状态文本——运行总时长 + 当前 qqbot 数 + 可用指令。
        /// </summary>
        /// <returns>状态文本</returns>
        private static string BuildInfoText()
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
            return "【QQ 管理器状态】\n运行时长: " + upText + "\n当前 qqbot 数: " + _connections.Count + "\n可用指令: /ping /info";
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
            // 指令回复/错误提示——纯文本通道（无 MD 检测），携带被动 msg_id
            conn.SendReply(source.Type, source.TargetId, text, source.MsgId, false);
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
            _lastSources[catKey] = source;
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

        /// <summary>游标——猫 Key → 已处理消息数（R2.3.5 只转发启用后新块）</summary>
        private static readonly Dictionary<string, int> _cursors = new Dictionary<string, int>();

        /// <summary>
        /// 输出转发轮询——主线程每帧调用（宿主主循环接入）。
        /// 游标增量：只扫描游标后新增消息；回复块（Assistant Content 非空）→ 出队来源 → 【Cat名：】转发。
        /// 未启用猫只推进游标不转发（启用时只转发启用后的新块）。
        /// </summary>
        public static void Tick()
        {
            if (!_started)
            {
                return;
            }
            List<QqTarget> targets = new List<QqTarget>();
            if (_collector != null)
            {
                targets = _collector.CollectAll();
            }
            for (int i = 0; i < targets.Count; i++)
            {
                QqTarget tg = targets[i];
                int count = tg.GetMessageCount();
                int cursor = GetCursor(tg.Key);
                if (count <= cursor)
                {
                    continue;
                }
                if (tg.Enable)
                {
                    LlmMessage[] msgs = tg.GetMessages();
                    for (int j = cursor; j < count && j < msgs.Length; j++)
                    {
                        LlmMessage m = msgs[j];
                        if (m.Role == LlmRole.Assistant && m.Content != null && m.Content.Length > 0)
                        {
                            // 无条件转发——出队来源优先（qqbot 注入对话），否则回退最后来源（前端对话也转发）
                            QqSource source;
                            if (TryDequeueSource(tg.Key, out source))
                            {
                                _lastSources[tg.Key] = source;
                            }
                            else if (!_lastSources.TryGetValue(tg.Key, out source))
                            {
                                continue;
                            }
                            SendReplyToTarget(tg, source, m.Content);
                        }
                    }
                }
                _cursors[tg.Key] = count;
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
            // 群聊保持现状——msg_type=0 直发（携带被动 msg_id）
            string formatted = tg.DisplayName + "：\n" + text;
            conn.SendReply(source.Type, source.TargetId, formatted, source.MsgId, false);
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
                bool isMarkdown = IsMarkdownText(text);
                List<string> chunks = ChunkForSending(text);
                for (int i = 0; i < chunks.Count; i = i + 1)
                {
                    // 【Cat名：】前缀只加第一条（计入长度预算）
                    string piece = i == 0 ? tg.DisplayName + "：\n" + chunks[i] : chunks[i];
                    if (!conn.SendReply("private", source.TargetId, piece, source.MsgId, isMarkdown))
                    {
                        return;
                    }
                }
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

        /// <summary>私聊被动回复条数上限——官方每条消息最多回复 4 次</summary>
        private const int MaxPassiveReplies = 4;

        /// <summary>
        /// MD 检测——行级扫描（P8 §二 规则）：标题/列表/引用/代码块/加粗/删除线/链接任一命中即 MD。
        /// </summary>
        /// <param name="text">消息文本</param>
        /// <returns>true=走 Markdown 通道</returns>
        private static bool IsMarkdownText(string text)
        {
            if (text == null || text.Length == 0)
            {
                return false;
            }
            string[] lines = text.Split('\n');
            for (int i = 0; i < lines.Length; i = i + 1)
            {
                string line = lines[i].TrimStart();
                if (line.Length == 0)
                {
                    continue;
                }
                // 标题——# 后跟空格
                if (line[0] == '#' && line.Length > 1 && line[1] == ' ')
                {
                    return true;
                }
                // 列表——- / * 开头或有序数字. 开头
                if (line.Length > 1 && (line[0] == '-' || line[0] == '*') && line[1] == ' ')
                {
                    return true;
                }
                if (IsOrderedList(line))
                {
                    return true;
                }
                // 引用 / 代码块
                if (line[0] == '>' || line.StartsWith("```"))
                {
                    return true;
                }
                // 加粗 / 删除线 / 链接
                if (line.Contains("**") || line.Contains("~~") || ContainsLink(line))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>有序列表检测——数字 + 点 + 空格开头</summary>
        private static bool IsOrderedList(string line)
        {
            int i = 0;
            while (i < line.Length && line[i] >= '0' && line[i] <= '9')
            {
                i = i + 1;
            }
            if (i == 0 || i + 1 >= line.Length)
            {
                return false;
            }
            return line[i] == '.' && line[i + 1] == ' ';
        }

        /// <summary>链接检测——[text](url) 形态</summary>
        private static bool ContainsLink(string line)
        {
            int lb = line.IndexOf('[');
            if (lb < 0)
            {
                return false;
            }
            int rp = line.IndexOf("](", lb);
            return rp > lb;
        }

        /// <summary>
        /// 按标题分块——标题行归属其后内容块；首个标题前文本（前言）独立成块；无标题整篇一块。
        /// </summary>
        /// <param name="text">消息文本</param>
        /// <returns>块列表（非空）</returns>
        private static List<string> SplitByHeading(string text)
        {
            List<string> blocks = new List<string>();
            string[] lines = text.Split('\n');
            StringBuilder cur = new StringBuilder();
            for (int i = 0; i < lines.Length; i = i + 1)
            {
                string line = lines[i];
                if (line.EndsWith("\r"))
                {
                    line = line.Substring(0, line.Length - 1);
                }
                string trimmed = line.TrimStart();
                bool isHeading = trimmed.Length > 1 && trimmed[0] == '#' && trimmed[1] == ' ';
                if (isHeading && cur.Length > 0)
                {
                    blocks.Add(cur.ToString().TrimEnd());
                    cur.Length = 0;
                }
                if (cur.Length > 0)
                {
                    cur.Append('\n');
                }
                cur.Append(line);
            }
            if (cur.Length > 0)
            {
                blocks.Add(cur.ToString().TrimEnd());
            }
            return blocks;
        }

        /// <summary>
        /// 切分发送块——标题分块贪心打包（N 块超 M → N-1 块发送）+ 单块退化 + 被动条数上限。
        /// </summary>
        /// <param name="text">消息文本（非空）</param>
        /// <returns>发送块列表（≤4 条）</returns>
        private static List<string> ChunkForSending(string text)
        {
            List<string> blocks = SplitByHeading(text);
            List<string> chunks = new List<string>();
            StringBuilder buf = new StringBuilder();
            for (int i = 0; i < blocks.Count; i = i + 1)
            {
                string block = blocks[i];
                // 单块超限——块内退化切分（段落→行→硬切）
                if (block.Length > MaxChunkChars)
                {
                    if (buf.Length > 0)
                    {
                        chunks.Add(buf.ToString().TrimEnd());
                        buf.Length = 0;
                    }
                    List<string> parts = SplitOversized(block);
                    for (int j = 0; j < parts.Count; j = j + 1)
                    {
                        chunks.Add(parts[j]);
                    }
                    continue;
                }
                if (buf.Length + block.Length > MaxChunkChars)
                {
                    if (buf.Length > 0)
                    {
                        chunks.Add(buf.ToString().TrimEnd());
                        buf.Length = 0;
                    }
                }
                if (buf.Length > 0)
                {
                    buf.Append('\n');
                }
                buf.Append(block);
            }
            if (buf.Length > 0)
            {
                chunks.Add(buf.ToString().TrimEnd());
            }
            // 被动上限——私聊最多 4 条；超出截断留痕
            if (chunks.Count > MaxPassiveReplies)
            {
                LogStore.Add("QQBot", 2, "回复超" + MaxPassiveReplies + "条截断 | 总块数" + chunks.Count, "QQBOT");
                chunks.RemoveRange(MaxPassiveReplies, chunks.Count - MaxPassiveReplies);
            }
            return chunks;
        }

        /// <summary>
        /// 单块超限退化——按空行分段 → 行硬切 → 字符硬切。
        /// </summary>
        /// <param name="block">超限块</param>
        /// <returns>切分段列表</returns>
        private static List<string> SplitOversized(string block)
        {
            List<string> result = new List<string>();
            string[] paras = block.Split(new string[] { "\n\n" }, StringSplitOptions.None);
            StringBuilder buf = new StringBuilder();
            for (int i = 0; i < paras.Length; i = i + 1)
            {
                if (buf.Length + paras[i].Length > MaxChunkChars)
                {
                    if (buf.Length > 0)
                    {
                        result.Add(buf.ToString().TrimEnd());
                        buf.Length = 0;
                    }
                    if (paras[i].Length > MaxChunkChars)
                    {
                        // 行级硬切
                        string[] lines = paras[i].Split('\n');
                        StringBuilder lb = new StringBuilder();
                        for (int j = 0; j < lines.Length; j = j + 1)
                        {
                            string line = lines[j];
                            if (line.EndsWith("\r"))
                            {
                                line = line.Substring(0, line.Length - 1);
                            }
                            if (lb.Length + line.Length > MaxChunkChars)
                            {
                                if (lb.Length > 0)
                                {
                                    result.Add(lb.ToString().TrimEnd());
                                    lb.Length = 0;
                                }
                                if (line.Length > MaxChunkChars)
                                {
                                    // 字符硬切
                                    string rest = line;
                                    while (rest.Length > MaxChunkChars)
                                    {
                                        result.Add(rest.Substring(0, MaxChunkChars));
                                        rest = rest.Substring(MaxChunkChars);
                                    }
                                    if (rest.Length > 0)
                                    {
                                        lb.Append(rest);
                                    }
                                    continue;
                                }
                            }
                            if (lb.Length > 0)
                            {
                                lb.Append('\n');
                            }
                            lb.Append(line);
                        }
                        if (lb.Length > 0)
                        {
                            result.Add(lb.ToString().TrimEnd());
                        }
                        continue;
                    }
                }
                if (buf.Length > 0)
                {
                    buf.Append("\n\n");
                }
                buf.Append(paras[i]);
            }
            if (buf.Length > 0)
            {
                result.Add(buf.ToString().TrimEnd());
            }
            return result;
        }

        /// <summary>截断文本——日志展示</summary>
        private static string Truncate(string s, int max)
        {
            return s.Length <= max ? s : s.Substring(0, max);
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

        /// <summary>构造来源。</summary>
        /// <param name="type">消息类型</param>
        /// <param name="targetId">目标 ID</param>
        /// <param name="msgId">被动回复 msg_id</param>
        public QqSource(string type, string targetId, string msgId)
        {
            Type = type;
            TargetId = targetId;
            MsgId = msgId;
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

        /// <summary>消息数读取——游标增量轮询</summary>
        public Func<int> GetMessageCount;

        /// <summary>消息历史读取——回复块扫描</summary>
        public Func<LlmMessage[]> GetMessages;
    }
}
