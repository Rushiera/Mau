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

namespace CatHome4.Http
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
                catch (Exception ex)
                {
                    // 推送异常不炸宿主——静默跳过下一轮
                    LogStore.Add("HttpHost", 2, "日志增量推送异常，跳过本轮: " + ex.Message, "SYS");
                }
            }
        }

        /// <summary>
        /// 快照注入页面标题——根对象加 title 字段（不可变快照复制 + 字段前置；解析失败原样返回）。
        /// </summary>
        /// <param name="json">原快照 JSON</param>
        /// <param name="title">标题文本（displayName · Chat）</param>
        /// <returns>注入后的快照 JSON</returns>
        private static string InjectSnapshotTitle(string json, string title)
        {
            try
            {
                using (JsonDocument doc = JsonUtil.ParseStrict(json))
                {
                    JsonElement root = doc.RootElement;
                    using (System.IO.MemoryStream ms = new System.IO.MemoryStream())
                    {
                        using (Utf8JsonWriter writer = new Utf8JsonWriter(ms))
                        {
                            writer.WriteStartObject();
                            writer.WriteString("title", title);
                            foreach (JsonProperty prop in root.EnumerateObject())
                            {
                                writer.WritePropertyName(prop.Name);
                                prop.Value.WriteTo(writer);
                            }
                            writer.WriteEndObject();
                        }
                        return System.Text.Encoding.UTF8.GetString(ms.ToArray());
                    }
                }
            }
            catch (Exception)
            {
                // 快照畸形——原样返回（观测面不受单帧异常影响）
                return json;
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
            // Q6 页面标题——每猫快照注入 title 字段（displayName · Chat；前端初始化 fetch 快照直接用——前端零业务逻辑）
            if (_pageTitle != null && _pageTitle.Length > 0)
            {
                json = InjectSnapshotTitle(json, _pageTitle);
            }
            _snapshotCache = json;
            // 增量流式——只推变化段；无变化零推送（Idle 稳态静默）：对话端口推本猫运行态（sessionstate）、主端口推全局 patch、两者皆缺时回落全量快照
            if (_sessionStateBuilder != null)
            {
                // 对话端口——本猫运行态变化才推（替代每 250ms 全量快照：空闲零推送、前端零轮询）
                string state = _sessionStateBuilder();
                if (state != null && state.Length > 0 && !string.Equals(state, _lastSessionState, StringComparison.Ordinal))
                {
                    _lastSessionState = state;
                    PushEvent("sessionstate", state);
                }
            }
            else if (_patchBuilder != null)
            {
                string patch = _patchBuilder();
                if (patch != null && patch.Length > 0)
                {
                    PushEvent("patch", patch);
                }
            }
            else
            {
                PushEvent("snapshot", json);
            }
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
                catch (Exception ex)
                {
                    // 帧流异常不阻塞主线程泵
                    LogStore.Add("HttpHost", 2, "帧流构建异常，不阻塞主线程泵: " + ex.Message, "SYS");
                }
            }
        }
        /// <summary>LogStore 增量推送——游标后新条目逐条推 log 事件（协议 §4.2）；trace 类审计不进前端（IsTraceAudit，2026-09-17）。</summary>
        private void PushLogIncrements()
        {
            List<LogStore.LogEntry> logs = LogStore.AllLog;
            lock (LogStore.Sync)
            {
                while (_logCursor < logs.Count)
                {
                    LogStore.LogEntry entry = logs[_logCursor];
                    _logCursor = _logCursor + 1;
                    // trace 类审计不进前端（导线级轨迹——2026-09-17）
                    if (IsTraceAudit(entry))
                    {
                        continue;
                    }
                    var obj = new
                    {
                        time = entry.Time,
                        frame = entry.Frame,
                        level = LogStore.LevelText(entry.Level),
                        category = entry.Category,
                        module = entry.Module,
                        message = entry.Message
                    };
                    PushEvent("log", JsonUtil.Serialize(obj));
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
            Interlocked.Increment(ref _seq);
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
        /// Note 状态事件——会话 Note 变化推送（M4c：前端悬浮气泡实时重绘；载荷含 sessionId 归属）。
        /// </summary>
        /// <param name="json">Note 状态 JSON（ChatSession.BuildNoteJson 产物）</param>
        public void PushNoteState(string json)
        {
            string frame = "{\"sessionId\":\"" + _sessionId + "\",\"state\":" + json + "}";
            PushEvent("note", frame);
        }        /// <summary>
                 /// 工具结果实时推送——宿主 ChatBridge ExecuteToolBatch 调用（B4 对话区：tool 事件）。
                 /// 载荷与 history 视图同截断（参数 ≤200/结果 ≤300）；事件顺序 = 执行顺序 = toolCalls 数组顺序（前端 FIFO 配对）。
                 /// </summary>
                 /// <param name="name">工具名</param>
                 /// <param name="arguments">参数摘要（≤200）</param>
                 /// <param name="result">结果摘要（≤300；ERR 前缀失败）</param>
        public void PushToolResult(string name, string arguments, string result)
        {
            var obj = new
            {
                name = name,
                arguments = arguments,
                result = result
            };
            PushEvent("tool", JsonUtil.Serialize(obj));
        }

        /// <summary>
        /// 会话完成事件——宿主 ChatBridge HandleChat 端末调用（B4 对话区：chatdone 事件）。
        /// 语义：llm done 仅代表"一轮 LLM 流结束"（工具轮还有 tool 事件 + 续轮）；chatdone = 整次会话终态。
        /// 前端以 chatdone 为准定型（去光标/未回填兜底/恢复 idle）——修复"工具轮 done 被当终态"的时序 bug。
        /// </summary>
        public void PushChatDone(int count)
        {
            var obj = new
            {
                sessionId = _sessionId,
                count = count
            };
            PushEvent("chatdone", JsonUtil.Serialize(obj));
        }

        /// <summary>
        /// LLM 流式事件转发——宿主 ChatBridge 调用（协议 §4.2 llm 事件：seq 单调 + kind 五态 + sessionId 归属）。
        /// sessionId：本实例归属会话（P9.3 多实例化——每猫 HttpHost 绑定自身会话；B4 归属性保持）。
        /// </summary>
        /// <param name="kind">事件态——text/reasoning/toolCalls/done/error（直映 LlmStreamKind）</param>
        /// <param name="text">增量文本或错误文本</param>
        public void PushLlm(string kind, string text)
        {
            // 先取自身序号（线性可用与广播推进并存——PushEvent 内部再递增；seq 单调即满足排序锚点语义）
            int seq = Interlocked.Increment(ref _seq);
            var obj = new
            {
                seq = seq,
                kind = kind,
                text = text,
                sessionId = _sessionId
            };
            PushEvent("llm", JsonUtil.Serialize(obj));
        }

        /// <summary>
        /// 用户消息事件——所有进内核的消息统一出口（单向数据流改造：前端气泡唯一来源）。
        /// </summary>
        /// <param name="text">消息文本</param>
        /// <param name="source">来源——user/system</param>
        public void PushUserMessage(string text, string source)
        {
            int seq = Interlocked.Increment(ref _seq);
            var obj = new
            {
                seq = seq,
                source = source,
                content = text,
                sessionId = _sessionId
            };
            PushEvent("user", JsonUtil.Serialize(obj));
        }

        /// <summary>
        /// 视图事件推送——F4 视图块统一出口（流式增量/整块/控制块；seq 全局单调）。
        /// 载荷语义：seq = 全局单调序号（流式容器标识）；renderType = 前端渲染类型；
        /// replaceSeq = 被替换块序号（流式→整块替换）；seqHint &gt; 0 时复用该序号（流式增量不递增）。
        /// </summary>
        /// <param name="renderType">渲染类型——stream/user/text/reason/toolcard/control</param>
        /// <param name="payload">载荷 JSON 字符串（内嵌对象）</param>
        /// <param name="replaceSeq">被替换块序号（-1=无替换）</param>
        /// <param name="seqHint">流式增量带已分配序号（&gt;0 不递增；≤0 分配新序号）</param>
        /// <returns>事件序号</returns>
        public int PushView(string renderType, string payload, long replaceSeq, long seqHint)
        {
            int seq;
            if (seqHint > 0)
            {
                seq = (int)seqHint;
            }
            else
            {
                seq = Interlocked.Increment(ref _seq);
            }
            object payloadObj;
            try
            {
                payloadObj = JsonSerializer.Deserialize<object>(payload);
                if (payloadObj == null)
                {
                    payloadObj = "";
                }
            }
            catch (Exception ex)
            {
                LogStore.Add("CatHome4", 2, "工具载荷解析失败（按原文）: " + ex.Message, "TOOL");
                payloadObj = payload;
            }
            var obj = new
            {
                seq = seq,
                renderType = renderType,
                payload = payloadObj,
                replaceSeq = replaceSeq
            };
            PushEvent("view", JsonUtil.Serialize(obj));
            return seq;
        }
    }
}