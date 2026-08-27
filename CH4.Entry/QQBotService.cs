using System;
using System.Collections.Generic;
using System.Text.Json;
using Mau.Runtime;

namespace CH4
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

        /// <summary>注入来源队列——猫 Key → 来源 FIFO（注入时入队，转发时出队）</summary>
        private static readonly Dictionary<string, Queue<string>> _sourceQueues = new Dictionary<string, Queue<string>>();

        /// <summary>最后来源——猫 Key → 最近一次 QQ 交互来源（无条件转发回退目标：前端对话回复也转发）</summary>
        private static readonly Dictionary<string, string> _lastSources = new Dictionary<string, string>();

        /// <summary>启动标志——防重复 Start</summary>
        private static bool _started = false;

        /// <summary>启动时刻——/info 运行时长统计</summary>
        private static DateTime _startTime = DateTime.Now;

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
            string source = "";
            string text = "";
            if (!TryParseMessage(raw, out t, out source, out text))
            {
                return;
            }
            if (text.Length == 0)
            {
                return;
            }
            LogStore.Add("QQBot", 1, "收: " + t + " | " + source + " → " + Truncate(text, 30), "QQBOT");
            // 强匹配指令——不走 LLM 路由，代码直执（R2.3.8）
            string cmdReply = HandleSlashCommand(text);
            if (cmdReply != null)
            {
                SendToSource(qqBotId, source, cmdReply);
                return;
            }
            // 查绑定该 Bot 的猫（默认猫 + 多猫）
            List<QqTarget> targets = Program.CollectQqTargets(qqBotId);
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
                tg.Inject("[来自QQ] " + text);
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
        /// <returns>解析成功 true</returns>
        private static bool TryParseMessage(string raw, out string t, out string source, out string text)
        {
            t = "";
            source = "";
            text = "";
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
                        source = "private:" + uid;
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
                        source = "group:" + gid + ":" + mid;
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
        /// 发送回复到来源——通过 Bot 连接（source 格式 private:uid / group:gid:mid）。
        /// </summary>
        /// <param name="qqBotId">Bot 配置身份</param>
        /// <param name="source">来源</param>
        /// <param name="text">回复文本</param>
        private static void SendToSource(Guid qqBotId, string source, string text)
        {
            QQBotConnection conn;
            if (!_connections.TryGetValue(qqBotId, out conn))
            {
                return;
            }
            int ci = source.IndexOf(':');
            string type = ci > 0 ? source.Substring(0, ci) : source;
            string target = ci > 0 ? source.Substring(ci + 1) : source;
            conn.SendReply(type, target, text);
        }

        /// <summary>
        /// 注入来源入队——猫 Key → 来源 FIFO（R2.3.5 输出转发消费）。
        /// </summary>
        /// <param name="catKey">猫标识（majordomo / cat id）</param>
        /// <param name="source">来源</param>
        private static void EnqueueSource(string catKey, string source)
        {
            Queue<string> q;
            if (!_sourceQueues.TryGetValue(catKey, out q))
            {
                q = new Queue<string>();
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
        internal static bool TryDequeueSource(string catKey, out string source)
        {
            Queue<string> q;
            if (_sourceQueues.TryGetValue(catKey, out q) && q.Count > 0)
            {
                source = q.Dequeue();
                return true;
            }
            source = "";
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
            List<QqTarget> targets = Program.CollectAllQqTargets();
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
                            string source;
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
        /// 转发回复到来源——【Cat名：】前缀格式。
        /// </summary>
        /// <param name="tg">绑定目标</param>
        /// <param name="source">来源（private:uid / group:gid:mid）</param>
        /// <param name="text">回复文本</param>
        private static void SendReplyToTarget(QqTarget tg, string source, string text)
        {
            QQBotConnection conn;
            if (!_connections.TryGetValue(tg.QqBotId, out conn))
            {
                return;
            }
            int ci = source.IndexOf(':');
            string type = ci > 0 ? source.Substring(0, ci) : source;
            string target = ci > 0 ? source.Substring(ci + 1) : source;
            string formatted = tg.DisplayName + "：\n" + text;
            conn.SendReply(type, target, formatted);
        }

        /// <summary>截断文本——日志展示</summary>
        private static string Truncate(string s, int max)
        {
            return s.Length <= max ? s : s.Substring(0, max);
        }
    }

    /// <summary>
    /// qqbot 绑定目标——默认猫 / 多猫统一抽象（路由注入面 + 输出转发面）。
    /// </summary>
    internal sealed class QqTarget
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
