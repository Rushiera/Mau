using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Mau.Runtime;

namespace Mau.Providers
{
    /// <summary>
    /// DeepSeek 非流式/流式适配器——内建最小实现（OpenAI 兼容 /chat/completions）。
    /// 非流式：宿主构造时注入 baseUrl/apiKey/model；流式：ConfigStore 拉起（P4 配置项机制）。
    /// </summary>
    public sealed class DeepSeekLlmRuntime : ILlmRuntime
    {
        /// <summary>
        /// HTTP 客户端——每实例独立（超时互不干扰）
        /// </summary>
        private readonly HttpClient _client;

        /// <summary>
        /// API 基址——如 https://api.deepseek.com
        /// </summary>
        private readonly string _baseUrl;

        /// <summary>
        /// API 密钥
        /// </summary>
        private readonly string _apiKey;

        /// <summary>
        /// 模型名
        /// </summary>
        private readonly string _model;

        /// <summary>
        /// 默认兜底模型名——官方现役 v4-pro / v4-flash（deepseek-chat 已废弃）
        /// </summary>
        private const string FallbackModel = "deepseek-v4-flash";

        /// <summary>
        /// 思考模式开关——true=思考模式（默认）；配置 llm.thinking 控制
        /// </summary>
        private readonly bool _thinkingEnabled;

        /// <summary>
        /// 推理强度——low/high/max（默认 high；配置 llm.reasoning_effort 控制）
        /// </summary>
        private readonly string _reasoningEffort;

        /// <summary>
        /// 配置存储——运行期实时读取（P7 热载：配置以本地持久化为准，构造期快照退役）
        /// </summary>
        private readonly ConfigStore? _config;

        /// <summary>
        /// 建立非流式适配器
        /// </summary>
        /// <param name="baseUrl">API 基址</param>
        /// <param name="apiKey">API 密钥</param>
        /// <param name="model">模型名</param>
        public DeepSeekLlmRuntime(string baseUrl, string apiKey, string model)
        {
            _config = null;
            _baseUrl = baseUrl;
            if (_baseUrl == null)
            {
                _baseUrl = "";
            }
            _apiKey = apiKey;
            if (_apiKey == null)
            {
                _apiKey = "";
            }
            _model = model;
            if (string.IsNullOrEmpty(_model))
            {
                _model = FallbackModel;
            }
            _thinkingEnabled = true;
            _reasoningEffort = "high";
            _client = new HttpClient();
            _client.Timeout = TimeSpan.FromSeconds(60);
        }

        /// <summary>
        /// 从配置存储建立适配器——base_url/api_key/model/thinking/reasoning_effort 从文件拉起（P4 配置项机制）。
        /// 拉取优先级（Mau 全局环境变量兜底——2026-08-17 拍板）：配置文件 llm.* → Mau 全局环境变量 MAU_LLM_* → 兼容/默认。
        /// key 优先级：配置 llm.api_key → MAU_LLM_API_KEY → DEEPSEEK_API_KEY（兼容） → 空串（ERR 路径）。
        /// </summary>
        /// <param name="config">配置存储（llm.* 键段）</param>
        public DeepSeekLlmRuntime(ConfigStore config)
        {
            // [段1] 配置存储持有——运行期实时读取（P7 热载拍板：配置以本地持久化为准，构造期快照退役）
            _config = config;
            _baseUrl = "";
            _apiKey = "";
            _model = "";
            _thinkingEnabled = true;
            _reasoningEffort = "high";
            _client = new HttpClient();
            _client.Timeout = TimeSpan.FromSeconds(60);
        }

        /// <summary>
        /// SSE 帧翻译——增量事件流（零 catch：畸形帧/缺 [DONE] 全部事件化；取消异常冒泡给调用方）。
        /// [DONE] 到达即终止（忽略余帧——实测 [DONE] 后可能有余帧）。
        /// </summary>
        /// <param name="parser">SseParser 实例</param>
        /// <param name="ct">取消令牌</param>
        /// <returns>流式事件序列</returns>
        private static async IAsyncEnumerable<LlmStreamEvent> TranslateSse(SseParser<string> parser, [EnumeratorCancellation] CancellationToken ct)
{
            bool done = false;
            // [段0] 工具调用聚合——按 index 累积（design A.5：id/name 仅首帧；arguments 是累积增量需拼接后整体解析）
            Dictionary<int, string> toolIds = new Dictionary<int, string>();
            Dictionary<int, string> toolNames = new Dictionary<int, string>();
            Dictionary<int, System.Text.StringBuilder> toolArgs = new Dictionary<int, System.Text.StringBuilder>();
            List<int> toolOrder = new List<int>();
            await foreach (SseItem<string> item in parser.EnumerateAsync(ct))
            {
                // [段1] [DONE] 哨兵——唯一可信终止；忽略余帧
                if (item.Data == "[DONE]")
                {
                    done = true;
                    break;
                }
                // [段2] 帧解析——delta.content/reasoning_content 判空再产事件（空首帧不开块；空 delta 帧跳过）
                string text;
                string reasoning;
                bool parsed = TryParseDelta(item.Data, out text, out reasoning);
                if (parsed)
                {
                    if (text.Length > 0)
                    {
                        yield return new LlmStreamEvent(LlmStreamKind.Text, text);
                    }
                    if (reasoning.Length > 0)
                    {
                        yield return new LlmStreamEvent(LlmStreamKind.Reasoning, reasoning);
                    }
                    // [段2b] tool_calls 增量——按 index 聚合（与文本/思考同帧可并存）
                    AccumulateToolCalls(item.Data, toolIds, toolNames, toolArgs, toolOrder);
                }
            }
            // [段3] 完整性检查——无 [DONE] 提前结束 = STREAM_CLOSED（模型调用不可信，按失败处理）
            if (!done)
            {
                yield return new LlmStreamEvent(LlmStreamKind.Error, "ERR|STREAM_CLOSED|SSE 流未以 [DONE] 结束");
                yield break;
            }
            // [段4] 工具调用完整列表——finish 后一次性发出（聚合后的 arguments 为完整 JSON——消费方整体解析）
            if (toolOrder.Count > 0)
            {
                yield return new LlmStreamEvent(LlmStreamKind.ToolCalls, BuildToolCallsJson(toolIds, toolNames, toolArgs, toolOrder));
            }
            yield return new LlmStreamEvent(LlmStreamKind.Done, "");
        }
        /// <summary>
        /// 解析 SSE 帧 JSON——提取 choices[0].delta.content / reasoning_content。
        /// 畸形帧返回 false（跳过不产事件）；choices 空数组返回 false（usage-only 尾帧）。
        /// </summary>
        /// <param name="data">帧 data 载荷</param>
        /// <param name="text">回复增量（无则空串）</param>
        /// <param name="reasoning">思考增量（无则空串）</param>
        /// <returns>true=解析成功（增量可能为空——调用方判空再产事件）</returns>
        private static bool TryParseDelta(string data, out string text, out string reasoning)
        {
            text = "";
            reasoning = "";
            if (data == null || data.Length == 0)
            {
                return false;
            }
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(data))
                {
                    JsonElement root = doc.RootElement;
                    JsonElement choices;
                    if (!root.TryGetProperty("choices", out choices) || choices.GetArrayLength() == 0)
                    {
                        return false;
                    }
                    JsonElement first = choices[0];
                    JsonElement delta;
                    if (!first.TryGetProperty("delta", out delta))
                    {
                        return false;
                    }
                    JsonElement value;
                    if (delta.TryGetProperty("content", out value) && value.ValueKind == JsonValueKind.String)
                    {
                        string? gotText = value.GetString();
                        if (gotText != null)
                        {
                            text = gotText;
                        }
                    }
                    if (delta.TryGetProperty("reasoning_content", out value) && value.ValueKind == JsonValueKind.String)
                    {
                        string? gotReasoning = value.GetString();
                        if (gotReasoning != null)
                        {
                            reasoning = gotReasoning;
                        }
                    }
                    return true;
                }
            }
            catch
            {
                // 畸形 JSON 帧——调用方跳过（MALFORMED 帧不做硬失败，容忍上游抖动）
                return false;
            }
        }

        /// <summary>
        /// 解析错误响应——错误 JSON 双形态兜底（官方 error{type,code,message} / 代理 type+error.type）。
        /// 格式：ERR|HTTP_{码}|{type}|{message}（截断 200 字防刷屏）。
        /// </summary>
        /// <param name="statusCode">HTTP 状态码</param>
        /// <param name="raw">响应体</param>
        /// <returns>错误文本</returns>
        private static string ParseErrorText(int statusCode, string raw)
        {
            // [段1] 解析错误 JSON——error.type/error.message 或外层 type
            string type = "";
            string message = "";
            if (raw != null && raw.Length > 0)
            {
                try
                {
                    using (JsonDocument doc = JsonDocument.Parse(raw))
                    {
                        JsonElement root = doc.RootElement;
                        JsonElement err;
                        if (root.TryGetProperty("error", out err) && err.ValueKind == JsonValueKind.Object)
                        {
                            JsonElement value;
                            if (err.TryGetProperty("type", out value) && value.ValueKind == JsonValueKind.String)
                            {
                                type = value.GetString() ?? "";
                            }
                            if (err.TryGetProperty("message", out value) && value.ValueKind == JsonValueKind.String)
                            {
                                message = value.GetString() ?? "";
                            }
                        }
                        else
                        {
                            JsonElement value;
                            if (root.TryGetProperty("type", out value) && value.ValueKind == JsonValueKind.String)
                            {
                                type = value.GetString() ?? "";
                            }
                        }
                    }
                }
                catch
                {
                    message = TrimText(raw, 200);
                }
            }
            // [段2] 兜底——无 type 用 HTTP 码；无 message 用原文
            if (type.Length == 0)
            {
                type = "HTTP_" + statusCode.ToString();
            }
            string detail;
            if (message.Length > 0)
            {
                detail = message;
            }
            else if (raw == null)
            {
                detail = "";
            }
            else
            {
                detail = raw;
            }
            return "ERR|" + type + "|" + TrimText(detail, 200);
        }

        /// <summary>
        /// 截断错误详情——防止超长错误文本刷屏
        /// </summary>
        /// <param name="text">原文</param>
        /// <param name="max">上限</param>
        /// <returns>截断文本</returns>
        private static string TrimText(string text, int max)
        {
            if (text == null)
            {
                return "";
            }
            if (text.Length <= max)
            {
                return text;
            }
            return text.Substring(0, max);
        }
/// <summary>
/// 流式对话完成——消息序列 + 工具定义 → 事件流（P5：OpenAI 兼容 tool_calls）。
/// Text/Reasoning 增量；ToolCalls 完整工具调用 JSON（聚合后一次性发出）；[DONE] → Done；错误 → Error。
/// 注意：C# 迭代器禁止 try-catch 内 yield——网络层错误用 catch 赋值 + catch 后 yield 模式。
/// </summary>
/// <param name = "messages">完整消息序列（system/user/assistant/tool 多 role）</param>
/// <param name = "tools">工具定义数组（可为空——纯对话）</param>
/// <param name = "ct">取消令牌</param>
/// <returns>流式事件序列</returns>
public async IAsyncEnumerable<LlmStreamEvent> ChatStream(LlmMessage[] messages, ToolSpec[] tools, [EnumeratorCancellation] CancellationToken ct = default)
{
    // [段1] 构造流式请求体并发送（ResponseHeadersRead——流式读取）
    string body = BuildChatRequestBody(messages, tools);
    using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, GetBaseUrl().TrimEnd('/') + "/chat/completions"))
    {
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + GetApiKey());
        request.Headers.TryAddWithoutValidation("Accept", "text/event-stream");
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        HttpResponseMessage? response = null;
        string netError = "";
        try
        {
            response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (Exception ex)
        {
            netError = "ERR|TRANSPORT|" + ex.GetType().Name + "|" + ex.Message;
        }

        if (netError.Length > 0)
        {
            yield return new LlmStreamEvent(LlmStreamKind.Error, netError);
            yield break;
        }

        using (response)
        {
            // [段2] HTTP 层失败——错误 JSON 双形态解析（error.type/message 兜底，HTTP 码不可作唯一判据）
            if (response == null)
            {
                yield return new LlmStreamEvent(LlmStreamKind.Error, "ERR|TRANSPORT|空响应");
                yield break;
            }

            if (!response.IsSuccessStatusCode)
            {
                string? raw = "";
                string readError = "";
                try
                {
                    raw = await response.Content.ReadAsStringAsync(ct);
                }
                catch (Exception ex)
                {
                    readError = "ERR|TRANSPORT|" + ex.GetType().Name + "|" + ex.Message;
                }

                if (readError.Length > 0)
                {
                    yield return new LlmStreamEvent(LlmStreamKind.Error, readError);
                    yield break;
                }

                string rawText;
                if (raw == null)
                {
                    rawText = "";
                }
                else
                {
                    rawText = raw;
                }

                yield return new LlmStreamEvent(LlmStreamKind.Error, ParseErrorText((int)response.StatusCode, rawText));
                yield break;
            }

            // [段3] SSE 解析——官方 SseParser（注释行/多行 data/空行分隔自动处理）
            Stream? stream = null;
            string streamError = "";
            try
            {
                stream = await response.Content.ReadAsStreamAsync(ct);
            }
            catch (Exception ex)
            {
                streamError = "ERR|TRANSPORT|" + ex.GetType().Name + "|" + ex.Message;
            }

            if (streamError.Length > 0)
            {
                yield return new LlmStreamEvent(LlmStreamKind.Error, streamError);
                yield break;
            }

            SseParser<string> parser = SseParser.Create(stream!);
            // [段4] translate——独立迭代器（零 catch：所有错误事件化；tool_calls 聚合在 translate 内）
            await foreach (LlmStreamEvent ev in TranslateSse(parser, ct))
            {
                yield return ev;
            }
        }
    }
}    /// <summary>
/// assistant 消息序列化——tool_calls JSON 原样透传 + reasoning_content 回传铁律。
/// 规则（A.6 ①⑦）：有 tool_calls 必带 reasoning_content（含空串）；无 tool_calls 但保留思考也带（多轮保留）。
/// </summary>
/// <param name = "m">assistant 消息</param>
/// <returns>wire 消息对象</returns>
private static object BuildAssistantMessage(LlmMessage m)
{
    Dictionary<string, object> wire = new Dictionary<string, object>();
    wire["role"] = "assistant";
    wire["content"] = m.Content;
    bool hasTools = m.ToolCallsJson.Length > 0;
    if (hasTools)
    {
        using (JsonDocument doc = JsonDocument.Parse(m.ToolCallsJson))
        {
            wire["tool_calls"] = doc.RootElement.Clone();
        }
    }

    if (hasTools)
    {
        wire["reasoning_content"] = m.ReasoningContent;
    }
    else if (m.ReasoningContent.Length > 0)
    {
        wire["reasoning_content"] = m.ReasoningContent;
    }

    return wire;
}/// <summary>
/// tools 数组序列化——OpenAI function 定义；parameters JSON Schema 原样透传（空参数 = 空对象 schema）。
/// </summary>
/// <param name = "tools">工具规格数组</param>
/// <returns>wire tools 数组</returns>
private static object[] BuildWireTools(ToolSpec[] tools)
{
    if (tools == null || tools.Length == 0)
    {
        return new object[0];
    }

    object[] result = new object[tools.Length];
    for (int i = 0; i < tools.Length; i++)
    {
        ToolSpec spec = tools[i];
        Dictionary<string, object> function = new Dictionary<string, object>();
        function["name"] = spec.Name;
        function["description"] = spec.Description;
        if (spec.ParametersJson.Length > 0)
        {
            using (JsonDocument doc = JsonDocument.Parse(spec.ParametersJson))
            {
                function["parameters"] = doc.RootElement.Clone();
            }
        }
        else
        {
            function["parameters"] = new
            {
                type = "object",
                properties = new object ()
            };
        }

        Dictionary<string, object> tool = new Dictionary<string, object>();
        tool["type"] = "function";
        tool["function"] = function;
        result[i] = tool;
    }

    return result;
}/// <summary>
/// 构造流式对话请求体——OpenAI 兼容消息序列 + tools（P5：接口端零创新，wire 标准）。
/// system/user 文本直写；assistant 带 tool_calls（JSON 透传）+ reasoning_content（A.6 ①⑦ 回传铁律）；
/// tool 独立消息（tool_call_id 配对）；思考模式 + effort 按配置；空 tools 省略字段。
/// </summary>
/// <param name = "messages">消息序列</param>
/// <param name = "tools">工具定义数组</param>
/// <returns>请求体 JSON</returns>
private string BuildChatRequestBody(LlmMessage[] messages, ToolSpec[] tools)
{
            // [段1] 消息数组——多 role 序列化（null 字段防御归一——外部消息来源可能带 null）
            List<object> wireMessages = new List<object>();
            for (int i = 0; i < messages.Length; i++)
            {
                LlmMessage m = messages[i];
                if (m.Content == null)
                {
                    m.Content = "";
                }
                if (m.ToolCallId == null)
                {
                    m.ToolCallId = "";
                }
                if (m.ToolCallsJson == null)
                {
                    m.ToolCallsJson = "";
                }
                if (m.ReasoningContent == null)
                {
                    m.ReasoningContent = "";
                }
                if (m.Role == LlmRole.System)
                {
                    wireMessages.Add(new { role = "system", content = m.Content });
                }
                else if (m.Role == LlmRole.User)
                {
                    wireMessages.Add(new { role = "user", content = m.Content });
                }
                else if (m.Role == LlmRole.Assistant)
                {
                    wireMessages.Add(BuildAssistantMessage(m));
                }
                else if (m.Role == LlmRole.Tool)
                {
                    string toolContent = m.Content;
                    if (toolContent.Length == 0)
                    {
                        toolContent = "(no output)";
                    }
                    wireMessages.Add(new { role = "tool", tool_call_id = m.ToolCallId, content = toolContent });
                }
            }
            // [段2] tools 数组——OpenAI function 定义（空数组省略字段——省略原则）
            object[] wireTools = BuildWireTools(tools);
            // [段3] 请求体——思考模式 enabled 带 effort；disabled 关闭思考；tools 非空才带
            Dictionary<string, object> payload = new Dictionary<string, object>();
            payload["model"] = GetModel();
            payload["messages"] = wireMessages;
            payload["stream"] = true;
            if (GetThinkingEnabled())
            {
                payload["thinking"] = new { type = "enabled" };
                payload["reasoning_effort"] = GetReasoningEffort();
            }
            else
            {
                payload["thinking"] = new { type = "disabled" };
            }
            if (wireTools.Length > 0)
            {
                payload["tools"] = wireTools;
            }
            return JsonSerializer.Serialize(payload);
        }

        /// <summary>
        /// 实时读取 API 基址——配置 llm.base_url → MAU_LLM_BASE_URL → 注入值/空串（P7 热载：每次调用取当前值）
        /// </summary>
        /// <returns>API 基址</returns>
        private string GetBaseUrl()
        {
            if (_config != null)
            {
                string value = _config.Get("llm.base_url", "");
                if (value.Length > 0)
                {
                    return value;
                }
                string? env = Environment.GetEnvironmentVariable("MAU_LLM_BASE_URL");
                if (!string.IsNullOrEmpty(env))
                {
                    return env;
                }
            }
            return _baseUrl;
        }

        /// <summary>
        /// 实时读取 API 密钥——配置 llm.api_key → MAU_LLM_API_KEY → DEEPSEEK_API_KEY（兼容） → 注入值/空串
        /// </summary>
        /// <returns>API 密钥</returns>
        private string GetApiKey()
        {
            if (_config != null)
            {
                string value = _config.Get("llm.api_key", "");
                if (value.Length > 0)
                {
                    return value;
                }
                string? env = Environment.GetEnvironmentVariable("MAU_LLM_API_KEY");
                if (!string.IsNullOrEmpty(env))
                {
                    return env;
                }
                env = Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY");
                if (!string.IsNullOrEmpty(env))
                {
                    return env;
                }
            }
            return _apiKey;
        }

        /// <summary>
        /// 实时读取模型名——配置 llm.model → MAU_LLM_MODEL → 注入值 → 兜底模型
        /// </summary>
        /// <returns>模型名</returns>
        private string GetModel()
        {
            if (_config != null)
            {
                string value = _config.Get("llm.model", "");
                if (value.Length > 0)
                {
                    return value;
                }
                string? env = Environment.GetEnvironmentVariable("MAU_LLM_MODEL");
                if (!string.IsNullOrEmpty(env))
                {
                    return env;
                }
            }
            if (string.IsNullOrEmpty(_model))
            {
                return FallbackModel;
            }
            return _model;
        }

        /// <summary>
        /// 实时读取思考模式开关——配置 llm.thinking → MAU_LLM_THINKING → 默认 enabled
        /// </summary>
        /// <returns>true=思考模式</returns>
        private bool GetThinkingEnabled()
        {
            if (_config != null)
            {
                string value = _config.Get("llm.thinking", "");
                if (value.Length == 0)
                {
                    string? env = Environment.GetEnvironmentVariable("MAU_LLM_THINKING");
                    if (!string.IsNullOrEmpty(env))
                    {
                        value = env;
                    }
                }
                if (value.Length == 0)
                {
                    value = "enabled";
                }
                return value == "enabled";
            }
            return _thinkingEnabled;
        }

        /// <summary>
        /// 实时读取推理强度——配置 llm.reasoning_effort → MAU_LLM_REASONING_EFFORT → 默认 high
        /// </summary>
        /// <returns>推理强度 low/high/max</returns>
        private string GetReasoningEffort()
        {
            if (_config != null)
            {
                string value = _config.Get("llm.reasoning_effort", "");
                if (value.Length == 0)
                {
                    string? env = Environment.GetEnvironmentVariable("MAU_LLM_REASONING_EFFORT");
                    if (!string.IsNullOrEmpty(env))
                    {
                        value = env;
                    }
                }
                if (value.Length == 0)
                {
                    value = "high";
                }
                return value;
            }
            return _reasoningEffort;
        }/// <summary>
/// 累积 SSE 帧的 tool_calls 增量——按 index 聚合（design A.5：id/name 仅首帧；arguments 累积增量拼接）。
/// </summary>
/// <param name = "data">帧 data 载荷</param>
/// <param name = "ids">index → 调用 ID</param>
/// <param name = "names">index → 工具名</param>
/// <param name = "args">index → 参数拼接缓冲</param>
/// <param name = "order">index 出现顺序</param>
private static void AccumulateToolCalls(string data, Dictionary<int, string> ids, Dictionary<int, string> names, Dictionary<int, System.Text.StringBuilder> args, List<int> order)
{
    try
    {
        using (JsonDocument doc = JsonDocument.Parse(data))
        {
            JsonElement root = doc.RootElement;
            JsonElement choices;
            if (!root.TryGetProperty("choices", out choices) || choices.GetArrayLength() == 0)
            {
                return;
            }

            JsonElement delta;
            if (!choices[0].TryGetProperty("delta", out delta))
            {
                return;
            }

            JsonElement calls;
            if (!delta.TryGetProperty("tool_calls", out calls) || calls.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            for (int i = 0; i < calls.GetArrayLength(); i++)
            {
                JsonElement call = calls[i];
                JsonElement indexEl;
                int index;
                if (!call.TryGetProperty("index", out indexEl) || !indexEl.TryGetInt32(out index))
                {
                    continue;
                }

                System.Text.StringBuilder? builder;
                if (!args.TryGetValue(index, out builder) || builder == null)
                {
                    builder = new System.Text.StringBuilder();
                    args[index] = builder;
                    ids[index] = "";
                    names[index] = "";
                    order.Add(index);
                }

                JsonElement idEl;
                if (ids[index].Length == 0 && call.TryGetProperty("id", out idEl) && idEl.ValueKind == JsonValueKind.String)
                {
                    string? gotId = idEl.GetString();
                    if (gotId != null)
                    {
                        ids[index] = gotId!;
                    }
                }

                JsonElement funcEl;
                if (call.TryGetProperty("function", out funcEl))
                {
                    if (names[index].Length == 0)
                    {
                        JsonElement nameEl;
                        if (funcEl.TryGetProperty("name", out nameEl) && nameEl.ValueKind == JsonValueKind.String)
                        {
                            string? gotName = nameEl.GetString();
                            if (gotName != null)
                            {
                                names[index] = gotName!;
                            }
                        }
                    }

                    JsonElement argsEl;
                    if (funcEl.TryGetProperty("arguments", out argsEl) && argsEl.ValueKind == JsonValueKind.String)
                    {
                        string? gotArgs = argsEl.GetString();
                        if (gotArgs != null)
                        {
                            builder.Append(gotArgs);
                        }
                    }
                }
            }
        }
    }
    catch
    {
    // 畸形帧跳过——容忍上游抖动
    }
}/// <summary>
/// 聚合结果 → 完整 tool_calls JSON 数组（[{"id","name","arguments"}]——arguments 为完整 JSON 文本，消费方整体解析）。
/// </summary>
/// <param name = "ids">index → 调用 ID</param>
/// <param name = "names">index → 工具名</param>
/// <param name = "args">index → 参数拼接缓冲</param>
/// <param name = "order">index 出现顺序</param>
/// <returns>JSON 数组字符串</returns>
private static string BuildToolCallsJson(Dictionary<int, string> ids, Dictionary<int, string> names, Dictionary<int, System.Text.StringBuilder> args, List<int> order)
{
            System.Text.StringBuilder builder = new System.Text.StringBuilder();
            builder.Append("[");
            for (int i = 0; i < order.Count; i++)
            {
                int index = order[i];
                if (i > 0)
                {
                    builder.Append(",");
                }
                // OpenAI wire 标准：{"id","type":"function","function":{"name","arguments"}}——function 为嵌套对象（判例：missing field function）
                builder.Append("{\"id\":");
                builder.Append(JsonSerializer.Serialize(ids[index]));
                builder.Append(",\"type\":\"function\",\"function\":{\"name\":");
                builder.Append(JsonSerializer.Serialize(names[index]));
                builder.Append(",\"arguments\":");
                builder.Append(JsonSerializer.Serialize(args[index].ToString()));
                builder.Append("}}");
            }
            builder.Append("]");
            return builder.ToString();
        }}
}
