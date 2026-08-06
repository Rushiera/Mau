# LLM 类积木 — LLM

> 类别码：LLM | ID 段：BRIK-LLM-###

| ID | 名字 | 工程路径 | 状态 | 说明 |
|:--|:--|:--|:--|:--|
| BRIK-LLM-001 | llm.chat | Mau.Bricks.LLM/LlmBrick.cs | ✅ | 非流式 LLM 调用（OpenAI 兼容） |
| BRIK-LLM-002 | llm.stream | Mau.Bricks.LLM/LlmBrick.cs | ✅ | 真流式 SSE 调用——启动后台请求，分片经 read_chunk 消费（CH3 CH_DeepSeekProvider 稳定代码块移植） |
| BRIK-LLM-002b | llm.read_chunk | Mau.Bricks.LLM/LlmBrick.cs | ✅ | 消费流式分片——contentDelta/reasoningDelta/toolCallsJson/finished/errorCode |
| BRIK-LLM-002c | llm.finish | Mau.Bricks.LLM/LlmBrick.cs | ✅ | 终止流式会话——取消请求并清理 |
| BRIK-LLM-003 | llm.ctx_set_system | Mau.Bricks.LLM/ContextBrick.cs | ✅ | 设置唯一 System Prompt |
| BRIK-LLM-004 | llm.ctx_push_user | Mau.Bricks.LLM/ContextBrick.cs | ✅ | 追加 User 消息 |
| BRIK-LLM-005 | llm.ctx_push_assistant | Mau.Bricks.LLM/ContextBrick.cs | ✅ | 追加 Assistant 消息 |
| BRIK-LLM-006 | llm.ctx_trim | Mau.Bricks.LLM/ContextBrick.cs | ✅ | 按字符预算截断（System 保留） |
| BRIK-LLM-007 | llm.ctx_count | Mau.Bricks.LLM/ContextBrick.cs | ✅ | 消息数量 |
| BRIK-LLM-008 | llm.ctx_build_prompt | Mau.Bricks.LLM/ContextBrick.cs | ✅ | 拼接纯文本 Prompt |
| BRIK-LLM-009 | llm.ctx_clear | Mau.Bricks.LLM/ContextBrick.cs | ✅ | 清空业务历史保留 System |

**llm.stream 用法：** `Stream(model, systemPrompt, userMessage, toolsJson, out requestId)` 启动 → 轮询 `ReadChunk(requestId, out contentDelta, out reasoningDelta, out toolCallsJson, out finished, out errorCode)` 消费分片 → `finished=true` 终态（toolCallsJson 携带聚合工具调用）→ `Finish(requestId)` 清理。toolsJson 为空=无工具调用。错误码：LLM_CREDENTIAL_MISSING / LLM_AUTH_FAILED / LLM_RATE_LIMITED / LLM_REMOTE_UNAVAILABLE / LLM_NETWORK_ERROR / LLM_TIMEOUT / LLM_RESPONSE_INVALID / LLM_STREAM_INCOMPLETE / LLM_PROVIDER_ERROR。
