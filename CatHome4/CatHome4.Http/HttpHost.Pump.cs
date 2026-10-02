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
        /// <summary>LogStore 增量推送——锁内只取区间快照 + 推进游标，序列化与广播在锁外；一轮泵的多条日志批量合帧为单个 log 事件（载荷数组，A141）；trace 类审计不进前端（IsTraceAudit，2026-09-17）。</summary>
        private void PushLogIncrements()
        {
            List<LogStore.LogEntry> logs = LogStore.AllLog;
            List<LogStore.LogEntry> pending = new List<LogStore.LogEntry>();
            // A141 锁内只做「取区间快照 + 推进游标」——序列化与广播移到锁外（宿主主循环写日志不再与推送面竞争同一把锁）
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
                    pending.Add(entry);
                }
            }
            if (pending.Count == 0)
            {
                return;
            }
            // A141 批量合帧——一轮泵的多条日志合成一个 log 帧（载荷为数组，前端按数组逐条消费；
            // 日志属"可弃"级：整批丢弃代价远低于每帧一槽的队列占用）
            List<object> list = new List<object>();
            for (int i = 0; i < pending.Count; i++)
            {
                LogStore.LogEntry entry = pending[i];
                list.Add(new
                {
                    time = entry.Time,
                    frame = entry.Frame,
                    level = LogStore.LevelText(entry.Level),
                    category = entry.Category,
                    module = entry.Module,
                    message = entry.Message
                });
            }
            PushEvent("log", JsonUtil.Serialize(list));
        }

        /// <summary>广播 SSE 事件帧——id 行（每连接帧序号，入队即分配）+ event 行 + data 行（协议 §4.1 标准 text/event-stream）。队列满时丢弃最旧帧不再静默：溢出计数 + 首次出声；丢弃条数体现为客户端所见 id 落差（design-ch4-push-perf §3.2）。</summary>
        /// <param name="eventName">事件名（snapshot/llm/log/cmd）</param>
        /// <param name="data">JSON 载荷</param>
        private void PushEvent(string eventName, string data)
        {
            if (_stopped)
            {
                return;
            }
            bool overflow = false;
            int overflowSeq = 0;
            lock (_clientLock)
            {
                for (int i = 0; i < _clients.Count; i++)
                {
                    SseClient client = _clients[i];
                    // A141 按订阅推送——不订阅本事件名的连接根本不入队（也不消耗其帧序号）
                    if (!client.Wants(eventName))
                    {
                        continue;
                    }
                    // 队列已满——本次 TryWrite 将丢弃最旧帧（DropOldest 内部完成、无回调）→ 显式计数
                    if (client.Queue.Reader.CanCount && client.Queue.Reader.Count >= SseClient.Capacity)
                    {
                        client.OverflowCount = client.OverflowCount + 1;
                        if (!client.OverflowReported)
                        {
                            client.OverflowReported = true;
                            overflow = true;
                            overflowSeq = client.SentSeq;
                        }
                    }
                    string frame = client.NextFrame(eventName, data);
                    if (!client.Queue.Writer.TryWrite(frame))
                    {
                        // 队列写失败（客户端断开）——完成该队列（消费端退出清理）
                        client.Queue.Writer.TryComplete();
                    }
                }
            }
            if (overflow)
            {
                // 锁外出声——失败必须可见（丢弃条数体现为客户端所见 id 落差）
                LogStore.Add("HttpHost", 2, "SSE 队列溢出——丢弃最旧帧（客户端已发 seq=" + overflowSeq + "；丢弃条数以 lastEventId 落差体现）", "SYS");
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
        /// 视图事件推送——视图出口统一 op 面（A158 期三：两区镜像）。
        /// op 取值：persist.append（持久块建块即推）· live.add / live.update / live.remove（流式区镜像）
        /// · control（瞬时事件面：usage / chatdone / paused / note / session_reset）。
        /// </summary>
        /// <param name="op">出口事件类型</param>
        /// <param name="payload">载荷 JSON 字符串（内嵌对象；live.remove 为空串——不解析）</param>
        /// <param name="meta">块元数据 JSON（A158 块字段：key / renderType / ts / durMs / state / id / src / origin——空串 = 不带）</param>
        public void PushView(string op, string payload, string meta)
        {
            object payloadObj = "";
            if (payload != null && payload.Length > 0)
            {
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
                    LogStore.Add("CatHome4", 2, "视图载荷解析失败（按原文）: " + ex.Message, "TOOL");
                    payloadObj = payload;
                }
            }
            Dictionary<string, object> ev = new Dictionary<string, object>();
            ev["op"] = op;
            ev["payload"] = payloadObj;
            MergeViewMeta(ev, meta);
            PushEvent("view", JsonUtil.Serialize(ev));
        }

        /// <summary>
        /// 块元数据并入视图事件（A157）——meta 是 JSON 对象串时逐键并入事件顶层（同名键以 meta 为准）。
        /// 解析失败或非对象：出声并忽略——元数据缺失可见，不阻断事件推送。
        /// </summary>
        /// <param name="ev">视图事件字典</param>
        /// <param name="meta">块元数据 JSON（空串 = 无）</param>
        private static void MergeViewMeta(Dictionary<string, object> ev, string meta)
        {
            if (meta == null || meta.Length == 0)
            {
                return;
            }
            try
            {
                using (JsonDocument doc = JsonUtil.ParseStrict(meta))
                {
                    JsonElement root = doc.RootElement;
                    if (root.ValueKind != JsonValueKind.Object)
                    {
                        LogStore.Add("CatHome4", 2, "视图块元数据非对象——已忽略: " + meta, "TOOL");
                        return;
                    }
                    foreach (JsonProperty prop in root.EnumerateObject())
                    {
                        // Clone——值节点独立于 JsonDocument：using 结束释放 doc 后仍可序列化。
                        // 未 Clone 的 JsonElement 逃出作用域 → 序列化时 ObjectDisposedException
                        // （2026-10-02 判例：视图事件推送即崩，exit 0xE0434352）
                        ev[prop.Name] = prop.Value.Clone();
                    }
                }
            }
            catch (Exception ex)
            {
                LogStore.Add("CatHome4", 2, "视图块元数据解析失败（已忽略）: " + ex.Message, "TOOL");
            }
        }
    }
}