# LLM 类积木 — LLM

> 类别码：LLM | ID 段：BRIK-LLM-###

| ID | 名字 | 工程路径 | 状态 | 说明 |
|:--|:--|:--|:--|:--|
| BRIK-LLM-001 | llm.chat | Bricks/LLM/BRIK-LLM-001_llm.chat.cs | ✅ | 非流式 LLM 调用（OpenAI 兼容） |
| BRIK-LLM-002 | llm.stream | Bricks/LLM/BRIK-LLM-002_llm.stream.cs | ✅ | 真流式 SSE 调用——启动后台请求，分片经 read_chunk 消费（CH3 CH_DeepSeekProvider 稳定代码块移植） |
| BRIK-LLM-020 | llm.read_chunk | Bricks/LLM/BRIK-LLM-020_llm.read_chunk.cs | ✅ | 消费流式分片——contentDelta/reasoningDelta/toolCallsJson/finished/errorCode（原 002b 迁移） |
| BRIK-LLM-021 | llm.finish | Bricks/LLM/BRIK-LLM-021_llm.finish.cs | ✅ | 终止流式会话——取消请求并清理（原 002c 迁移） |
| BRIK-LLM-003 | llm.ctx_set_system | Bricks/LLM/BRIK-LLM-003_llm.ctx_set_system.cs | ✅ | 设置唯一 System Prompt |
| BRIK-LLM-004 | llm.ctx_push_user | Bricks/LLM/BRIK-LLM-004_llm.ctx_push_user.cs | ✅ | 追加 User 消息 |
| BRIK-LLM-005 | llm.ctx_push_assistant | Bricks/LLM/BRIK-LLM-005_llm.ctx_push_assistant.cs | ✅ | 追加 Assistant 消息 |
| BRIK-LLM-006 | llm.ctx_trim | Bricks/LLM/BRIK-LLM-006_llm.ctx_trim.cs | ✅ | 按字符预算截断（System 保留） |
| BRIK-LLM-007 | llm.ctx_count | Bricks/LLM/BRIK-LLM-007_llm.ctx_count.cs | ✅ | 消息数量 |
| BRIK-LLM-008 | llm.ctx_build_prompt | Bricks/LLM/BRIK-LLM-008_llm.ctx_build_prompt.cs | ✅ | 拼接纯文本 Prompt（跳过 Tool/Error） |
| BRIK-LLM-009 | llm.ctx_clear | Bricks/LLM/BRIK-LLM-009_llm.ctx_clear.cs | ✅ | 清空业务历史保留 System |
| BRIK-LLM-010 | llm.ctx_push_tool | Bricks/LLM/BRIK-LLM-010_llm.ctx_push_tool.cs | ✅ | 追加 Tool 结果消息（tool_call_id 回执） |
| BRIK-LLM-011 | llm.ctx_push_assistant_tool_calls | Bricks/LLM/BRIK-LLM-011_llm.ctx_push_assistant_tool_calls.cs | ✅ | 追加 Assistant 工具声明（tool_calls JSON） |
| BRIK-LLM-012 | llm.ctx_build_messages_json | Bricks/LLM/BRIK-LLM-012_llm.ctx_build_messages_json.cs | ✅ | 导出 OpenAI 兼容 messages 数组（跳过 Error） |
| BRIK-LLM-013 | llm.completions | Bricks/LLM/BRIK-LLM-013_llm.completions.cs | ✅ | 结构化流式调用——messagesJson 数组透传 |
| BRIK-LLM-014 | llm.is_end | Bricks/LLM/BRIK-LLM-014_llm.is_end.cs | ✅ | 判断最后分片是否终态（返回=判断结果） |
| BRIK-LLM-015 | llm.is_tool | Bricks/LLM/BRIK-LLM-015_llm.is_tool.cs | ✅ | 判断最后分片是否含工具（返回=判断结果） |
| BRIK-LLM-016 | llm.has_error | Bricks/LLM/BRIK-LLM-016_llm.has_error.cs | ✅ | 判断最后分片是否带错误码（返回=判断结果；错误码=LLM 正常返回值） |
| BRIK-LLM-017 | llm.ctx_checkpoint | Bricks/LLM/BRIK-LLM-017_llm.ctx_checkpoint.cs | ✅ | 记录回滚断点（对话开始前调用） |
| BRIK-LLM-018 | llm.ctx_rollback | Bricks/LLM/BRIK-LLM-018_llm.ctx_rollback.cs | ✅ | 回滚到断点——移除断点后消息（错误自救） |
| BRIK-LLM-019 | llm.ctx_push_error | Bricks/LLM/BRIK-LLM-019_llm.ctx_push_error.cs | ✅ | 错误码入持久流（Error 角色消息——不参与 LLM 请求） |

**llm.stream 用法：** `Stream(model, systemPrompt, userMessage, toolsJson, out requestId)` 启动 → 轮询 `ReadChunk(requestId, out contentDelta, out reasoningDelta, out toolCallsJson, out finished, out errorCode)` 消费分片 → `finished=true` 终态（toolCallsJson 携带聚合工具调用）→ `Finish(requestId)` 清理。toolsJson 为空=无工具调用。错误码：LLM_CREDENTIAL_MISSING / LLM_AUTH_FAILED / LLM_RATE_LIMITED / LLM_REMOTE_UNAVAILABLE / LLM_NETWORK_ERROR / LLM_TIMEOUT / LLM_RESPONSE_INVALID / LLM_STREAM_INCOMPLETE / LLM_PROVIDER_ERROR。

> **迁移说明（2026-08-06）：** BRIK-LLM-002b/002c 已迁移为独立三位序号 BRIK-LLM-020/021（编号规范 v2——纯三位序号，唯一顺序延伸）。旧编号不重用。
