using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace Mau.Runtime
{
    /// <summary>
    /// LLM 会话管理（程序级）——会话表经 DataBox scope 存储（BRIK 唯一数据协议）+ SSE 解析
    /// </summary>
    public static class LlmSession
    {
        /// <summary>
        /// 会话表——DataBox scope "llm"
        /// </summary>
        private static ConcurrentDictionary<string, LlmStreamSession> Sessions
        {
            get { return DataBox.GetOrCreate<ConcurrentDictionary<string, LlmStreamSession>>("llm", "sessions"); }
        }

        /// <summary>
        /// 流式会话序号——requestId 生成源
        /// </summary>
        private static int _sessionSeq = 0;

        /// <summary>
        /// 创建会话并注册
        /// </summary>
        /// <param name="requestId">生成的会话 ID</param>
        /// <returns>会话（注册失败返回 null）</returns>
        public static LlmStreamSession? CreateSession(out string requestId)
        {
            string id = "LLM_" + Interlocked.Increment(ref _sessionSeq).ToString();
            LlmStreamSession session = new LlmStreamSession(LlmBridge.Timeout);
            if (!Sessions.TryAdd(id, session))
            {
                session.Cancel.Dispose();
                requestId = "";
                return null;
            }
            requestId = id;
            return session;
        }

        /// <summary>
        /// 查找会话
        /// </summary>
        /// <param name="requestId">会话 ID</param>
        /// <returns>会话或 null</returns>
        public static LlmStreamSession? FindSession(string requestId)
        {
            LlmStreamSession? session;
            if (Sessions.TryGetValue(BrickText.SafeText(requestId), out session)
                && session != null)
            {
                return session;
            }
            return null;
        }

        /// <summary>
        /// 移除并终止会话
        /// </summary>
        /// <param name="requestId">会话 ID</param>
        /// <returns>是否找到</returns>
        public static bool RemoveSession(string requestId)
        {
            LlmStreamSession? session;
            if (!Sessions.TryRemove(BrickText.SafeText(requestId), out session)
                || session == null)
            {
                return false;
            }
            session.Cancel.Cancel();
            System.Threading.Tasks.Task? worker = session.Worker;
            if (worker != null)
            {
                try
                {
                    worker.Wait(2000);
                }
                catch (AggregateException)
                {
                    // 后台任务异常已入队终态分片——等待超时不影响清理
                }
            }
            session.Cancel.Dispose();
            return true;
        }
/// <summary>
/// 是否存在活跃 LLM 会话——流式进行中判定源（llm.finish 移除会话后返回 false）
/// </summary>
/// <returns>存在活跃会话为真</returns>
public static bool HasActiveSession()
{
    return !Sessions.IsEmpty;
}
/// <summary>
/// 活跃会话清单快照——GAP.7 sys.llm 查询源（requestId → 会话；ConcurrentDictionary 枚举线程安全）
/// </summary>
/// <returns>会话键值对数组</returns>
public static KeyValuePair<string, LlmStreamSession>[] GetActiveSessions()
{
    List<KeyValuePair<string, LlmStreamSession>> list = new List<KeyValuePair<string, LlmStreamSession>>();
    foreach (KeyValuePair<string, LlmStreamSession> pair in Sessions)
    {
        list.Add(pair);
    }

    return list.ToArray();
}        /// <summary>
        /// 入队终态分片——Interlocked 保证只入队一次
        /// </summary>
        /// <param name="session">会话</param>
        /// <param name="errorCode">错误码（成功为空）</param>
        /// <param name="errorMessage">错误摘要（成功为空）</param>
        /// <param name="toolCallsJson">工具调用聚合 JSON（可空）</param>
        public static void PushTerminal(LlmStreamSession session,
            string errorCode, string errorMessage, string toolCallsJson)
        {
            if (Interlocked.Exchange(ref session.IsTerminal, 1) != 0)
            {
                return;
            }
            // 🔴 LLM 轮次结束观测（2026-08-12 诊断补透明性——LLM 是否发起工具调用不可见）：
            //   llm.terminal 事件落盘——toolCalls 摘要（前 300 字符）+ 内容长度 + usage + 错误码
            //   定位"LLM 未调用工具" vs "调用被下游丢弃"
            //   🔴 观测零副作用——try-catch 保护：埋点异常绝不阻断终态分片入队（2026-08-12 测试卡死教训）
            try
            {
                AuditStore.Default?.Record("LlmSession", "llm.terminal", -1, new AuditProp[] {
                    new AuditProp("errorCode", errorCode),
                    new AuditProp("contentChars", session.ContentChars.ToString()),
                    new AuditProp("toolCalls", toolCallsJson.Length > 300
                        ? toolCallsJson.Substring(0, 300) : toolCallsJson),
                    new AuditProp("usage", session.UsagePrompt.ToString() + "/"
                        + session.UsageCompletion.ToString() + "/"
                        + session.UsageCacheHit.ToString())
                });
            }
            catch (Exception)
            {
                // 观测失败不影响主链路
            }
            // 结束时间戳——轮次统计耗时基准（Stopwatch 精度；2026-08-10）
            session.FinishedTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
            LlmStreamChunk chunk = new LlmStreamChunk();
            chunk.Finished = true;
            chunk.ErrorCode = errorCode;
            chunk.ErrorMessage = errorMessage;
            chunk.ToolCallsJson = toolCallsJson;
            session.Chunks.Enqueue(chunk);
        }

        /// <summary>
        /// 逐行读取 SSE——分片入队，终态保证只入队一次
        /// </summary>
        /// <param name="reader">响应读取器</param>
        /// <param name="session">会话</param>
        /// <returns>异步工作</returns>
        public static async System.Threading.Tasks.Task ReadStreamEventsAsync(
            StreamReader reader, LlmStreamSession session)
        {
            bool sawTerminalMarker = false;
            bool sawFinishReason = false;
            Dictionary<int, LlmToolCallBuilder> toolCalls = new Dictionary<int, LlmToolCallBuilder>();
            while (!reader.EndOfStream)
            {
                string? line = await reader.ReadLineAsync(session.Cancel.Token)
                    .ConfigureAwait(false);
                if (line == null)
                {
                    break;
                }
                if (!line.StartsWith("data:", StringComparison.Ordinal))
                {
                    continue;
                }
                string payload = line.Substring(5).Trim();
                if (payload == "[DONE]")
                {
                    sawTerminalMarker = true;
                    break;
                }
                if (payload.Length > 0)
                {
                    bool sawFinish;
                    ParseStreamEvent(payload, session, toolCalls, out sawFinish);
                    if (sawFinish)
                    {
                        sawFinishReason = true;
                    }
                }
            }
            if (!sawTerminalMarker && !sawFinishReason)
            {
                PushTerminal(session, "LLM_STREAM_INCOMPLETE",
                    "LLM stream ended unexpectedly.", "");
                return;
            }
            PushTerminal(session, "", "", BuildToolCallsJson(toolCalls));
        }

        /// <summary>
        /// 解析单个 OpenAI 兼容 SSE 事件
        /// </summary>
        /// <param name="payload">data 字段 JSON</param>
        /// <param name="session">会话</param>
        /// <param name="toolCalls">工具调用聚合表</param>
        /// <param name="sawFinish">是否观察到 finish_reason</param>
        private static void ParseStreamEvent(string payload, LlmStreamSession session,
            Dictionary<int, LlmToolCallBuilder> toolCalls, out bool sawFinish)
{
            sawFinish = false;
            using (System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(payload))
            {
                // usage——最后一条 chunk 携带 token 统计（轮次统计 llm.round_stats_text 数据源）
                System.Text.Json.JsonElement usageElement;
                if (document.RootElement.TryGetProperty("usage", out usageElement)
                    && usageElement.ValueKind == System.Text.Json.JsonValueKind.Object)
                {
                    long prompt = ReadLong(usageElement, "prompt_tokens");
                    if (prompt > 0) { session.UsagePrompt = prompt; }
                    long completion = ReadLong(usageElement, "completion_tokens");
                    if (completion > 0) { session.UsageCompletion = completion; }
                    long cacheHit = ReadLong(usageElement, "prompt_cache_hit_tokens");
                    if (cacheHit > 0) { session.UsageCacheHit = cacheHit; }
                }
                System.Text.Json.JsonElement choices;
                if (!document.RootElement.TryGetProperty("choices", out choices)
                    || choices.ValueKind != System.Text.Json.JsonValueKind.Array
                    || choices.GetArrayLength() == 0)
                {
                    return;
                }
                System.Text.Json.JsonElement choice = choices[0];
                System.Text.Json.JsonElement finish;
                if (choice.TryGetProperty("finish_reason", out finish)
                    && finish.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    sawFinish = true;
                }
                System.Text.Json.JsonElement delta;
                if (!choice.TryGetProperty("delta", out delta)
                    || delta.ValueKind != System.Text.Json.JsonValueKind.Object)
                {
                    return;
                }
                string content = ReadString(delta, "content");
                string reasoning = ReadString(delta, "reasoning_content");
                System.Text.Json.JsonElement toolCallsElement;
                if (delta.TryGetProperty("tool_calls", out toolCallsElement)
                    && toolCallsElement.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    AppendToolCalls(toolCallsElement, toolCalls);
                }
                if (content.Length > 0 || reasoning.Length > 0)
                {
                    if (content.Length > 0)
                    {
                        // 空回复检测源——正文累计（2026-08-10 续传链路）
                        session.ContentChars = session.ContentChars + content.Length;
                        // D.2 分片合并——完整正文累积（BRIK-LLM-027 ctx_push_stream 数据源；2026-08-11）
                        session.ContentBuilder.Append(content);
                    }
                    if (reasoning.Length > 0)
                    {
                        // G.2 思考显示——完整推理累积（2026-08-11）
                        session.ReasoningBuilder.Append(reasoning);
                    }
                    LlmStreamChunk chunk = new LlmStreamChunk();
                    chunk.ContentDelta = content;
                    chunk.ReasoningDelta = reasoning;
                    session.Chunks.Enqueue(chunk);
                }
            }
        }
        /// <summary>
        /// 聚合事件内的工具调用分片
        /// </summary>
        /// <param name="calls">tool_calls 数组</param>
        /// <param name="toolCalls">聚合表</param>
        private static void AppendToolCalls(System.Text.Json.JsonElement calls,
            Dictionary<int, LlmToolCallBuilder> toolCalls)
        {
            foreach (System.Text.Json.JsonElement call in calls.EnumerateArray())
            {
                System.Text.Json.JsonElement indexValue;
                if (!call.TryGetProperty("index", out indexValue)
                    || indexValue.ValueKind != System.Text.Json.JsonValueKind.Number)
                {
                    continue;
                }
                int index = indexValue.GetInt32();
                LlmToolCallBuilder? builder;
                if (!toolCalls.TryGetValue(index, out builder) || builder == null)
                {
                    builder = new LlmToolCallBuilder();
                    toolCalls.Add(index, builder);
                }
                builder.Append(call);
            }
        }

        /// <summary>
        /// 按 index 顺序生成工具调用 JSON 数组
        /// </summary>
        /// <param name="toolCalls">聚合表</param>
        /// <returns>JSON 数组或空串</returns>
        private static string BuildToolCallsJson(
            Dictionary<int, LlmToolCallBuilder> toolCalls)
        {
            if (toolCalls.Count == 0)
            {
                return "";
            }
            List<int> indexes = new List<int>(toolCalls.Keys);
            indexes.Sort();
            using MemoryStream stream = new MemoryStream();
            using (System.Text.Json.Utf8JsonWriter writer = new System.Text.Json.Utf8JsonWriter(stream))
            {
                writer.WriteStartArray();
                for (int i = 0; i < indexes.Count; i = i + 1)
                {
                    toolCalls[indexes[i]].WriteTo(writer);
                }
                writer.WriteEndArray();
            }
            return Encoding.UTF8.GetString(stream.ToArray());
        }

        /// <summary>
        /// 读取对象内的可选长整型属性（usage 解析）
        /// </summary>
        /// <param name="element">JSON 对象</param>
        /// <param name="name">属性名</param>
        /// <returns>长整型值或 0</returns>
        private static long ReadLong(System.Text.Json.JsonElement element, string name)
        {
            System.Text.Json.JsonElement value;
            if (element.TryGetProperty(name, out value)
                && value.ValueKind == System.Text.Json.JsonValueKind.Number)
            {
                long result;
                if (value.TryGetInt64(out result))
                {
                    return result;
                }
            }
            return 0;
        }

        /// <summary>
        /// 读取对象内的可选字符串属性
        /// </summary>
        /// <param name="element">JSON 对象</param>
        /// <param name="name">属性名</param>
        /// <returns>字符串或空串</returns>
        private static string ReadString(System.Text.Json.JsonElement element, string name)
        {
            System.Text.Json.JsonElement value;
            if (element.TryGetProperty(name, out value)
                && value.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                return BrickText.SafeText(value.GetString());
            }
            return "";
        }
    }

    /// <summary>
    /// 跨 SSE 事件累积单个工具调用字段
    /// </summary>
    internal sealed class LlmToolCallBuilder
    {
        /// <summary>
        /// 工具调用 ID
        /// </summary>
        private string _id;

        /// <summary>
        /// 工具名称累积
        /// </summary>
        private readonly StringBuilder _name;

        /// <summary>
        /// 参数 JSON 累积
        /// </summary>
        private readonly StringBuilder _arguments;

        /// <summary>
        /// 创建空分片聚合器
        /// </summary>
        internal LlmToolCallBuilder()
        {
            _id = "";
            _name = new StringBuilder();
            _arguments = new StringBuilder();
        }

        /// <summary>
        /// 追加一个工具调用 delta
        /// </summary>
        /// <param name="call">delta 对象</param>
        internal void Append(System.Text.Json.JsonElement call)
        {
            System.Text.Json.JsonElement idValue;
            if (call.TryGetProperty("id", out idValue)
                && idValue.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                _id = BrickText.SafeText(idValue.GetString());
            }
            System.Text.Json.JsonElement function;
            if (!call.TryGetProperty("function", out function)
                || function.ValueKind != System.Text.Json.JsonValueKind.Object)
            {
                return;
            }
            System.Text.Json.JsonElement nameValue;
            if (function.TryGetProperty("name", out nameValue)
                && nameValue.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                _name.Append(nameValue.GetString());
            }
            System.Text.Json.JsonElement argsValue;
            if (function.TryGetProperty("arguments", out argsValue)
                && argsValue.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                _arguments.Append(argsValue.GetString());
            }
        }

        /// <summary>
        /// 将完整工具调用写入 JSON
        /// </summary>
        /// <param name="writer">JSON 写入器</param>
        internal void WriteTo(System.Text.Json.Utf8JsonWriter writer)
        {
            writer.WriteStartObject();
            writer.WriteString("id", _id);
            writer.WriteString("type", "function");
            writer.WriteStartObject("function");
            writer.WriteString("name", _name.ToString());
            writer.WriteString("arguments", _arguments.ToString());
            writer.WriteEndObject();
            writer.WriteEndObject();
        }
    }
}
