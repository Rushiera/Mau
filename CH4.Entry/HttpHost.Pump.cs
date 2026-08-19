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
    /// HttpHost 推送面分部——后台快照循环/主线程泵/Log 增量/SSE 事件广播。
    /// P7b partial 拆分——自 HttpHost.cs 原样搬移，逻辑零改动。
    /// </summary>
    public sealed partial class HttpHost
    {
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
            // O3 帧流——每帧紧凑 JSON 落盘（回放/复盘；FrameStore 未配置时静默 no-op）
            if (_frameBuilder != null)
            {
                try
                {
                    string frameJson = _frameBuilder();
                    if (frameJson != null && frameJson.Length > 0)
                    {
                        FrameStore.Append(frameJson);
                    }
                }
                catch
                {
                    // 帧流异常不阻塞主线程泵
                }
            }
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
    }
}