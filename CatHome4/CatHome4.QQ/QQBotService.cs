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
                            delegate(string raw) { OnMessage(c.QqBotId, raw); });
                        _connections[c.QqBotId] = conn;
                        conn.Start();
                        LogStore.Add("QQBot", 1, "配置变更重建连接 | " + c.DisplayName + "（sandbox=" + (c.Sandbox ? "true" : "false") + "）", "QQBOT");
                    }
                    continue;
                }
                string newSecret = store.GetSecret(c.QqBotId);
                QQBotConnection newConn = new QQBotConnection(
                    c.QqBotId, c.DisplayName, c.AppId, newSecret, c.Sandbox,
                    delegate(string raw) { OnMessage(c.QqBotId, raw); });
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
            LogStore.Add("QQBot", 1, "收: " + t + " | " + source + " → " + Truncate(text, 30) + attachText, "QQBOT");
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
            bool anyInjected = false;
            for (int i = 0; i < targets.Count; i++)
            {
                QqTarget tg = targets[i];
                if (!tg.Enable)
                {
                    continue;
                }
                tg.Inject("[来自QQ]" + BuildHeader(source) + " " + text + attachText);
                EnqueueSource(tg.Key, source);
                ResetRound(tg.Key);
                anyInjected = true;
            }
            if (!anyInjected)
            {
                SendToSource(qqBotId, source, "目标 Cat 未启用 qqbot 转发功能");
            }
        }/// <summary>
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
        /// 归一化 QQ 富文本 faceType 标记——&lt;faceType=X,...&gt; → [本地缓存路径] 原位嵌入。
        /// 多图嵌长文本可区分图片与文字的对应关系；agent 直接可调 image-analyze（v0.96.1）。
        /// </summary>
        /// <param name="content">原始内容（已剥离 @ 前缀）</param>
        /// <param name="attachments">附件列表——按序对应 faceType 标记；降级链：本地路径→文件名→[图片]</param>
        /// <returns>归一化后的文本</returns>
        private static string NormalizeFaceTags(string content, List<QqAttachment> attachments)
        {
            if (string.IsNullOrEmpty(content))
            {
                return content ?? "";
            }
            int idx = 0;
            return _faceTagRegex.Replace(content, m =>
            {
                // 原位嵌入真实本地路径——agent 直接可调 image-analyze，多图嵌长文本可区分对应关系（v0.96.1）
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
                idx++;
                return string.IsNullOrEmpty(mark) ? "[图片]" : "[" + mark + "]";
            });
        }
        /// <summary>
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
            if (text.StartsWith("/"))
            {
                return "未知指令: " + text + "（可用: /ping /info /new）";
            }
            return null;
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
            sb.Append("【QQ 管理器状态】\n运行时长: " + upText + "\n当前 qqbot 数: " + _connections.Count + "\n可用指令: /ping /info /new");
            // Q5——合并绑定猫的 info（直接调用工具函数，不经 LLM/OA）
            if (_collector != null)
            {
                List<QqTarget> targets = _collector.CollectByBot(qqBotId);
                for (int i = 0; i < targets.Count; i = i + 1)
                {
                    QqTarget tg = targets[i];
                    if (tg.GetInfo != null)
                    {
                        sb.Append("\n\n【猫: " + tg.DisplayName + "】\n" + tg.GetInfo());
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
            // 指令回复/错误提示——MD 通道（Q4 全 MD），携带被动 msg_id
            conn.SendReply(source.Type, source.TargetId, text, source.MsgId, true);
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
        }        /// <summary>
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
        }        /// <summary>
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
        }/// <summary>游标——猫 Key → 已处理消息数（R2.3.5 只转发启用后新块）</summary>
        private static readonly Dictionary<string, int> _cursors = new Dictionary<string, int>();

        /// <summary>输出转发轮询——主线程每帧调用（宿主主循环接入）。视图块游标增量：text 块 → 即时转发 ≤3 / 超限累计；roundsum 块 → 轮结束哨兵（第 4 次汇总转发 + 出队来源 + 重置）。无 qqbot 来源（前端对话）不转发——被动机制。</summary>
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
                        ClearRoundState(tg.Key);
                    }
                    continue;
                }
                for (int j = cursor; j < count; j++)
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
            }
        }        /// <summary>读取游标——缺省 0</summary>
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
                // 3+1 预算——每条转发单次 API 调用（整轮 ≤4 不超被动回复上限）；长文截断
                string piece = tg.DisplayName + "：\n" + Truncate(text, MaxChunkChars);
                conn.SendReply("private", source.TargetId, piece, source.MsgId, true);
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

        /// <summary>即时转发上限——3+1 预算：≤3 条即时转发，第 4 次轮末汇总（对齐官方被动回复 4 次上限）</summary>
        private const int MaxImmediateReplies = 3;
        /// <summary>QQ 富文本 faceType 标记正则——表情/大表情图片标记（v0.96.1 简化）</summary>
        private static readonly Regex _faceTagRegex = new Regex("<faceType=[^>]*>", RegexOptions.Compiled);
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
