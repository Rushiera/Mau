// ═══════════════════════════════════════════════
// 测试: Mau.Bricks.LLM——LlmBrick 流式积木族（llm.stream/read_chunk/finish）
// ID:   BRIK-LLM-002 配套
// 引用: Mau.Bricks.Tests → Mau.Bricks.LLM + Mau.Contracts
// 原理: 本地 TcpListener 模拟 SSE 服务器——分片流确定性验证
// 常用: llm.stream 真流式回归——CH4 TalkCat 流式对话前置
// ═══════════════════════════════════════════════
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Mau.Bricks;
using Xunit;

namespace Mau.Bricks.Tests
{
    /// <summary>
    /// LLM 流式积木测试——SSE 分片消费 / 工具调用聚合 / 错误码 / 会话清理
    /// </summary>
    public sealed class LlmStreamTests
    {
        /// <summary>
        /// 模拟 SSE 服务器——接受一个连接，按行输出 SSE 事件后关闭
        /// </summary>
        private sealed class SseServer
        {
            /// <summary>
            /// 监听器
            /// </summary>
            private readonly TcpListener _listener;

            /// <summary>
            /// SSE 事件行
            /// </summary>
            private readonly string[] _lines;

            /// <summary>
            /// 服务器线程
            /// </summary>
            private readonly Thread _thread;

            /// <summary>
            /// 捕获的请求体——供请求结构断言（Content-Length 非空时）
            /// </summary>
            internal string CapturedBody = "";

            /// <summary>
            /// 创建模拟服务器并开始监听
            /// </summary>
            /// <param name="lines">SSE 事件行</param>
            internal SseServer(string[] lines)
            {
                _listener = new TcpListener(IPAddress.Loopback, 0);
                _listener.Start();
                _lines = lines;
                _thread = new Thread(ServeLoop);
                _thread.IsBackground = true;
                _thread.Start();
            }

            /// <summary>
            /// 测试端点——指向本服务器
            /// </summary>
            internal string Endpoint
            {
                get
                {
                    IPEndPoint ep = (IPEndPoint)_listener.LocalEndpoint;
                    return "http://127.0.0.1:" + ep.Port.ToString() + "/v1/chat/completions";
                }
            }

            /// <summary>
            /// 服务循环——读请求头，写 SSE 响应
            /// </summary>
            private void ServeLoop()
            {
                try
                {
                    using (TcpClient client = _listener.AcceptTcpClient())
                    using (NetworkStream stream = client.GetStream())
                    using (StreamReader reader = new StreamReader(stream,
                        Encoding.UTF8, false, 4096, true))
                    {
                        // 读请求头——记录 Content-Length
                        string line;
                        long contentLength = 0;
                        do
                        {
                            line = reader.ReadLine() ?? "";
                            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                            {
                                long.TryParse(line.Substring(15).Trim(), out contentLength);
                            }
                        } while (line.Length > 0);
                        // 读请求体——供请求结构断言
                        if (contentLength > 0)
                        {
                            char[] body = new char[contentLength];
                            int read = 0;
                            while (read < contentLength)
                            {
                                int n = reader.Read(body, read, (int)(contentLength - read));
                                if (n <= 0)
                                {
                                    break;
                                }
                                read = read + n;
                            }
                            CapturedBody = new string(body, 0, read);
                        }
                        byte[] header = Encoding.UTF8.GetBytes(
                            "HTTP/1.1 200 OK\r\nContent-Type: text/event-stream\r\n\r\n");
                        stream.Write(header, 0, header.Length);
                        for (int i = 0; i < _lines.Length; i = i + 1)
                        {
                            byte[] data = Encoding.UTF8.GetBytes(_lines[i] + "\n\n");
                            stream.Write(data, 0, data.Length);
                            stream.Flush();
                        }
                    }
                }
                catch
                {
                    // 服务器线程异常不影响测试主体——测试超时由守卫循环兜底
                }
                finally
                {
                    _listener.Stop();
                }
            }
        }

        /// <summary>
        /// 轮询消费分片直到终态或超时
        /// </summary>
        /// <param name="requestId">会话 ID</param>
        /// <param name="content">聚合正文</param>
        /// <param name="toolCallsJson">工具调用 JSON</param>
        /// <param name="errorCode">错误码</param>
        /// <returns>是否在超时前到达终态</returns>
        private static bool DrainToFinish(string requestId, out string content,
            out string toolCallsJson, out string errorCode)
        {
            StringBuilder builder = new StringBuilder();
            toolCallsJson = "";
            errorCode = "";
            int guard = 0;
            while (guard < 200)
            {
                guard = guard + 1;
                string delta;
                string reasoning;
                string toolCalls;
                bool finished;
                string chunkError;
                if (LlmBrick.ReadChunk(requestId, out delta, out reasoning,
                    out toolCalls, out finished, out chunkError))
                {
                    builder.Append(delta);
                    if (toolCalls.Length > 0)
                    {
                        toolCallsJson = toolCalls;
                    }
                    if (chunkError.Length > 0)
                    {
                        errorCode = chunkError;
                    }
                    if (finished)
                    {
                        content = builder.ToString();
                        return true;
                    }
                }
                else
                {
                    Thread.Sleep(10);
                }
            }
            content = builder.ToString();
            return false;
        }

        /// <summary>
        /// 内容分片流——聚合为完整回复
        /// </summary>
        [Fact]
        public void LlmStream_ContentChunks_AggregateToFullReply()
        {
            SseServer server = new SseServer(new string[]
            {
                "data: {\"choices\":[{\"delta\":{\"content\":\"你好\"},\"finish_reason\":null}]}",
                "data: {\"choices\":[{\"delta\":{\"content\":\"，世界\"},\"finish_reason\":null}]}",
                "data: {\"choices\":[{\"delta\":{},\"finish_reason\":\"stop\"}]}",
                "data: [DONE]"
            });
            LlmBrick.ConfigureApiKey("test-key");
            LlmBrick.ConfigureEndpoint(server.Endpoint, 10);
            try
            {
                string requestId;
                Assert.True(LlmBrick.Stream("test-model", "sys", "你好", "", out requestId));
                string content;
                string toolCalls;
                string errorCode;
                Assert.True(DrainToFinish(requestId, out content, out toolCalls,
                    out errorCode));
                LlmBrick.Finish(requestId);
                Assert.Equal("", errorCode);
                Assert.Equal("你好，世界", content);
                Assert.Equal("", toolCalls);
            }
            finally
            {
                LlmBrick.ConfigureApiKey("");
            }
        }

        /// <summary>
        /// 工具调用分片——终态携带聚合 JSON
        /// </summary>
        [Fact]
        public void LlmStream_ToolCalls_AggregateToJson()
        {
            SseServer server = new SseServer(new string[]
            {
                "data: {\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"id\":\"call_1\",\"type\":\"function\",\"function\":{\"name\":\"file.read\",\"arguments\":\"\"}}]},\"finish_reason\":null}]}",
                "data: {\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"function\":{\"arguments\":\"{\\\"path\\\":\\\"a.txt\\\"}\"}}]},\"finish_reason\":null}]}",
                "data: {\"choices\":[{\"delta\":{},\"finish_reason\":\"tool_calls\"}]}",
                "data: [DONE]"
            });
            LlmBrick.ConfigureApiKey("test-key");
            LlmBrick.ConfigureEndpoint(server.Endpoint, 10);
            try
            {
                string requestId;
                string toolsJson = "[{\"type\":\"function\",\"function\":{\"name\":\"file.read\",\"description\":\"read\",\"parameters\":{\"type\":\"object\",\"properties\":{}}}}]";
                Assert.True(LlmBrick.Stream("test-model", "sys", "读文件", toolsJson,
                    out requestId));
                string content;
                string toolCalls;
                string errorCode;
                Assert.True(DrainToFinish(requestId, out content, out toolCalls,
                    out errorCode));
                LlmBrick.Finish(requestId);
                Assert.Equal("", errorCode);
                Assert.Equal("", content);
                Assert.Contains("file.read", toolCalls);
                Assert.Contains("a.txt", toolCalls);
            }
            finally
            {
                LlmBrick.ConfigureApiKey("");
            }
        }

        /// <summary>
        /// 未配置 Key——终态报 LLM_CREDENTIAL_MISSING
        /// </summary>
        [Fact]
        public void LlmStream_NoKey_TerminalWithCredentialMissing()
        {
            LlmBrick.ConfigureApiKey("");
            string requestId;
            Assert.True(LlmBrick.Stream("test-model", "sys", "你好", "", out requestId));
            string content;
            string toolCalls;
            string errorCode;
            Assert.True(DrainToFinish(requestId, out content, out toolCalls,
                out errorCode));
            LlmBrick.Finish(requestId);
            Assert.Equal("LLM_CREDENTIAL_MISSING", errorCode);
        }

        /// <summary>
        /// Finish 清理会话——二次 Finish 返回 false
        /// </summary>
        [Fact]
        public void LlmStream_Finish_RemovesSession()
        {
            LlmBrick.ConfigureApiKey("");
            string requestId;
            Assert.True(LlmBrick.Stream("test-model", "sys", "你好", "", out requestId));
            Assert.True(LlmBrick.Finish(requestId));
            Assert.False(LlmBrick.Finish(requestId));
        }

        /// <summary>
        /// 分片分类——内容分片 is_end=false；终态分片 is_end=true
        /// </summary>
        [Fact]
        public void LlmStream_IsEnd_ClassifiesChunks()
        {
            SseServer server = new SseServer(new string[]
            {
                "data: {\"choices\":[{\"delta\":{\"content\":\"你好\"},\"finish_reason\":null}]}",
                "data: {\"choices\":[{\"delta\":{},\"finish_reason\":\"stop\"}]}",
                "data: [DONE]"
            });
            LlmBrick.ConfigureApiKey("test-key");
            LlmBrick.ConfigureEndpoint(server.Endpoint, 10);
            try
            {
                string requestId;
                Assert.True(LlmBrick.Stream("test-model", "sys", "你好", "", out requestId));
                // 消费内容分片——is_end 返回 false（非终态）
                string delta;
                string reasoning;
                string toolCalls;
                bool finished;
                string chunkError;
                Assert.True(DrainChunk(requestId, out delta, out reasoning,
                    out toolCalls, out finished, out chunkError));
                bool ended;
                string err;
                Assert.False(LlmBrick.IsEnd(requestId, out ended, out err));
                // 消费终态分片——is_end 返回 true
                Assert.True(DrainChunk(requestId, out delta, out reasoning,
                    out toolCalls, out finished, out chunkError));
                Assert.True(LlmBrick.IsEnd(requestId, out ended, out err));
                Assert.True(ended);
                LlmBrick.Finish(requestId);
            }
            finally
            {
                LlmBrick.ConfigureApiKey("");
            }
        }

        /// <summary>
        /// 工具调用分类——is_tool 在终态分片后为 true 且输出聚合 JSON
        /// </summary>
        [Fact]
        public void LlmStream_IsTool_DetectsToolCalls()
        {
            SseServer server = new SseServer(new string[]
            {
                "data: {\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"id\":\"call_1\",\"type\":\"function\",\"function\":{\"name\":\"file.read\",\"arguments\":\"\"}}]},\"finish_reason\":null}]}",
                "data: {\"choices\":[{\"delta\":{},\"finish_reason\":\"tool_calls\"}]}",
                "data: [DONE]"
            });
            LlmBrick.ConfigureApiKey("test-key");
            LlmBrick.ConfigureEndpoint(server.Endpoint, 10);
            try
            {
                string requestId;
                Assert.True(LlmBrick.Stream("test-model", "sys", "读文件", "", out requestId));
                string delta;
                string reasoning;
                string toolCalls;
                bool finished;
                string chunkError;
                // 工具调用 delta 不产生内容分片——第一个可消费分片即终态（携带聚合 toolCallsJson）
                Assert.True(DrainChunk(requestId, out delta, out reasoning,
                    out toolCalls, out finished, out chunkError));
                Assert.True(finished);
                bool isTool;
                string toolCallsJson;
                Assert.True(LlmBrick.IsTool(requestId, out isTool, out toolCallsJson));
                Assert.True(isTool);
                Assert.Contains("file.read", toolCallsJson);
                LlmBrick.Finish(requestId);
            }
            finally
            {
                LlmBrick.ConfigureApiKey("");
            }
        }

        /// <summary>
        /// 消费单个分片（循环等待）
        /// </summary>
        /// <param name="requestId">会话 ID</param>
        /// <param name="delta">正文增量</param>
        /// <param name="reasoning">推理增量</param>
        /// <param name="toolCalls">工具调用 JSON</param>
        /// <param name="finished">终态标志</param>
        /// <param name="errorCode">错误码</param>
        /// <returns>是否在超时前取到分片</returns>
        private static bool DrainChunk(string requestId, out string delta,
            out string reasoning, out string toolCalls, out bool finished,
            out string errorCode)
        {
            int guard = 0;
            while (guard < 200)
            {
                guard = guard + 1;
                if (LlmBrick.ReadChunk(requestId, out delta, out reasoning,
                    out toolCalls, out finished, out errorCode))
                {
                    return true;
                }
                Thread.Sleep(10);
            }
            delta = "";
            reasoning = "";
            toolCalls = "";
            finished = false;
            errorCode = "";
            return false;
        }

        /// <summary>
        /// 结构化流式（llm.completions）——messagesJson 数组透传 + 分片闭环
        /// </summary>
        [Fact]
        public void LlmCompletions_MessagesJson_RoundTrip()
        {
            SseServer server = new SseServer(new string[]
            {
                "data: {\"choices\":[{\"delta\":{\"content\":\"结构化回复\"},\"finish_reason\":null}]}",
                "data: {\"choices\":[{\"delta\":{},\"finish_reason\":\"stop\"}]}",
                "data: [DONE]"
            });
            LlmBrick.ConfigureApiKey("test-key");
            LlmBrick.ConfigureEndpoint(server.Endpoint, 10);
            try
            {
                string messages = "[{\"role\":\"system\",\"content\":\"sys\"},{\"role\":\"user\",\"content\":\"你好\"},{\"role\":\"assistant\",\"content\":\"\",\"tool_calls\":[{\"id\":\"call_1\",\"type\":\"function\",\"function\":{\"name\":\"file.read\",\"arguments\":\"{}\"}}]},{\"role\":\"tool\",\"tool_call_id\":\"call_1\",\"content\":\"结果\"}]";
                string requestId;
                Assert.True(LlmBrick.Completions("test-model", messages, "", out requestId));
                string content;
                string toolCalls;
                string errorCode;
                Assert.True(DrainToFinish(requestId, out content, out toolCalls,
                    out errorCode));
                LlmBrick.Finish(requestId);
                Assert.Equal("", errorCode);
                Assert.Equal("结构化回复", content);

                // 请求体必须透传 messages 数组（全角色结构保留）
                Assert.Contains("\"messages\"", server.CapturedBody);
                Assert.Contains("\"role\":\"tool\"", server.CapturedBody);
                Assert.Contains("\"tool_call_id\":\"call_1\"", server.CapturedBody);
            }
            finally
            {
                LlmBrick.ConfigureApiKey("");
            }
        }
    }
}
