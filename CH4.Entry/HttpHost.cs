using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Mau.Runtime;

namespace CH4
{
    /// <summary>
    /// HTTP 外观层宿主——Kestrel + Minimal API（P6 最小闭环）。
    /// 通道：GET /api/v1/snapshot（全量快照）· GET /api/v1/stream（SSE 事件流）· POST /api/v1/command（指令入口）· GET /（静态页）。
    /// 协议唯一权威：design-ch4-protocol.md（CCBP Project/CH4/）。归 CH4 业务区块（边界判据 7：外观层可选/可切换）。
    /// </summary>
    public sealed class HttpHost
    {
        // [段1] 服务字段——应用实例/端口/回调/事件状态
        /// <summary>Kestrel 应用实例</summary>
        private WebApplication _app;

        /// <summary>绑定端口（配置项接入前写死 8080——协议 §二）</summary>
        private int _port;

        /// <summary>快照 JSON 构建回调——宿主侧注入（Program.BuildSnapshotJson，includeLogs 参数）</summary>
        private Func<bool, string> _snapshotBuilder;

        /// <summary>指令投递回调——宿主侧注入（Program.DispatchCommand）</summary>
        private Func<string, bool> _dispatcher;

        /// <summary>SSE 事件序号——单调递增（协议 §4.3 seq 锚点）</summary>
        private int _seq;

        /// <summary>SSE 客户端集合——锁保护（多连接独立广播）</summary>
        private readonly List<SseClient> _clients = new List<SseClient>();

        /// <summary>客户端集合锁</summary>
        private readonly object _clientLock = new object();

        /// <summary>LogStore 增量游标——已推送条目数（协议 §4.2 log 事件）</summary>
        private int _logCursor;

        /// <summary>后台推送任务取消源——宿主退出时停</summary>
        private CancellationTokenSource _pumpCts;

        /// <summary>后台推送任务——快照定时 + log 增量（协议 §4.1）</summary>
        private Task _pumpTask;

        /// <summary>停止标志——Stop 后 Push 静默丢弃</summary>
        private bool _stopped;

        /// <summary>快照缓存——主线程 PumpMainThread 构建，HTTP 线程只读（最近一帧主线程快照）</summary>
        private string _snapshotCache = "{}";

        /// <summary>快照待构建标志——PumpLoop 置位，主线程 PumpMainThread 消费</summary>
        private volatile bool _snapshotPending;

        /// <summary>快照推送间隔毫秒——250ms（协议 §4.2 snapshot 事件）</summary>
        private const int SnapshotIntervalMs = 250;

        /// <summary>
        /// 启动 HTTP 外观层——Kestrel 绑定端口 + 注册四路由 + 后台推送任务启动。
        /// </summary>
        /// <param name="port">监听端口（127.0.0.1 回环）</param>
        /// <param name="snapshotBuilder">快照 JSON 构建回调（includeLogs——快照轮询含日志/SSE 事件裁剪）</param>
        /// <param name="dispatcher">指令投递回调（返回 true=识别并投递）</param>
        /// <returns>HttpHost 实例</returns>
        public static HttpHost Start(int port, Func<bool, string> snapshotBuilder, Func<string, bool> dispatcher)
        {
            HttpHost host = new HttpHost();
            host._port = port;
            host._snapshotBuilder = snapshotBuilder;
            host._dispatcher = dispatcher;
            host.BuildApp();
            host._pumpCts = new CancellationTokenSource();
            host._pumpTask = Task.Run(delegate
            {
                host.PumpLoop(host._pumpCts.Token);
            });
            return host;
        }

        /// <summary>
        /// 构建 Kestrel 应用——路由注册（快照/流/指令/静态页）。
        /// </summary>
        private void BuildApp()
        {
            WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.ConfigureKestrel((options) =>
            {
                options.ListenLocalhost(_port);
            });
            _app = builder.Build();
            _app.MapGet("/api/v1/snapshot", (HttpContext ctx) =>
            {
                // 缓存快照——主线程 PumpMainThread 构建（ThreadGuard：OA.GetSnapshot 仅宿主主线程）
                // ?logs=N——动态附加日志段（LogStore 锁内快照，HTTP 线程安全；v2 界面刷新拉最后 N 条）
                string json = _snapshotCache;
                int logCount = ParseLogsQuery(ctx);
                string logsJson = BuildLogsJson(logCount);
                if (logsJson.Length > 0)
                {
                    json = ReplaceLogsSection(json, logsJson);
                }
                return Results.Text(json, "application/json");
            });
            _app.MapGet("/api/v1/stream", (RequestDelegate)StreamEvents);
            _app.MapPost("/api/v1/command", (Delegate)HandleCommand);
            _app.MapGet("/api/v1/config", (Delegate)HandleConfigGet);
            _app.MapPost("/api/v1/config", (Delegate)HandleConfigPost);
            _app.MapGet("/", () =>
            {
                return ServeIndex();
            });
            _app.StartAsync().GetAwaiter().GetResult();
        }

        /// <summary>
        /// 后台推送循环——每 250ms 推全量快照 + LogStore 增量（协议 §4.2）。
        /// </summary>
        /// <param name="token">取消令牌</param>
        private void PumpLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    Thread.Sleep(SnapshotIntervalMs);
                    // 快照构建归主线程（ThreadGuard——OA.GetSnapshot 仅宿主主线程）——后台只置标志，主线程 PumpMainThread 消费
                    _snapshotPending = true;
                    PushLogIncrements();
                }
                catch (Exception)
                {
                    // 推送异常不炸宿主——静默跳过下一轮
                }
            }
        }

        /// <summary>
        /// 主线程泵——宿主帧循环调用：快照待构建标志置位时在主线程构建 + 缓存 + 推送（ThreadGuard 契约）。
        /// </summary>
        public void PumpMainThread()
        {
            if (!_snapshotPending)
            {
                return;
            }
            _snapshotPending = false;
            string json = _snapshotBuilder(false);
            _snapshotCache = json;
            PushEvent("snapshot", json);
        }

        /// <summary>
        /// 解析 ?logs=N 查询参数——缺省/非法回退 200（v2 刷新拉取最后 N 条）
        /// </summary>
        /// <param name="ctx">HTTP 上下文</param>
        /// <returns>日志条数（1-2000 夹取）</returns>
        private static int ParseLogsQuery(HttpContext ctx)
        {
            int count = 200;
            if (ctx.Request.Query.TryGetValue("logs", out Microsoft.Extensions.Primitives.StringValues values))
            {
                string raw = values.ToString();
                int parsed;
                if (int.TryParse(raw, out parsed) && parsed > 0)
                {
                    count = parsed;
                }
            }
            if (count > 2000)
            {
                count = 2000;
            }
            return count;
        }

        /// <summary>
        /// 构建日志段 JSON——LogStore 尾部 N 条（与 PushLogIncrements 同格式；锁内快照）
        /// </summary>
        /// <param name="count">条数</param>
        /// <returns>日志数组 JSON；无日志返回空串（不拼接）</returns>
        private static string BuildLogsJson(int count)
        {
            List<LogStore.LogEntry> logs = LogStore.AllLog;
            lock (LogStore.Sync)
            {
                int start = logs.Count - count;
                if (start < 0)
                {
                    start = 0;
                }
                List<object> list = new List<object>();
                for (int i = start; i < logs.Count; i++)
                {
                    LogStore.LogEntry entry = logs[i];
                    list.Add(new { time = entry.Time, frame = entry.Frame, level = LogStore.LevelText(entry.Level), category = entry.Category, module = entry.Module, message = entry.Message });
                }
                return JsonSerializer.Serialize(list);
            }
        }

        /// <summary>
        /// 替换快照 JSON 的日志段——缓存快照 logs=[] 的精确字符串替换（System.Text.Json 固定输出无空格；不可靠则原样返回）
        /// </summary>
        /// <param name="json">快照 JSON（含 "logs":[]）</param>
        /// <param name="logsJson">日志数组 JSON</param>
        /// <returns>拼接后 JSON；未命中返回原 JSON</returns>
        private static string ReplaceLogsSection(string json, string logsJson)
        {
            string marker = "\"logs\":[]";
            int idx = json.IndexOf(marker, StringComparison.Ordinal);
            if (idx < 0)
            {
                return json;
            }
            return json.Substring(0, idx) + "\"logs\":" + logsJson + json.Substring(idx + marker.Length);
        }

        /// <summary>
        /// LogStore 增量推送——游标后新条目逐条推 log 事件（协议 §4.2）。
        /// </summary>
        private void PushLogIncrements()
        {
            List<LogStore.LogEntry> logs = LogStore.AllLog;
            lock (LogStore.Sync)
            {
                while (_logCursor < logs.Count)
                {
                    LogStore.LogEntry entry = logs[_logCursor];
                    _logCursor = _logCursor + 1;
                    var obj = new
                    {
                        time = entry.Time,
                        frame = entry.Frame,
                        level = LogStore.LevelText(entry.Level),
                        category = entry.Category,
                        module = entry.Module,
                        message = entry.Message
                    };
                    PushEvent("log", JsonSerializer.Serialize(obj));
                }
            }
        }

        /// <summary>
        /// SSE 连接处理——注册客户端 + 事件队列消费（协议 §四：text/event-stream 帧格式）。
        /// </summary>
        /// <param name="ctx">HTTP 上下文</param>
        /// <returns>完成任务</returns>
        private async Task StreamEvents(HttpContext ctx)
        {
            ctx.Response.Headers["Content-Type"] = "text/event-stream";
            ctx.Response.Headers["Cache-Control"] = "no-cache";
            ctx.Response.Headers["Connection"] = "keep-alive";
            SseClient client = new SseClient(ctx.Response);
            lock (_clientLock)
            {
                _clients.Add(client);
            }
            // 连接建立即推全量快照——重连兜底（协议 §4.3；缓存——主线程构建）
            PushEvent("snapshot", _snapshotCache);
            try
            {
                await foreach (string frame in client.Queue.Reader.ReadAllAsync(ctx.RequestAborted))
                {
                    await client.Response.WriteAsync(frame);
                    await client.Response.Body.FlushAsync();
                }
            }
            catch (Exception)
            {
                // 客户端断开——静默移除
            }
            finally
            {
                lock (_clientLock)
                {
                    _clients.Remove(client);
                }
            }
        }

        /// <summary>
        /// 指令入口处理——解析 JSON body → 投递 → 回执（协议 §五：投递即回执，结果异步可见）。
        /// </summary>
        /// <param name="ctx">HTTP 上下文</param>
        /// <returns>回执 JSON</returns>
        private async Task<IResult> HandleCommand(HttpContext ctx)
        {
            string body = "";
            using (System.IO.StreamReader reader = new System.IO.StreamReader(ctx.Request.Body))
            {
                body = await reader.ReadToEndAsync();
            }
            string text = ExtractText(body);
            bool ok = _dispatcher(text);
            string cmdId = "cmd-" + _seq;
            long frame = FlowRunner.GlobalFrame;
            if (ok)
            {
                var resp = new
                {
                    ok = true,
                    cmdId = cmdId,
                    frame = frame
                };
                PushEvent("cmd", JsonSerializer.Serialize(resp));
                return Results.Json(resp);
            }
            var fail = new
            {
                ok = false,
                cmdId = cmdId,
                frame = frame,
                error = "指令未识别: " + text
            };
            PushEvent("cmd", JsonSerializer.Serialize(fail));
            return Results.Json(fail);
        }

        /// <summary>
        /// 配置读取——GET /api/v1/config（配置区 v3）。
        /// 返回合并后的有效配置项：file（llm.cfg 显式值）优先，env（MAU_LLM_* 兜底）补齐；
        /// 敏感键（api_key/secret/token）掩码展示——snapshot 边界铁律同源。
        /// </summary>
        /// <returns>配置 JSON——版本 + items[key/value/source]</returns>
        private IResult HandleConfigGet()
        {
            // [段1] 解析配置存储——未绑定返回空列表（壳形态可渲染）
            ConfigStore cfg = null!;
            bool bound = DataBox.TryResolve<ConfigStore>(out cfg);
            List<object> items = new List<object>();
            if (!bound || cfg == null)
            {
                var emptyResp = new
                {
                    version = 1,
                    items = items
                };
                return Results.Json(emptyResp);
            }
            // [段2] file 显式项——llm.cfg 值优先；敏感键掩码（effective 记录已显式键）
            KeyValuePair<string, string>[] all = cfg.All();
            Dictionary<string, string> effective = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < all.Length; i++)
            {
                string key = all[i].Key;
                string value = all[i].Value;
                if (!effective.ContainsKey(key))
                {
                    effective[key] = value;
                }
                if (IsSecretKey(key))
                {
                    value = MaskSecret(value);
                }
                items.Add(new
                {
                    key = key,
                    value = value,
                    source = "file"
                });
            }
            // [段3] env 兜底——file 未显式提供的 llm.* 键从 MAU_LLM_* 全局环境变量补齐
            string[] envKeys = new string[] { "llm.base_url", "llm.api_key", "llm.model", "llm.thinking", "llm.reasoning_effort" };
            string[] envNames = new string[] { "MAU_LLM_BASE_URL", "MAU_LLM_API_KEY", "MAU_LLM_MODEL", "MAU_LLM_THINKING", "MAU_LLM_REASONING_EFFORT" };
            for (int i = 0; i < envKeys.Length; i++)
            {
                if (effective.ContainsKey(envKeys[i]))
                {
                    continue;
                }
                string envValue = Environment.GetEnvironmentVariable(envNames[i]);
                if (string.IsNullOrEmpty(envValue))
                {
                    continue;
                }
                string shown = envValue;
                if (IsSecretKey(envKeys[i]))
                {
                    shown = MaskSecret(shown);
                }
                items.Add(new
                {
                    key = envKeys[i],
                    value = shown,
                    source = "env"
                });
            }
            var resp = new
            {
                version = 1,
                items = items
            };
            return Results.Json(resp);
        }

        /// <summary>
        /// 配置写入——POST /api/v1/config（body: {"key":"...","value":"..."}）。
        /// 落盘 llm.cfg（ConfigStore.Set + Save 原子写）；敏感键拒绝掩码值回写（防掩码覆盖）。
        /// 注意：运行时生效待 P7（DeepSeekLlmRuntime 构造期读配置——本轮写配置 = 落盘 + 提示重启）。
        /// </summary>
        /// <param name="ctx">HTTP 上下文</param>
        /// <returns>回执 JSON——ok/key/frame</returns>
        private async Task<IResult> HandleConfigPost(HttpContext ctx)
        {
            // [段1] body 解析——防御式 JSON {key,value}
            string body = "";
            using (System.IO.StreamReader reader = new System.IO.StreamReader(ctx.Request.Body))
            {
                body = await reader.ReadToEndAsync();
            }
            string key = "";
            string value = "";
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(body))
                {
                    JsonElement root = doc.RootElement;
                    if (root.TryGetProperty("key", out JsonElement k))
                    {
                        key = k.GetString() ?? "";
                    }
                    if (root.TryGetProperty("value", out JsonElement v))
                    {
                        value = v.GetString() ?? "";
                    }
                }
            }
            catch (Exception)
            {
                // body 非 JSON——key/value 保持空串走下方校验拒绝
                key = "";
                value = "";
            }
            // [段2] 校验——key/value 非空 + 掩码回写拒绝
            if (key.Length == 0)
            {
                return Results.Json(new { ok = false, error = "key 为空" });
            }
            if (value.Length == 0)
            {
                return Results.Json(new { ok = false, error = "value 为空" });
            }
            if (value.IndexOf("****", StringComparison.Ordinal) >= 0)
            {
                return Results.Json(new { ok = false, error = "value 含掩码标记——请输入真实值" });
            }
            // [段3] 写入落盘——Set + Save 原子写；回执值掩码
            ConfigStore cfg = null!;
            bool bound = DataBox.TryResolve<ConfigStore>(out cfg);
            if (!bound || cfg == null)
            {
                return Results.Json(new { ok = false, error = "配置存储未绑定" });
            }
            cfg.Set(key, value);
            cfg.Save();
            long frame = FlowRunner.GlobalFrame;
            // 回执值掩码——敏感键不回显新值明文（与 GET 掩码同规）
            string maskedValue;
            if (IsSecretKey(key))
            {
                maskedValue = MaskSecret(value);
            }
            else
            {
                maskedValue = value;
            }
            var resp = new
            {
                ok = true,
                key = key,
                value = maskedValue,
                frame = frame
            };
            return Results.Json(resp);
        }

        /// <summary>
        /// 敏感键判定——api_key/secret/token 系内容掩码展示（边界铁律：密钥不进快照/回显明文）
        /// </summary>
        /// <param name="key">配置键</param>
        /// <returns>是否敏感</returns>
        private static bool IsSecretKey(string key)
        {
            return key.IndexOf("api_key", StringComparison.OrdinalIgnoreCase) >= 0
                || key.IndexOf("secret", StringComparison.OrdinalIgnoreCase) >= 0
                || key.IndexOf("token", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// 敏感值掩码——前4 + **** + 后4（短值全掩码）
        /// </summary>
        /// <param name="value">原始值</param>
        /// <returns>掩码文本</returns>
        private static string MaskSecret(string value)
        {
            if (value == null || value.Length == 0)
            {
                return "";
            }
            if (value.Length <= 8)
            {
                return "****";
            }
            return value.Substring(0, 4) + "****" + value.Substring(value.Length - 4);
        }

        /// <summary>
        /// 从请求 body 提取指令文本——防御式解析（协议 §5.1：{"text":"..."}）。
        /// </summary>
        /// <param name="body">原始 body</param>
        /// <returns>指令文本（解析失败空串）</returns>
        private static string ExtractText(string body)
        {
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(body))
                {
                    JsonElement root = doc.RootElement;
                    JsonElement textEl;
                    if (root.TryGetProperty("text", out textEl) && textEl.ValueKind == JsonValueKind.String)
                    {
                        string text = textEl.GetString();
                        if (text != null)
                        {
                            return text;
                        }
                    }
                }
            }
            catch (Exception)
            {
                // 解析失败——按空指令处理（错误可见性：回执 ok=false）
            }
            return "";
        }

        /// <summary>
        /// 静态页服务——返回 index.html（协议 §六：GET / 本地面板）。
        /// </summary>
        /// <returns>HTML 响应</returns>
        private static IResult ServeIndex()
        {
            string baseDir = AppContext.BaseDirectory;
            string htmlPath = System.IO.Path.Combine(baseDir, "html", "index.html");
            if (System.IO.File.Exists(htmlPath))
            {
                string html = System.IO.File.ReadAllText(htmlPath);
                return Results.Text(html, "text/html");
            }
            return Results.Text("CH4 外观层——index.html 未找到: " + htmlPath + "（宿主需在 CH4.Entry/bin/.../html/ 放置静态页）", "text/plain");
        }

        /// <summary>
        /// 广播 SSE 事件帧——event: 名 + data: JSON（协议 §4.1 标准 text/event-stream）。
        /// </summary>
        /// <param name="eventName">事件名（snapshot/llm/log/cmd）</param>
        /// <param name="data">JSON 载荷</param>
        private void PushEvent(string eventName, string data)
        {
            if (_stopped)
            {
                return;
            }
            _seq = _seq + 1;
            string frame = "event: " + eventName + "\ndata: " + data + "\n\n";
            lock (_clientLock)
            {
                for (int i = 0; i < _clients.Count; i++)
                {
                    SseClient client = _clients[i];
                    if (!client.Queue.Writer.TryWrite(frame))
                    {
                        // 队列写失败（客户端断开）——完成该队列（消费端退出清理）
                        client.Queue.Writer.TryComplete();
                    }
                }
            }
        }

        /// <summary>
        /// LLM 流式事件转发——宿主 ChatBridge 调用（协议 §4.2 llm 事件：seq 单调 + kind 五态）。
        /// </summary>
        /// <param name="kind">事件态——text/reasoning/toolCalls/done/error（直映 LlmStreamKind）</param>
        /// <param name="text">增量文本或错误文本</param>
        public void PushLlm(string kind, string text)
        {
            var obj = new
            {
                seq = _seq,
                kind = kind,
                text = text
            };
            PushEvent("llm", JsonSerializer.Serialize(obj));
        }

        /// <summary>
        /// 停止外观层——Kestrel 停止 + 后台推送取消。
        /// </summary>
        public void Stop()
        {
            _stopped = true;
            if (_pumpCts != null)
            {
                _pumpCts.Cancel();
                _pumpCts.Dispose();
                _pumpCts = null;
            }
            if (_app != null)
            {
                _app.StopAsync().GetAwaiter().GetResult();
                _app = null;
            }
        }

        /// <summary>
        /// 已绑定端口——对外查询（观测用）。
        /// </summary>
        public int Port
        {
            get { return _port; }
        }

        /// <summary>
        /// SSE 客户端——每连接一个无界事件队列（协议 §4.3：多浏览器连接独立广播）。
        /// </summary>
        private sealed class SseClient
        {
            /// <summary>事件队列——宿主 Push 入队 / 连接消费写响应</summary>
            public Channel<string> Queue;

            /// <summary>HTTP 响应——写入 SSE 帧</summary>
            public HttpResponse Response;

            /// <summary>
            /// 构造 SSE 客户端
            /// </summary>
            /// <param name="response">HTTP 响应</param>
            public SseClient(HttpResponse response)
            {
                Response = response;
                Queue = Channel.CreateUnbounded<string>();
            }
        }
    }
}
