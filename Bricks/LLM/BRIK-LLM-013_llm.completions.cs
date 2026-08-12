// ═══════════════════════════════════════════════════
// 积木: llm.completions
// ID:   BRIK-LLM-013
// 类别: LLM
// 作用: 结构化流式 LLM 调用——messagesJson 消息数组（OpenAI 协议全角色）
// 依赖: 无
// 引用: System · System.IO · System.Net.Http · System.Net.Http.Headers · System.Text · System.Text.Json
// 原理: messagesJson 透传（tool 角色消息结构化回填的请求端）→ 后台 SSE → 分片入队
// 常用: TalkCat 结构化上下文请求（ctx_build_messages_json → completions）
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
    /// LLM 积木——llm.completions 结构化流式调用（依赖 LlmBridge/LlmSession）
    /// </summary>
    public static class LlmCompletionsBrick
    {
        /// <summary>
        /// 结构化流式 LLM 调用——messagesJson 消息数组（OpenAI 协议全角色：system/user/assistant/tool）
        /// </summary>
        /// <param name="model">模型名</param>
        /// <param name="messagesJson">messages 数组 JSON</param>
        /// <param name="toolsJson">工具定义 JSON 数组（空=无工具）</param>
        /// <param name="requestId">流式会话 ID——ReadChunk/Finish 用</param>
        /// <returns>true=启动成功</returns>
        public static bool Completions(string model, string messagesJson,
            string toolsJson, out string requestId)
        {
            requestId = "";
            if (string.IsNullOrWhiteSpace(model))
            {
                model = "deepseek-chat";
            }
            if (string.IsNullOrWhiteSpace(messagesJson))
            {
                return false;
            }
            LlmStreamSession? session = LlmSession.CreateSession(out requestId);
            if (session == null)
            {
                return false;
            }
            Task worker = RunCompletionsAsync(requestId, model, messagesJson,
                toolsJson, session);
            session.Worker = worker;
            return true;
        }

        /// <summary>
        /// 执行结构化流式 HTTP 请求并解析 SSE——分片写入会话队列
        /// </summary>
        /// <param name="requestId">会话 ID</param>
        /// <param name="model">模型名</param>
        /// <param name="messagesJson">messages 数组 JSON</param>
        /// <param name="toolsJson">工具定义 JSON</param>
        /// <param name="session">会话</param>
        /// <returns>异步工作</returns>
        private static async Task RunCompletionsAsync(string requestId,
            string model, string messagesJson, string toolsJson,
            LlmStreamSession session)
        {
            try
            {
                // 🔴 LLM 请求观测（2026-08-12 诊断补透明性）：toolsJson 是否传给了 LLM——
                //   定位"请求无工具声明"（T_BuildTools/cat.tools_json 问题）vs "LLM 不调用"（声明质量/行为）
                //   🔴 观测零副作用——try-catch 保护：埋点异常绝不导致请求误判失败（2026-08-12 测试卡死教训）
                try
                {
                    AuditStore.Default?.Record("LlmSession", "llm.request", -1, new AuditProp[] {
                        new AuditProp("model", model),
                        new AuditProp("toolsLen", toolsJson.Length.ToString()),
                        new AuditProp("tools", toolsJson.Length > 200
                            ? toolsJson.Substring(0, 200) : toolsJson),
                        new AuditProp("messagesLen", messagesJson.Length.ToString())
                    });
                }
                catch (Exception)
                {
                    // 观测失败不影响主链路
                }
                string apiKey = LlmBridge.ApiKey;
                if (apiKey.Length == 0)
                {
                    LlmSession.PushTerminal(session, "LLM_CREDENTIAL_MISSING",
                        "LLM credential is not configured.", "");
                    return;
                }
                using (HttpRequestMessage message = BuildCompletionsRequest(model,
                    messagesJson, toolsJson, apiKey))
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
        /// 构建结构化流式 HTTP 请求
        /// </summary>
        /// <param name="model">模型名</param>
        /// <param name="messagesJson">messages 数组 JSON</param>
        /// <param name="toolsJson">工具定义 JSON</param>
        /// <param name="apiKey">API Key</param>
        /// <returns>HTTP 请求</returns>
        private static HttpRequestMessage BuildCompletionsRequest(string model,
            string messagesJson, string toolsJson, string apiKey)
        {
            HttpRequestMessage message = new HttpRequestMessage(HttpMethod.Post,
                LlmBridge.Endpoint);
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer",
                apiKey);
            message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(
                "text/event-stream"));
            message.Content = new StringContent(BuildCompletionsBody(model,
                messagesJson, toolsJson), Encoding.UTF8, "application/json");
            return message;
        }

        /// <summary>
        /// 构建结构化流式请求 JSON 正文——messages 数组直接透传（OpenAI 协议）
        /// </summary>
        /// <param name="model">模型名</param>
        /// <param name="messagesJson">messages 数组 JSON</param>
        /// <param name="toolsJson">工具定义 JSON 数组</param>
        /// <returns>JSON 正文</returns>
        private static string BuildCompletionsBody(string model,
            string messagesJson, string toolsJson)
        {
            using MemoryStream stream = new MemoryStream();
            using (Utf8JsonWriter writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                writer.WriteString("model", model);
                writer.WriteBoolean("stream", true);
                writer.WritePropertyName("messages");
                using (JsonDocument messages = JsonDocument.Parse(messagesJson))
                {
                    messages.RootElement.WriteTo(writer);
                }
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
// #MAU_CHECKSUM:SHA256:D4D506D314FEC149BCADD9A7F137CAA8D006396743CCCFB4FC0207F615D68F32
