using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Mau.Runtime;
using CatHome4.Contracts;

namespace CatHome4.Http
{
    /// <summary>
    /// HTTP 外观层宿主——Kestrel + Minimal API（P6 最小闭环）。
    /// 通道：GET /api/v1/snapshot（全量快照）· GET /api/v1/stream（SSE 事件流）· POST /api/v1/command（指令入口）· GET /（静态页）。
    /// 协议唯一权威：design-ch4-protocol.md（CCBP Project/CH4/）。归 CH4 业务区块（边界判据 7：外观层可选/可切换）。
    /// 服务面（Start/BuildApp/Stop/Port/SseClient）在本文件；推送面分部在 HttpHost.Pump.cs；
    /// HTTP 端点面分部在 HttpHost.Api.cs；配置区面分部在 HttpHost.Config.cs（P7b partial 拆分）。
    /// </summary>
    /// <summary>
    /// HTTP 外观层启动参数——Start 具名选项（R6-P3-06：13 位置参数 → 属性赋值，调用点自文档、顺序无关）。
    /// 回调属性为空 = 该面对应端点/推送不注册（与旧签名 null 参数同义）。
    /// </summary>
    public sealed class HttpHostOptions
    {
        /// <summary>绑定端口</summary>
        public int Port { get; set; }

        /// <summary>会话归属 ID——SSE llm/chatdone 事件的 sessionId</summary>
        public string SessionId { get; set; }

        /// <summary>快照 JSON 构建回调</summary>
        public Func<bool, string> SnapshotBuilder { get; set; }

        /// <summary>指令投递回调</summary>
        public Func<string, bool> Dispatcher { get; set; }

        /// <summary>紧凑帧构建回调（可空=不落帧）</summary>
        public Func<string> FrameBuilder { get; set; }

        /// <summary>会话历史构建回调（可空=不注册该端点）</summary>
        public Func<int, string> HistoryBuilder { get; set; }

        /// <summary>多猫列表构建回调（可空=不注册该端点）</summary>
        public Func<string> CatsBuilder { get; set; }

        /// <summary>Note 状态构建回调（可空）</summary>
        public Func<string> NoteBuilder { get; set; }

        /// <summary>增量 patch 构建回调（可空=全量推送）</summary>
        public Func<string> PatchBuilder { get; set; }

        /// <summary>静态页模式（true=chat.html / false=index.html）</summary>
        public bool ServeChatPage { get; set; }

        /// <summary>管理路由注册回调（可空）</summary>
        public Action<IHttpRouteSink> RouteRegistrar { get; set; }

        /// <summary>html 根解析（源码区优先 + 部署区回退）</summary>
        public IHtmlRootProvider HtmlRootProvider { get; set; }

        /// <summary>页面标题展示名（null=不注入）</summary>
        public string DisplayName { get; set; }
    }

    public sealed partial class HttpHost : IHostPush, IHttpRouteSink
    {
        // [段1] 服务字段——应用实例/端口/回调/事件状态
        /// <summary>Kestrel 应用实例</summary>
        private WebApplication _app;

        /// <summary>绑定端口（配置项接入前写死 8080——协议 §二）</summary>
        private int _port;

        /// <summary>快照 JSON 构建回调——宿主侧注入（Program.BuildSnapshotJson，includeLogs 参数）</summary>
        private Func<bool, string> _snapshotBuilder;

        /// <summary>增量 patch 构建回调——宿主侧注入（Program.BuildPatchJson；可空=不推 patch 保持全量推送）</summary>
        private Func<string> _patchBuilder;

        /// <summary>指令投递回调——宿主侧注入（Program.DispatchCommand）</summary>
        private Func<string, bool> _dispatcher;

        /// <summary>SSE 事件序号——单调递增（协议 §4.3 seq 锚点；多线程并发写走 Interlocked——HTTP/LLM/泵三线程）</summary>
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

        /// <summary>紧凑帧构建回调——frame.txt 帧流（可空=不落帧）</summary>
        private Func<string> _frameBuilder;

        /// <summary>会话历史构建回调——Program.BuildHistoryView（B4 对话区；GET /api/v1/history）</summary>
        private Func<int, string> _historyBuilder;

        /// <summary>会话归属 ID——SSE llm/chatdone 事件 sessionId 字段（P9.3 多实例化：每猫实例绑定自身会话）</summary>
        private string _sessionId;

        /// <summary>多猫列表构建回调——GET /api/v1/cats（可空=不注册该端点——P9.3b 注册表就位后传入）</summary>
        private Func<string> _catsBuilder;

        /// <summary>静态页模式——true=chat.html 独立对话页 / false=index.html 主面板（P9.3 双页模式）</summary>
        private bool _serveChatPage;

        /// <summary>Note 状态构建回调——GET /api/v1/note（M4c 前端面板数据源）</summary>
        private Func<string> _noteBuilder;

        /// <summary>管理路由注册回调——入口壳注入（Admin 域经 IHttpRouteSink 注册 llm-apis/qqbot-apis/workspace 等；仅主端口非空）</summary>
        private Action<IHttpRouteSink> _routeRegistrar;

        /// <summary>html 根解析——入口壳注入（源码区优先 + 部署区回退——Program.ResolveHtmlRoot 适配）</summary>
        private IHtmlRootProvider _htmlRootProvider;

        /// <summary>页面标题——快照注入 title 字段（Q6 2026-09-08：displayName · Chat；前端初始化 fetch 快照直接用；null=不注入）</summary>
        private string _pageTitle;

        /// <summary>快照推送间隔毫秒——250ms（协议 §4.2 snapshot 事件）</summary>
        private const int SnapshotIntervalMs = 250;
        /// <summary>前端测试代理客户端——静态复用（R6-P3-01；Timeout 仅初始化可设，故用工厂构造）</summary>
        private static readonly System.Net.Http.HttpClient FrontendTestHttp = CreateFrontendTestHttp();

        /// <summary>
        /// 启动 HTTP 外观层——Kestrel 绑定端口 + 注册路由 + 后台推送任务启动。
        /// </summary>
        /// <param name="options">启动参数（端口/会话/回调集/页面模式——见 HttpHostOptions 各属性注释）</param>
        /// <returns>HttpHost 实例</returns>
        public static HttpHost Start(HttpHostOptions options)
        {
            HttpHost host = new HttpHost();
            host._port = options.Port;
            host._sessionId = options.SessionId;
            host._snapshotBuilder = options.SnapshotBuilder;
            host._dispatcher = options.Dispatcher;
            host._frameBuilder = options.FrameBuilder;
            host._historyBuilder = options.HistoryBuilder;
            host._catsBuilder = options.CatsBuilder;
            host._noteBuilder = options.NoteBuilder;
            host._patchBuilder = options.PatchBuilder;
            host._serveChatPage = options.ServeChatPage;
            host._routeRegistrar = options.RouteRegistrar;
            host._htmlRootProvider = options.HtmlRootProvider;
            if (options.DisplayName != null && options.DisplayName.Length > 0)
            {
                host._pageTitle = options.DisplayName + " · Chat";
            }
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
            // D5：Kestrel 请求级日志降噪（Hosting.Diagnostics 每请求 4-6 行 → 仅 Warning；Lifetime 启动一行保留）
            builder.Logging.AddFilter("Microsoft.Hosting.Lifetime", LogLevel.Warning);
            builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
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
            _app.MapGet("/api/v1/logs", (Delegate)HandleLogs);
            _app.MapGet("/api/v1/config", (Delegate)HandleConfigGet);
            _app.MapPost("/api/v1/config", (Delegate)HandleConfigPost);
            _app.MapGet("/api/v1/history", (HttpContext ctx) =>
            {
                // B4 对话区——会话历史视图（内存 ChatContext 实时真源；max 夹取 1-2000 缺省 200）
                int max = 200;
                string raw = ctx.Request.Query["max"].ToString();
                int parsed;
                if (int.TryParse(raw, out parsed) && parsed > 0)
                {
                    max = parsed;
                }
                if (max > 2000)
                {
                    max = 2000;
                }
                return Results.Text(_historyBuilder(max), "application/json");
            });
            _app.MapGet("/api/v1/note", (HttpContext ctx) =>
            {
                // M4c Note 状态——前端悬浮气泡数据源（页面加载兜底；实时更新走 SSE note 事件）
                return Results.Text(_noteBuilder(), "application/json");
            });
            if (_catsBuilder != null)
            {
                _app.MapGet("/api/v1/cats", (HttpContext ctx) =>
                {
                    // P9.3 多猫列表——catsBuilder 非空才注册（主端口管理页签数据源；每猫实例不注册）
                    return Results.Text(_catsBuilder(), "application/json");
                });
                // S2 管理端点族——经 IHttpRouteSink 由 Admin 域注册（Http 域零依赖 Admin 域；主端口仅注册一次）
                if (_routeRegistrar != null)
                {
                    _routeRegistrar(this);
                }
            }
            _app.MapGet("/", (HttpContext ctx) =>
            {
                // B4 修复：静态页禁缓存——前端频繁迭代，浏览器启发式缓存导致拿旧版 html（工具回填等新 JS 不生效）
                ctx.Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
                return ServeIndex();
            });
            // 2026-08-25 模块化拆分——静态资源多文件路由（css/js 子目录；禁缓存同 index 策略；路径穿越校验）
            _app.MapGet("/css/{file}", (HttpContext ctx) => ServeStatic(ctx, "css", "text/css"));
            _app.MapGet("/js/{file}", (HttpContext ctx) => ServeStatic(ctx, "js", "application/javascript"));
            // 前端测试服务代理——CH4 通过宿主端点触发前端测试（开发流程：改前端 → 跑测试 → 刷新生效；转发 8099）
            _app.MapGet("/api/v1/frontend-test", async (HttpContext ctx) =>
            {
                string path = "unit";   // E2E 已移除（2026-08-28）——只转发 Vitest
                try
                {
                    System.Net.Http.HttpResponseMessage resp = await FrontendTestHttp.GetAsync("http://127.0.0.1:8099/api/test/" + path);
                    string body = await resp.Content.ReadAsStringAsync();
                    return Results.Text(body, "application/json");
                }
                catch (Exception ex)
                {
                    return Results.Text("{\"ok\":false,\"error\":\"" + ex.Message + "\"}", "application/json");
                }
            });
            try
            {
                _app.StartAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                // 端口绑定失败分类——优先提示"实例已在运行"（P7 裂缝1：双击 exe 闪退判例）
                string detail = ex.Message;
                string hint;
                if (detail.IndexOf("in use", StringComparison.OrdinalIgnoreCase) >= 0 || detail.IndexOf("占用", StringComparison.Ordinal) >= 0)
                {
                    hint = "端口 " + _port + " 已被占用——可能已有 CH4 实例在运行";
                }
                else
                {
                    hint = "HTTP 端口 " + _port + " 绑定失败";
                }
                throw new InvalidOperationException(hint + "（" + detail + "）", ex);
            }
        }

        /// <summary>
        /// 注册 GET 路由——IHttpRouteSink 实现（Admin 域管理路由注册面；转发到 Minimal API 路由表——S2 解耦：Http 域零依赖 Admin 域）。
        /// </summary>
        /// <param name="pattern">路由模板（如 /api/v1/llm-apis）</param>
        /// <param name="handler">处理委托（Minimal API Delegate）</param>
        public void MapGet(string pattern, Delegate handler)
        {
            _app.MapGet(pattern, handler);
        }

        /// <summary>
        /// 注册 POST 路由——IHttpRouteSink 实现。
        /// </summary>
        /// <param name="pattern">路由模板</param>
        /// <param name="handler">处理委托（Minimal API Delegate）</param>
        public void MapPost(string pattern, Delegate handler)
        {
            _app.MapPost(pattern, handler);
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
                // 有界队列 + 满时丢最旧——保留最新事件；慢客户端内存有界（协议 §四"写失败静默丢弃"；无界队列慢客户端堆积判例）
                BoundedChannelOptions options = new BoundedChannelOptions(512);
                options.FullMode = BoundedChannelFullMode.DropOldest;
                options.SingleReader = true;
                Queue = Channel.CreateBounded<string>(options);
            }
        }
        /// <summary>
        /// 创建前端测试代理客户端——静态初始化设超时（HttpClient.Timeout 首次请求后不可改）。
        /// </summary>
        /// <returns>配置好超时的客户端实例</returns>
        private static System.Net.Http.HttpClient CreateFrontendTestHttp()
        {
            System.Net.Http.HttpClient h = new System.Net.Http.HttpClient();
            h.Timeout = TimeSpan.FromSeconds(120);
            return h;
        }
    }
}