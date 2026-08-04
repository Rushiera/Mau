# LLM 类积木 — LLM

> 类别码：LLM | ID 段：BRIK-LLM-###

| ID | 名字 | 工程路径 | 状态 | 说明 |
|:--|:--|:--|:--|:--|
| BRIK-LLM-001 | llm.chat | Mau.Bricks.LLM/LlmBrick.cs | ✅ | 非流式 LLM 调用（OpenAI 兼容） |
| BRIK-LLM-002 | llm.stream | Mau.Bricks.LLM/LlmBrick.cs | ✅ | 流式 LLM 调用（当前非流式实现，SSE 接入 CH4 时代） |
| BRIK-LLM-003 | llm.ctx_set_system | Mau.Bricks.LLM/ContextBrick.cs | ✅ | 设置唯一 System Prompt |
| BRIK-LLM-004 | llm.ctx_push_user | Mau.Bricks.LLM/ContextBrick.cs | ✅ | 追加 User 消息 |
| BRIK-LLM-005 | llm.ctx_push_assistant | Mau.Bricks.LLM/ContextBrick.cs | ✅ | 追加 Assistant 消息 |
| BRIK-LLM-006 | llm.ctx_trim | Mau.Bricks.LLM/ContextBrick.cs | ✅ | 按字符预算截断（System 保留） |
| BRIK-LLM-007 | llm.ctx_count | Mau.Bricks.LLM/ContextBrick.cs | ✅ | 消息数量 |
| BRIK-LLM-008 | llm.ctx_build_prompt | Mau.Bricks.LLM/ContextBrick.cs | ✅ | 拼接纯文本 Prompt |
| BRIK-LLM-009 | llm.ctx_clear | Mau.Bricks.LLM/ContextBrick.cs | ✅ | 清空业务历史保留 System |
