// ═══════════════════════════════════════════════════
// 积木: llm.stream
// ID:   BRIK-LLM-002
// 类别: LLM
// 作用: 流式 LLM 调用——启动后台 SSE 请求，分片经 read_chunk 消费
// 依赖: 无
// 引用: System · System.IO · System.Net.Http · System.Net.Http.Headers · System.Text · System.Text.Json
// 原理: 创建会话 → 后台任务 SSE 解析（分片入队）→ ReadChunk 消费
// 常用: CH4 TalkCat 流式对话（P2.2）
// 时长: Streaming
// 线程: worker
// ═══════════════════════════════════════════════════
using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

using Mau.Runtime;
namespace Mau.Bricks
{
    /// <summary>
    /// LLM 积木——llm.stream 真流式调用（依赖 LlmBridge/LlmSession）
    /// </summary>
    public static class LlmStreamBrick
    {
        /// <summary>
        /// 流式 LLM 调用——启动后台 SSE 请求，分片经 ReadChunk 消费
        /// </summary>
        /// <param name="model">模型名</param>
        /// <param name="systemPrompt">System Prompt</param>
        /// <param name="userMessage">User 消息</param>
        /// <param name="toolsJson">工具定义 JSON 数组（空=无工具）</param>
        /// <param name="requestId">流式会话 ID——ReadChunk/Finish 用</param>
        /// <returns>true=启动成功</returns>
        public static bool Stream(string model, string systemPrompt,
            string userMessage, string toolsJson, out string requestId)
        {
            requestId = "";
            if (string.IsNullOrWhiteSpace(model))
            {
                model = "deepseek-chat";
            }
            LlmStreamSession? session = LlmSession.CreateSession(out requestId);
            if (session == null)
            {
                return false;
            }
            Task worker = RunStreamAsync(requestId, model, systemPrompt,
                userMessage, toolsJson, session);
            session.Worker = worker;
            return true;
        }

        /// <summary>
        /// 执行流式 HTTP 请求并解析 SSE——分片写入会话队列
        /// </summary>
        /// <param name="requestId">会话 ID</param>
        /// <param name="model">模型名</param>
        /// <param name="systemPrompt">System Prompt</param>
        /// <param name="userMessage">User 消息</param>
        /// <param name="toolsJson">工具定义 JSON</param>
        /// <param name="session">会话</param>
        /// <returns>异步工作</returns>
        private static async Task RunStreamAsync(string requestId,
            string model, string systemPrompt, string userMessage,
            string toolsJson, LlmStreamSession session)
        {
            try
            {
                string apiKey = LlmBridge.ApiKey;
                if (apiKey.Length == 0)
                {
                    LlmSession.PushTerminal(session, "LLM_CREDENTIAL_MISSING",
                        "LLM credential is not configured.", "");
                    return;
                }
                using (HttpRequestMessage message = BuildStreamRequest(model,
                    systemPrompt, userMessage, toolsJson, apiKey))
                using (HttpResponseMessage response = await LlmBridge.Http
                    .SendAsync(message, HttpCompletionOption.ResponseHeadersRead,
                        session.Cancel.Token).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        string code = LlmBridge.MapHttpError(response.StatusCode);
                        LlmSession.PushTerminal(session, code, "LLM returned HTTP "
                            + ((int)response.StatusCode).ToString() + ".", "");
                        return;
                    }
                    using (Stream stream = await response.Content
                        .ReadAsStreamAsync(session.Cancel.Token).ConfigureAwait(false))
                    using (StreamReader reader = new StreamReader(stream, Encoding.UTF8,
                        true, 4096, false))
                    {
                        await LlmSession.ReadStreamEventsAsync(reader, session)
                            .ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                LlmSession.PushTerminal(session, "LLM_TIMEOUT",
                    "LLM request timed out or cancelled.", "");
            }
            catch (HttpRequestException)
            {
                LlmSession.PushTerminal(session, "LLM_NETWORK_ERROR",
                    "LLM network request failed.", "");
            }
            catch (JsonException)
            {
                LlmSession.PushTerminal(session, "LLM_RESPONSE_INVALID",
                    "LLM returned invalid JSON.", "");
            }
            catch (Exception exception)
            {
                LlmSession.PushTerminal(session, "LLM_PROVIDER_ERROR",
                    exception.GetType().Name + " occurred in LLM provider.", "");
            }
        }

        /// <summary>
        /// 构建流式 HTTP 请求
        /// </summary>
        /// <param name="model">模型名</param>
        /// <param name="systemPrompt">System Prompt</param>
        /// <param name="userMessage">User 消息</param>
        /// <param name="toolsJson">工具定义 JSON</param>
        /// <param name="apiKey">API Key</param>
        /// <returns>HTTP 请求</returns>
        private static HttpRequestMessage BuildStreamRequest(string model,
            string systemPrompt, string userMessage, string toolsJson,
            string apiKey)
        {
            HttpRequestMessage message = new HttpRequestMessage(HttpMethod.Post,
                LlmBridge.Endpoint);
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer",
                apiKey);
            message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(
                "text/event-stream"));
            message.Content = new StringContent(BuildStreamBody(model,
                systemPrompt, userMessage, toolsJson), Encoding.UTF8,
                "application/json");
            return message;
        }

        /// <summary>
        /// 构建流式请求 JSON 正文
        /// </summary>
        /// <param name="model">模型名</param>
        /// <param name="systemPrompt">System Prompt</param>
        /// <param name="userMessage">User 消息</param>
        /// <param name="toolsJson">工具定义 JSON 数组</param>
        /// <returns>JSON 正文</returns>
        private static string BuildStreamBody(string model, string systemPrompt,
            string userMessage, string toolsJson)
        {
            using MemoryStream stream = new MemoryStream();
            using (Utf8JsonWriter writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                writer.WriteString("model", model);
                writer.WriteBoolean("stream", true);
                writer.WriteStartArray("messages");
                if (!string.IsNullOrEmpty(systemPrompt))
                {
                    writer.WriteStartObject();
                    writer.WriteString("role", "system");
                    writer.WriteString("content", LlmBridge.SafeText(systemPrompt));
                    writer.WriteEndObject();
                }
                writer.WriteStartObject();
                writer.WriteString("role", "user");
                writer.WriteString("content", LlmBridge.SafeText(userMessage));
                writer.WriteEndObject();
                writer.WriteEndArray();
                if (!string.IsNullOrWhiteSpace(toolsJson))
                {
                    writer.WritePropertyName("tools");
                    using (JsonDocument tools = JsonDocument.Parse(toolsJson))
                    {
                        tools.RootElement.WriteTo(writer);
                    }
                }
                writer.WriteEndObject();
            }
            return Encoding.UTF8.GetString(stream.ToArray());
        }
    }
}
// #MAU_CHECKSUM:SHA256:2A8136D8F544784DB22A9011963660C8F55623A5518E12A35D5424BCDBB32B7B
// #MAU_CHECKSUM:SHA256:483EAC1DFBE861185ED13C76C10BF3DF7FAF588263A4B5360EBD38D895ABB520
