using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Mau.Runtime;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Mau.Observer
{
    /// <summary>
    /// HTTP 传输层——IObserverTransport 的 ASP.NET Core 实现
    /// GET /status → JSON 全量快照
    /// WS /ws → 每帧推送
    /// GET / → HTML 面板（占位）
    /// </summary>
    public sealed class HttpTransport : IObserverTransport, IDisposable
    {
/// <summary>
/// 监听端口
/// </summary>
        private readonly int _port;
/// <summary>
/// 最近一次推送的快照
/// </summary>
        private AggregatedSnapshot? _latest;
/// <summary>
/// 活跃 WebSocket 客户端集合
/// </summary>
        private readonly ConcurrentBag<WebSocket> _sockets;
/// <summary>
/// ASP.NET Core 主机实例
/// </summary>
        private IHost? _host;
/// <summary>
/// 服务器生命周期取消源
/// </summary>
        private CancellationTokenSource? _cts;
/// <summary>
/// 快照读写锁
/// </summary>
        private readonly object _lock;

        /// <summary>
        /// 构造 HTTP 传输层
        /// </summary>
        /// <param name="port">监听端口</param>
        public HttpTransport(int port = 9230)
        {
            _port = port;
            _latest = null;
            _sockets = new ConcurrentBag<WebSocket>();
            _host = null;
            _cts = null;
            _lock = new object();
        }

        /// <summary>
        /// 启动 HTTP 服务器
        /// </summary>
        /// <returns>true=启动成功 / false=端口被占用</returns>
        public bool Start()
        {
            // 端口冲突检测——try-bind 后立即释放
            try
            {
                System.Net.Sockets.Socket testSocket = new System.Net.Sockets.Socket(
                    System.Net.Sockets.AddressFamily.InterNetwork,
                    System.Net.Sockets.SocketType.Stream,
                    System.Net.Sockets.ProtocolType.Tcp);
                testSocket.Bind(new System.Net.IPEndPoint(System.Net.IPAddress.Any, _port));
                testSocket.Close();
            }
            catch (System.Net.Sockets.SocketException)
            {
                return false;
            }

            _cts = new CancellationTokenSource();
            CancellationToken token = _cts.Token;

            _host = Host.CreateDefaultBuilder()
                .ConfigureLogging(logging =>
                {
                    logging.ClearProviders();
                    logging.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Warning);
                })
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseUrls("http://0.0.0.0:" + _port.ToString());
                    webBuilder.Configure(app =>
                    {
                        app.UseWebSockets();
                        app.UseRouting();
                        app.UseEndpoints(endpoints =>
                        {
                            endpoints.MapGet("/status", HandleStatus);
                            endpoints.MapGet("/", HandleHtml);
                        });
                        app.Use(async (context, next) =>
                        {
                            if (context.Request.Path == "/ws" && context.WebSockets.IsWebSocketRequest)
                            {
                                await HandleWebSocket(context);
                            }
                            else
                            {
                                await next();
                            }
                        });
                    });
                })
                .Build();

            Task.Run(() => _host.RunAsync(token), token);
            return true;
        }

        /// <summary>
        /// 推送快照——IObserverTransport 实现
        /// </summary>
        /// <param name="snapshot">统合快照</param>
        public void Push(AggregatedSnapshot snapshot)
        {
            lock (_lock)
            {
                _latest = snapshot;
            }

            string json = SerializeSnapshot(snapshot);
            byte[] bytes = Encoding.UTF8.GetBytes(json);

            // 推送到所有 WebSocket 客户端
            WebSocket[] sockets = _sockets.ToArray();
            for (int i = 0; i < sockets.Length; i = i + 1)
            {
                WebSocket ws = sockets[i];
                if (ws.State == WebSocketState.Open)
                {
                    try
                    {
                        ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None).GetAwaiter().GetResult();
                    }
                    catch
                    {
                        // 客户端已断开——忽略
                    }
                }
            }
        }

        /// <summary>
        /// GET /status —— 返回最新快照 JSON
        /// </summary>
        /// <param name="context">HTTP 上下文</param>
        /// <returns>Task</returns>
        private Task HandleStatus(HttpContext context)
        {
            AggregatedSnapshot? latest;
            lock (_lock)
            {
                latest = _latest;
            }

            if (latest == null)
            {
                context.Response.ContentType = "application/json";
                return context.Response.WriteAsync("{\"status\":\"no data yet\"}");
            }

            string json = SerializeSnapshot(latest);
            context.Response.ContentType = "application/json";
            return context.Response.WriteAsync(json);
        }

        /// <summary>
        /// GET / —— 占位 HTML 面板
        /// </summary>
        /// <param name="context">HTTP 上下文</param>
        /// <returns>Task</returns>
        private Task HandleHtml(HttpContext context)
        {
            string html = "<!DOCTYPE html>\n<html>\n<head>\n<meta charset=\"utf-8\">\n<title>Mau Observer</title>\n<style>\nbody{font-family:monospace;background:#111;color:#0f0;padding:20px;}\nh1{color:#0ff;}\n#status{white-space:pre-wrap;}\n</style>\n</head>\n<body>\n<h1>🐱 Mau Observer</h1>\n<p>WebSocket 连接到 ws://" + context.Request.Host.Host + ":" + _port.ToString() + "/ws 获取实时状态。</p>\n<p>GET <a href=\"/status\">/status</a> 获取当前完整快照。</p>\n<p><small>HTML 面板后续外包前端实现。</small></p>\n<div id=\"status\"></div>\n<script>\nconst ws = new WebSocket('ws://' + location.host + '/ws');\nws.onmessage = function(e) {\n    const data = JSON.parse(e.data);\n    document.getElementById('status').textContent = JSON.stringify(data, null, 2);\n};\n</script>\n</body>\n</html>";
            context.Response.ContentType = "text/html; charset=utf-8";
            return context.Response.WriteAsync(html);
        }

        /// <summary>
        /// WebSocket 处理
        /// </summary>
        /// <param name="context">HTTP 上下文</param>
        /// <returns>Task</returns>
        private async Task HandleWebSocket(HttpContext context)
        {
            WebSocket ws = await context.WebSockets.AcceptWebSocketAsync();
            _sockets.Add(ws);

            // 发送当前快照
            AggregatedSnapshot? latest;
            lock (_lock)
            {
                latest = _latest;
            }
            if (latest != null)
            {
                string json = SerializeSnapshot(latest);
                byte[] bytes = Encoding.UTF8.GetBytes(json);
                await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
            }

            // 保持连接直到客户端断开
            byte[] buffer = new byte[1024];
            try
            {
                while (ws.State == WebSocketState.Open)
                {
                    WebSocketReceiveResult result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        break;
                    }
                }
            }
            catch
            {
                // 客户端断开
            }

            // 清理
            WebSocket[] sockets = _sockets.ToArray();
            ConcurrentBag<WebSocket> newBag = new ConcurrentBag<WebSocket>();
            for (int i = 0; i < sockets.Length; i = i + 1)
            {
                if (sockets[i] != ws)
                {
                    newBag.Add(sockets[i]);
                }
            }
            _sockets.Clear();
            WebSocket[] remaining = newBag.ToArray();
            for (int i = 0; i < remaining.Length; i = i + 1)
            {
                _sockets.Add(remaining[i]);
            }
        }

        /// <summary>
        /// 序列化快照为 JSON
        /// </summary>
        /// <param name="snapshot">统合快照</param>
        /// <returns>JSON 字符串</returns>
        private static string SerializeSnapshot(AggregatedSnapshot snapshot)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine("  \"timestamp\": \"" + snapshot.Timestamp.ToString("o") + "\",");
            sb.AppendLine("  \"hostFrame\": " + snapshot.HostFrame.ToString() + ",");
            sb.AppendLine("  \"flows\": [");
            for (int i = 0; i < snapshot.Flows.Length; i = i + 1)
            {
                if (i > 0)
                {
                    sb.AppendLine(",");
                }
                FlowSnapshotEntry entry = snapshot.Flows[i];
                sb.AppendLine("    {");
                sb.AppendLine("      \"flowName\": \"" + Mau.Runtime.TextUtil.JsonEscape(entry.FlowName) + "\",");
                sb.AppendLine("      \"status\": {");
                sb.AppendLine("        \"frame\": " + entry.Status.Frame.ToString() + ",");
                sb.Append("        \"propositions\": [");
                for (int p = 0; p < entry.Status.Propositions.Length; p = p + 1)
                {
                    if (p > 0)
                    {
                        sb.Append(", ");
                    }
                    PropSnapshot prop = entry.Status.Propositions[p];
                    sb.Append("{\"name\":\"" + Mau.Runtime.TextUtil.JsonEscape(prop.Name) + "\",\"kind\":\"" + prop.Kind + "\",\"value\":" + (prop.Value ? "true" : "false") + "}");
                }
                sb.AppendLine("],");
                sb.Append("        \"transitions\": [");
                for (int t = 0; t < entry.Status.Transitions.Length; t = t + 1)
                {
                    if (t > 0)
                    {
                        sb.Append(", ");
                    }
                    TransSnapshot trans = entry.Status.Transitions[t];
                    sb.Append("{\"name\":\"" + Mau.Runtime.TextUtil.JsonEscape(trans.Name) + "\",\"cubeState\":\"" + trans.CubeState + "\",\"elapsedFrames\":" + trans.ElapsedFrames.ToString() + ",\"limitFrames\":" + trans.LimitFrames.ToString() + "}");
                }
                sb.AppendLine("]");
                sb.AppendLine("      },");
                sb.Append("      \"logs\": [");
                for (int l = 0; l < entry.Logs.Length; l = l + 1)
                {
                    if (l > 0)
                    {
                        sb.Append(", ");
                    }
                    MauDebug log = entry.Logs[l];
                    sb.Append("{\"frame\":" + log.Frame.ToString() + ",\"transition\":\"" + Mau.Runtime.TextUtil.JsonEscape(log.TransitionName) + "\",\"phase\":\"" + log.Phase + "\",\"message\":\"" + Mau.Runtime.TextUtil.JsonEscape(log.Message) + "\"}");
                }
                sb.AppendLine("]");
                sb.Append("    }");
            }
            if (snapshot.Flows.Length > 0)
            {
                sb.AppendLine();
            }
            sb.AppendLine("  ],");
            sb.AppendLine("  \"systemEvents\": [");
            for (int e = 0; e < snapshot.SystemEvents.Length; e = e + 1)
            {
                if (e > 0)
                {
                    sb.AppendLine(",");
                }
                SystemEvent ev = snapshot.SystemEvents[e];
                sb.Append("    {\"hostFrame\":" + ev.HostFrame.ToString() + ",\"timestamp\":\"" + ev.Timestamp.ToString("o") + "\",\"level\":\"" + ev.Level + "\",\"source\":\"" + Mau.Runtime.TextUtil.JsonEscape(ev.Source) + "\",\"message\":\"" + Mau.Runtime.TextUtil.JsonEscape(ev.Message) + "\"}");
            }
            if (snapshot.SystemEvents.Length > 0)
            {
                sb.AppendLine();
            }
            sb.AppendLine("  ],");
            sb.AppendLine("  \"host\": " + SerializeHost(snapshot.Host));
            sb.Append("}");
            return sb.ToString();
        }

        /// <summary>
        /// 序列化宿主机制快照——OA/Command/ID/线程守卫
        /// </summary>
        /// <param name="host">宿主快照，null=未注入机制</param>
        /// <returns>JSON 对象文本</returns>
        private static string SerializeHost(HostSnapshot? host)
        {
            if (host == null)
            {
                return "null";
            }
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine("  \"frame\": " + host.Frame.ToString() + ",");
            sb.AppendLine("  \"isMainThread\": " + (host.IsMainThread ? "true" : "false") + ",");
            if (host.OA.HasValue)
            {
                OAView oa = host.OA.Value;
                sb.AppendLine("  \"oa\": {");
                sb.AppendLine("    \"version\": " + oa.Version.ToString() + ",");
                sb.AppendLine("    \"open\": " + oa.OpenCount.ToString() + ",");
                sb.AppendLine("    \"work\": " + oa.WorkCount.ToString() + ",");
                sb.AppendLine("    \"closed\": " + oa.ClosedCount.ToString() + ",");
                sb.AppendLine("    \"timeout\": " + oa.TimeoutCount.ToString());
                sb.AppendLine("  },");
            }
            else
            {
                sb.AppendLine("  \"oa\": null,");
            }
            if (host.Command.HasValue)
            {
                CommandSnapshot cmd = host.Command.Value;
                sb.AppendLine("  \"command\": {");
                sb.AppendLine("    \"version\": " + cmd.Version.ToString() + ",");
                sb.AppendLine("    \"registeredOwners\": " + cmd.RegisteredOwnerCount.ToString() + ",");
                sb.AppendLine("    \"registeredKeys\": " + cmd.RegisteredKeyCount.ToString() + ",");
                sb.AppendLine("    \"pendingKeys\": " + cmd.PendingKeyCount.ToString() + ",");
                sb.AppendLine("    \"frozenKeys\": " + cmd.FrozenKeyCount.ToString() + ",");
                sb.AppendLine("    \"acceptingInput\": " + (cmd.IsAcceptingInput ? "true" : "false") + ",");
                sb.AppendLine("    \"rejectedInputs\": " + cmd.RejectedInputCount.ToString());
                sb.AppendLine("  },");
            }
            else
            {
                sb.AppendLine("  \"command\": null,");
            }
            sb.AppendLine("  \"nextId\": " + host.NextId.ToString());
            sb.Append("}");
            return sb.ToString();
        }

        /// <summary>
        /// 停止 HTTP 服务器
        /// </summary>
        public void Stop()
        {
            if (_cts != null)
            {
                _cts.Cancel();
                _cts.Dispose();
                _cts = null;
            }
            if (_host != null)
            {
                _host.StopAsync().GetAwaiter().GetResult();
                _host.Dispose();
                _host = null;
            }
        }

        /// <summary>
        /// 释放
        /// </summary>
        public void Dispose()
        {
            Stop();
        }
    }
}
