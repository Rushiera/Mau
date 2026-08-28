using System;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using Mau.Runtime;

namespace CatHome4.QQ
{
    /// <summary>
    /// 单 QQ Bot WS 连接——CH1 CatQQBot.cs 协议资产移植（R2.3.3）。
    /// 鉴权（appId+secret → access_token）/ WS 全 opcode / 心跳 ACK 超时检测 / 重连+Resume。
    /// 消息到达经 OnMessage 回调管理器（路由层）；发送经 SendReply（管理器调用）。
    ///
    /// 【QQ Bot WebSocket 协议（核心 opcode，2026 官方文档校验）】
    ///   op=0  Dispatch — 服务端推送事件（含 t 事件类型 + s 序列号）
    ///   op=1  Heartbeat — 客户端心跳，d 字段携带最后处理的 s 值
    ///   op=2  Identify — 首次鉴权 / 全新连接
    ///   op=6  Resume — 断线重连恢复会话（携带 session_id + seq）
    ///   op=7  Reconnect — 服务端要求重连
    ///   op=9  Invalid Session — 鉴权/resume 参数有误（d.code: 4006=无效session, 4009=连接过期可重试）
    ///   op=10 Hello — 连接建立后第一条消息（heartbeat_interval + session_id）
    ///   op=11 Heartbeat ACK — 服务端对心跳的回应，不触发重连
    ///
    /// 【重连+Resume 策略】
    ///   断线 → 连接 WS → Hello → 若有 _sessionId:
    ///     发 op=6 Resume(token + session_id + seq)
    ///       → 成功: 收到 op=0 t=RESUMED → 正常收发
    ///       → 失败4006: 清 _sessionId → 同连接内回落 op=2 Identify
    ///       → 失败4009: 重试 op=6（连接过期但可恢复）
    ///     若无 _sessionId / resume 失败:
    ///       发 op=2 Identify → 收到 op=0 t=READY → 正常收发
    ///
    /// 【心跳 ACK 超时检测】
    ///   连续 2 次心跳未收到 op=11 → HbLoop 主动 Dispose WebSocket → 触发重连。
    ///   避免 TCP 半开连接时 ReceiveAsync 超时前（~92s）的长盲区。
    /// </summary>
    internal sealed class QQBotConnection
    {
        // op=9 错误码
        private const int ERR_INVALID_SESSION = 4006;  // session_id 无效，须全新鉴权
        private const int ERR_EXPIRED = 4009;          // 连接过期，可重试 resume

        // 心跳 ACK 超时：连续多少次无 ACK 后触发重连
        private const int MaxMissedAcks = 2;

        /// <summary>Bot 配置身份——Bot 池条目引用</summary>
        private readonly Guid _qqBotId;

        /// <summary>Bot 显示名——日志标识</summary>
        private readonly string _displayName;

        /// <summary>QQ 开放平台 Bot 应用 ID</summary>
        private readonly string _appId;

        /// <summary>Bot 应用 Secret——Bot 池 secrets 读取</summary>
        private readonly string _secret;

        /// <summary>沙箱标志——true=沙箱端点 / false=正式端点</summary>
        private readonly bool _sandbox;

        /// <summary>消息到达回调——管理器路由层（raw JSON）</summary>
        private readonly Action<string> _onMessage;

        /// <summary>API 主机——沙箱/正式双端点</summary>
        private readonly string _apiHost;

        /// <summary>访问令牌——鉴权产物</summary>
        private string _accessToken = "";

        /// <summary>令牌过期时刻——Stopwatch 时间戳</summary>
        private long _tokenExpireTick = 0;

        /// <summary>WS 连接实例</summary>
        private ClientWebSocket _ws = null;

        /// <summary>WS 线程——后台常驻</summary>
        private Thread _wsThread = null;

        /// <summary>WS 运行标志——Stop 置 false 退出循环</summary>
        private bool _wsRun = false;

        /// <summary>会话恢复——session_id</summary>
        private string _sessionId = "";

        /// <summary>会话恢复——最后处理序列号</summary>
        private long _lastSeq = 0;

        /// <summary>重连计数——日志标识</summary>
        private int _reconnectCount = 0;

        /// <summary>心跳 ACK 追踪——HbLoop 发送后递增，WsLoop 收到 op=11 时清零</summary>
        private int _missedAcks = 0;

        /// <summary>
        /// 构造单 Bot 连接。
        /// </summary>
        /// <param name="qqBotId">Bot 配置身份</param>
        /// <param name="displayName">Bot 显示名</param>
        /// <param name="appId">QQ 开放平台 Bot 应用 ID</param>
        /// <param name="secret">Bot 应用 Secret</param>
        /// <param name="sandbox">沙箱标志</param>
        /// <param name="onMessage">消息到达回调（raw JSON）</param>
        public QQBotConnection(Guid qqBotId, string displayName, string appId, string secret, bool sandbox, Action<string> onMessage)
        {
            _qqBotId = qqBotId;
            _displayName = displayName;
            _appId = appId;
            _secret = secret;
            _sandbox = sandbox;
            _onMessage = onMessage;
            _apiHost = _sandbox
                ? "https://sandbox.api.sgroup.qq.com"
                : "https://api.sgroup.qq.com";
        }

        /// <summary>
        /// 启动连接——刷新 Token + 启动 WS 线程。
        /// </summary>
        public void Start()
        {
            if (_wsRun)
            {
                return;
            }
            RefreshToken();
            _wsRun = true;
            _wsThread = new Thread(WsLoop);
            _wsThread.IsBackground = true;
            _wsThread.Start();
            Log("QQBot | " + _displayName + " | 启动 " + (_sandbox ? "[沙箱]" : "[正式]") + " AppID=" + Truncate(_appId, 8), 1);
        }

        /// <summary>
        /// 停止连接——置运行标志 false + 关闭 WS。
        /// </summary>
        public void Stop()
        {
            _wsRun = false;
            try
            {
                if (_ws != null)
                {
                    _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None).GetAwaiter().GetResult();
                }
            }
            catch (Exception)
            {
            }
            try
            {
                if (_ws != null)
                {
                    _ws.Dispose();
                }
            }
            catch (Exception)
            {
            }
            _ws = null;
            Log("QQBot | " + _displayName + " | 已停止", 1);
        }

        /// <summary>
        /// 发送回复——私聊 / 群聊（管理器调用；401 自动刷新重试一次）。
        /// </summary>
        /// <param name="msgType">消息类型——private / group</param>
        /// <param name="targetId">目标 ID（群聊含 gid:mid 段）</param>
        /// <param name="text">回复文本</param>
        public void SendReply(string msgType, string targetId, string text)
        {
            try
            {
                using (System.Net.Http.HttpClient h = new System.Net.Http.HttpClient())
                {
                    h.DefaultRequestHeaders.Add("Authorization", "QQBot " + _accessToken);
                    string url;
                    string json;
                    if (msgType == "private")
                    {
                        url = _apiHost + "/v2/users/" + targetId + "/messages";
                        json = "{\"content\":\"" + EscapeJson(text) + "\",\"msg_type\":0}";
                    }
                    else
                    {
                        int ci = targetId.IndexOf(':');
                        string gid = ci > 0 ? targetId.Substring(0, ci) : targetId;
                        url = _apiHost + "/v2/groups/" + gid + "/messages";
                        json = "{\"content\":\"" + EscapeJson(text) + "\",\"msg_type\":0}";
                    }
                    System.Net.Http.StringContent c = new System.Net.Http.StringContent(json, Encoding.UTF8, "application/json");
                    System.Net.Http.HttpResponseMessage r = h.PostAsync(url, c).GetAwaiter().GetResult();
                    if ((int)r.StatusCode == 401)
                    {
                        RefreshToken();
                        h.DefaultRequestHeaders.Remove("Authorization");
                        h.DefaultRequestHeaders.Add("Authorization", "QQBot " + _accessToken);
                        c = new System.Net.Http.StringContent(json, Encoding.UTF8, "application/json");
                        r = h.PostAsync(url, c).GetAwaiter().GetResult();
                    }
                    int status = (int)r.StatusCode;
                    if (status >= 300)
                    {
                        // 转发失败 L2 留痕——内部 err 可见（R2.3.5 失败语义）
                        Log("QQBot | " + _displayName + " | 发: " + msgType + ":" + targetId + " ← " + Truncate(text, 30) + " (" + status + ")", 2);
                    }
                    else
                    {
                        Log("QQBot | " + _displayName + " | 发: " + msgType + ":" + targetId + " ← " + Truncate(text, 30) + " (" + status + ")", 1);
                    }
                }
            }
            catch (Exception e)
            {
                Log("QQBot | " + _displayName + " | 发送失败: " + e.Message, 2);
            }
        }

        /// <summary>
        /// 刷新访问令牌——POST bots.qq.com/app/getAppAccessToken（appId + clientSecret → access_token + expires_in）。
        /// </summary>
        private void RefreshToken()
        {
            try
            {
                using (System.Net.Http.HttpClient h = new System.Net.Http.HttpClient())
                {
                    string body = "{\"appId\":\"" + EscapeJson(_appId)
                        + "\",\"clientSecret\":\"" + EscapeJson(_secret) + "\"}";
                    System.Net.Http.StringContent c = new System.Net.Http.StringContent(body, Encoding.UTF8, "application/json");
                    System.Net.Http.HttpResponseMessage r = h.PostAsync("https://bots.qq.com/app/getAppAccessToken", c).GetAwaiter().GetResult();
                    string raw = r.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    using (JsonDocument d = JsonDocument.Parse(raw))
                    {
                        _accessToken = d.RootElement.GetProperty("access_token").GetString() ?? "";
                    }
                    int expires = 7200;
                    using (JsonDocument d = JsonDocument.Parse(raw))
                    {
                        if (d.RootElement.TryGetProperty("expires_in", out JsonElement e))
                        {
                            if (e.ValueKind == JsonValueKind.Number)
                            {
                                expires = e.GetInt32();
                            }
                            else if (e.ValueKind == JsonValueKind.String)
                            {
                                int.TryParse(e.GetString(), out expires);
                            }
                        }
                    }
                    if (expires <= 0)
                    {
                        expires = 7200;
                    }
                    _tokenExpireTick = Stopwatch.GetTimestamp() + (long)((expires - 300) * Stopwatch.Frequency);
                    Log("QQBot | " + _displayName + " | Token 刷新成功 " + expires + "s", 1);
                }
            }
            catch (Exception e)
            {
                Log("QQBot | " + _displayName + " | Token 刷新失败: " + e.Message, 3);
            }
        }

        /// <summary>
        /// WS 主循环——连接 + 接收 + opcode 分发；断线自动重连（Resume 优先）。
        /// </summary>
        private void WsLoop()
        {
            while (_wsRun)
            {
                try
                {
                    _ws = new ClientWebSocket();
                    _ws.ConnectAsync(new Uri("wss://api.sgroup.qq.com/websocket"), CancellationToken.None).GetAwaiter().GetResult();
                    _reconnectCount = _reconnectCount + 1;
                    string resumeTag = _sessionId.Length > 0
                        ? "  将尝试 Resume  session_id=" + Truncate(_sessionId, 16) + "...  seq=" + _lastSeq
                        : "  首次鉴权 (Identify)";
                    Log("QQBot | " + _displayName + " | WS 已连接 (重连#" + _reconnectCount + ")" + resumeTag, 1);

                    byte[] buf = new byte[65536];
                    int hbMs = 41250;
                    bool hbStarted = false;
                    bool didResume = false;
                    string pendingSessionId = _sessionId;
                    _missedAcks = 0;

                    while (_ws.State == WebSocketState.Open && _wsRun)
                    {
                        int receiveTimeoutMs = hbMs * 2 + 10000;
                        using (CancellationTokenSource cts = new CancellationTokenSource(receiveTimeoutMs))
                        {
                            WebSocketReceiveResult result;
                            try
                            {
                                result = _ws.ReceiveAsync(new ArraySegment<byte>(buf), cts.Token).GetAwaiter().GetResult();
                            }
                            catch (OperationCanceledException)
                            {
                                Log("QQBot | " + _displayName + " | ReceiveAsync 超时 " + (receiveTimeoutMs / 1000) + "s，连接坏死，主动断开重连", 2);
                                break;
                            }
                            if (result.MessageType == WebSocketMessageType.Close)
                            {
                                break;
                            }
                            string raw = Encoding.UTF8.GetString(buf, 0, result.Count);
                            using (JsonDocument d = JsonDocument.Parse(raw))
                            {
                                int op = d.RootElement.GetProperty("op").GetInt32();
                                // ── op=10: Hello ──────────────────────
                                if (op == 10)
                                {
                                    JsonElement dd = d.RootElement.GetProperty("d");
                                    hbMs = dd.GetProperty("heartbeat_interval").GetInt32();
                                    if (dd.TryGetProperty("session_id", out JsonElement sid))
                                    {
                                        _sessionId = sid.GetString() ?? "";
                                    }
                                    Log("QQBot | " + _displayName + " | Hello 心跳=" + hbMs + "ms  session=" + (_sessionId.Length > 0 ? Truncate(_sessionId, 16) + "..." : "(无)"), 1);
                                    // 决定发 op=6 Resume 还是 op=2 Identify
                                    string auth;
                                    if (pendingSessionId.Length > 0)
                                    {
                                        didResume = true;
                                        auth = "{\"op\":6,\"d\":{\"token\":\"QQBot " + _accessToken + "\",\"session_id\":\"" + pendingSessionId + "\",\"seq\":" + _lastSeq + "}}";
                                    }
                                    else
                                    {
                                        didResume = false;
                                        auth = "{\"op\":2,\"d\":{\"token\":\"QQBot " + _accessToken + "\",\"intents\":" + ((1 << 25) | (1 << 26)) + ",\"shard\":[0,1]}}";
                                    }
                                    byte[] ab = Encoding.UTF8.GetBytes(auth);
                                    _ws.SendAsync(new ArraySegment<byte>(ab), WebSocketMessageType.Text, true, CancellationToken.None).GetAwaiter().GetResult();
                                    Log("QQBot | " + _displayName + " | " + (didResume ? "Resume(op=6)" : "Identify(op=2)") + " 已发送", 1);
                                    if (!hbStarted)
                                    {
                                        hbStarted = true;
                                        ClientWebSocket capturedWs = _ws;
                                        new Thread(delegate() { HbLoop(capturedWs, hbMs); }) { IsBackground = true }.Start();
                                    }
                                }
                                // ── op=0: Dispatch ────────────────────
                                else if (op == 0)
                                {
                                    string t = "";
                                    long s = 0;
                                    if (d.RootElement.TryGetProperty("t", out JsonElement tp))
                                    {
                                        t = tp.GetString() ?? "";
                                    }
                                    if (d.RootElement.TryGetProperty("s", out JsonElement se))
                                    {
                                        s = se.GetInt64();
                                    }
                                    if (s > 0)
                                    {
                                        _lastSeq = s;
                                    }
                                    bool isMsg = (t == "C2C_MESSAGE_CREATE" || t == "GROUP_AT_MESSAGE_CREATE");
                                    string stateTag = "";
                                    if (t == "RESUMED")
                                    {
                                        stateTag = " ✅会话恢复成功";
                                    }
                                    else if (t == "READY")
                                    {
                                        stateTag = " ✅鉴权完成";
                                    }
                                    Log("QQBot | " + _displayName + " ◄◄◄ op=0 s=" + s + " t=" + (t.Length > 0 ? t : "(无)") + (isMsg ? " ★消息" : "") + stateTag, 1);
                                    if (_onMessage != null)
                                    {
                                        _onMessage(raw);
                                    }
                                }
                                // ── op=9: Invalid Session ─────────────
                                else if (op == 9)
                                {
                                    int code = 0;
                                    if (d.RootElement.TryGetProperty("d", out JsonElement d9) && d9.TryGetProperty("code", out JsonElement ce))
                                    {
                                        code = ce.GetInt32();
                                    }
                                    if (code == ERR_INVALID_SESSION && didResume)
                                    {
                                        Log("QQBot | " + _displayName + " | Resume 被拒 (code=4006 session无效)，回落 Identify(op=2)", 2);
                                        didResume = false;
                                        string auth2 = "{\"op\":2,\"d\":{\"token\":\"QQBot " + _accessToken + "\",\"intents\":" + ((1 << 25) | (1 << 26)) + ",\"shard\":[0,1]}}";
                                        byte[] ab2 = Encoding.UTF8.GetBytes(auth2);
                                        _ws.SendAsync(new ArraySegment<byte>(ab2), WebSocketMessageType.Text, true, CancellationToken.None).GetAwaiter().GetResult();
                                        Log("QQBot | " + _displayName + " | Identify(op=2) 已发送（回落）", 1);
                                    }
                                    else if (code == ERR_EXPIRED && didResume)
                                    {
                                        Log("QQBot | " + _displayName + " | Resume 过期 (code=4009)，重试 op=6", 2);
                                        string auth6 = "{\"op\":6,\"d\":{\"token\":\"QQBot " + _accessToken + "\",\"session_id\":\"" + pendingSessionId + "\",\"seq\":" + _lastSeq + "}}";
                                        byte[] ab6 = Encoding.UTF8.GetBytes(auth6);
                                        _ws.SendAsync(new ArraySegment<byte>(ab6), WebSocketMessageType.Text, true, CancellationToken.None).GetAwaiter().GetResult();
                                    }
                                    else
                                    {
                                        _sessionId = "";
                                        Log("QQBot | " + _displayName + " | 会话无效 op=9 code=" + code + "，清除 session_id 后重连", 2);
                                        break;
                                    }
                                }
                                // ── op=11: Heartbeat ACK ──────────────
                                else if (op == 11)
                                {
                                    _missedAcks = 0;
                                }
                                // ── op=7: Reconnect ───────────────────
                                else if (op == 7)
                                {
                                    Log("QQBot | " + _displayName + " | 服务端要求重连 (op=7)", 1);
                                    break;
                                }
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    Log("QQBot | " + _displayName + " | WS 异常 (重连#" + _reconnectCount + "): " + e.Message, 2);
                }
                finally
                {
                    try
                    {
                        if (_ws != null)
                        {
                            _ws.Dispose();
                        }
                    }
                    catch (Exception)
                    {
                    }
                    _ws = null;
                }
                if (_wsRun)
                {
                    Thread.Sleep(3000);
                    if (Stopwatch.GetTimestamp() > _tokenExpireTick)
                    {
                        RefreshToken();
                    }
                }
            }
        }

        /// <summary>
        /// 心跳发送循环——绑定创建时的 ClientWebSocket 实例（ws 参数）。
        /// 每次发送后递增 _missedAcks；若连续 >= MaxMissedAcks 次无 ACK，主动 Dispose ws 触发 WsLoop 重连。
        /// d 字段携带 _lastSeq，使服务端知晓客户端已处理事件进度。
        /// </summary>
        /// <param name="ws">绑定的 WS 实例</param>
        /// <param name="intervalMs">心跳间隔毫秒</param>
        private void HbLoop(ClientWebSocket ws, int intervalMs)
        {
            while (_wsRun && ws.State == WebSocketState.Open)
            {
                Thread.Sleep(intervalMs);
                if (_missedAcks >= MaxMissedAcks)
                {
                    Log("QQBot | " + _displayName + " | 心跳 ACK 连续 " + _missedAcks + " 次无回应，连接坏死，主动断开", 2);
                    try
                    {
                        ws.Dispose();
                    }
                    catch (Exception)
                    {
                    }
                    break;
                }
                try
                {
                    byte[] hb = Encoding.UTF8.GetBytes("{\"op\":1,\"d\":" + _lastSeq + "}");
                    ws.SendAsync(new ArraySegment<byte>(hb), WebSocketMessageType.Text, true, CancellationToken.None).GetAwaiter().GetResult();
                    _missedAcks = _missedAcks + 1;
                }
                catch (Exception)
                {
                    break;
                }
            }
        }

        /// <summary>日志——LogStore 统一观测（module=QQBot）</summary>
        private void Log(string message, int level)
        {
            LogStore.Add("QQBot", level, message, "QQBOT");
        }

        /// <summary>截断文本——日志展示</summary>
        private static string Truncate(string s, int max)
        {
            return s.Length <= max ? s : s.Substring(0, max);
        }

        /// <summary>JSON 转义——发送载荷</summary>
        private static string EscapeJson(string s)
        {
            StringBuilder sb = new StringBuilder(s.Length + 8);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '"')
                {
                    sb.Append("\\\"");
                }
                else if (c == '\\')
                {
                    sb.Append("\\\\");
                }
                else if (c == '\n')
                {
                    sb.Append("\\n");
                }
                else if (c == '\r')
                {
                    sb.Append("\\r");
                }
                else if (c == '\t')
                {
                    sb.Append("\\t");
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }
    }
}
