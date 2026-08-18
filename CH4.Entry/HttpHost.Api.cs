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

namespace CH4
{
    /// <summary>
    /// HttpHost HTTP 端点面分部——SSE 连接/指令入口/日志段/静态页。
    /// P7b partial 拆分——自 HttpHost.cs 原样搬移，逻辑零改动。
    /// </summary>
    public sealed partial class HttpHost
    {
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
    }
}