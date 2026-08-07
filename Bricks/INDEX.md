# Mau 积木索引 — INDEX

> 版本：v3.0 | 创建：2026-08-04 | 更新：2026-08-06（v3.0：`mau bricks index --update` 自动生成——注册表唯一真相源）
> 全量积木登记——一行一条。ID 永不重用。

## 全部积木

| ID | 名字 | 类别 | 工程路径 | 依赖 | 状态 | 来源 |
|:--|:--|:--|:--|:--|:--|:--|
| BRIK-APPROVAL-001 | approval.request | APPROVAL | Mau.Bricks.Approval/ApprovalBrick.cs |  |  | ✅ |
| BRIK-APPROVAL-002 | approval.resolve | APPROVAL | Mau.Bricks.Approval/ApprovalBrick.cs |  |  | ✅ |
| BRIK-APPROVAL-003 | approval.reject | APPROVAL | Mau.Bricks.Approval/ApprovalBrick.cs |  |  | ✅ |
| BRIK-APPROVAL-004 | approval.pending | APPROVAL | Mau.Bricks.Approval/ApprovalBrick.cs |  |  | ✅ |
| BRIK-CMD-001 | cmd.register | CMD | Mau.Bricks.Standard/CmdBrick.cs |  |  | ✅ |
| BRIK-CMD-002 | cmd.unregister | CMD | Mau.Bricks.Standard/CmdBrick.cs |  |  | ✅ |
| BRIK-CMD-003 | cmd.consume | CMD | Mau.Bricks.Standard/CmdBrick.cs |  |  | ✅ |
| BRIK-CMD-004 | cmd.set | CMD | Mau.Bricks.Standard/CmdBrick.cs |  |  | ✅ |
| BRIK-CMD-005 | cmd.clean | CMD | Mau.Bricks.Standard/CmdBrick.cs |  |  | ✅ |
| BRIK-DATA-001 | data.snapshot_encode | DATA | Mau.Bricks.Data/DataBrick.cs |  |  | ✅ |
| BRIK-DATA-002 | data.snapshot_decode | DATA | Mau.Bricks.Data/DataBrick.cs |  |  | ✅ |
| BRIK-DATA-003 | data.box_set | DATA | Mau.Bricks.Data/ValueBox.cs |  |  | ✅ |
| BRIK-DATA-004 | data.box_get | DATA | Mau.Bricks.Data/ValueBox.cs |  |  | ✅ |
| BRIK-DATA-005 | data.box_set_dic | DATA | Mau.Bricks.Data/ValueBox.cs |  |  | ✅ |
| BRIK-DATA-006 | data.box_get_dic | DATA | Mau.Bricks.Data/ValueBox.cs |  |  | ✅ |
| BRIK-FILE-001 | file.convert | FILE | Mau.Bricks.Standard/FileBrick.cs |  |  | ✅ |
| BRIK-FILE-002 | file.read | FILE | Mau.Bricks.Standard/FileBrick.cs |  |  | ✅ |
| BRIK-FILE-003 | file.write | FILE | Mau.Bricks.Standard/FileBrick.cs |  |  | ✅ |
| BRIK-FILE-004 | file.append | FILE | Mau.Bricks.Standard/FileBrick.cs |  |  | ✅ |
| BRIK-FILE-005 | file.replace | FILE | Mau.Bricks.Standard/FileBrick.cs |  |  | ✅ |
| BRIK-FILE-006 | file.read_lines | FILE | Mau.Bricks.Standard/FileBrick.cs |  |  | ✅ |
| BRIK-FILE-007 | file.tree | FILE | Mau.Bricks.Standard/FileBrick.cs |  |  | ✅ |
| BRIK-FILE-008 | file.find | FILE | Mau.Bricks.Standard/FileBrick.cs |  |  | ✅ |
| BRIK-FILE-009 | file.move | FILE | Mau.Bricks.Standard/FileBrick.cs |  |  | ✅ |
| BRIK-FILE-010 | file.delete | FILE | Mau.Bricks.Standard/FileBrick.cs |  |  | ✅ |
| BRIK-FILE-011 | file.batch | FILE | Mau.Bricks.Standard/FileBrick.cs |  |  | ✅ |
| BRIK-LLM-001 | llm.chat | LLM | Mau.Bricks.LLM/LlmBrick.cs |  |  | ✅ |
| BRIK-LLM-002 | llm.stream | LLM | Mau.Bricks.LLM/LlmBrick.cs |  |  | ✅ |
| BRIK-LLM-003 | llm.ctx_set_system | LLM | Mau.Bricks.LLM/ContextBrick.cs |  |  | ✅ |
| BRIK-LLM-004 | llm.ctx_push_user | LLM | Mau.Bricks.LLM/ContextBrick.cs |  |  | ✅ |
| BRIK-LLM-005 | llm.ctx_push_assistant | LLM | Mau.Bricks.LLM/ContextBrick.cs |  |  | ✅ |
| BRIK-LLM-006 | llm.ctx_trim | LLM | Mau.Bricks.LLM/ContextBrick.cs |  |  | ✅ |
| BRIK-LLM-007 | llm.ctx_count | LLM | Mau.Bricks.LLM/ContextBrick.cs |  |  | ✅ |
| BRIK-LLM-008 | llm.ctx_build_prompt | LLM | Mau.Bricks.LLM/ContextBrick.cs |  |  | ✅ |
| BRIK-LLM-009 | llm.ctx_clear | LLM | Mau.Bricks.LLM/ContextBrick.cs |  |  | ✅ |
| BRIK-LLM-010 | llm.ctx_push_tool | LLM | Mau.Bricks.LLM/ContextBrick.cs |  |  | ✅ |
| BRIK-LLM-011 | llm.ctx_push_assistant_tool_calls | LLM | Mau.Bricks.LLM/ContextBrick.cs |  |  | ✅ |
| BRIK-LLM-012 | llm.ctx_build_messages_json | LLM | Mau.Bricks.LLM/ContextBrick.cs |  |  | ✅ |
| BRIK-LLM-013 | llm.completions | LLM | Mau.Bricks.LLM/LlmBrick.cs |  |  | ✅ |
| BRIK-LLM-014 | llm.is_end | LLM | Mau.Bricks.LLM/LlmBrick.cs |  |  | ✅ |
| BRIK-LLM-015 | llm.is_tool | LLM | Mau.Bricks.LLM/LlmBrick.cs |  |  | ✅ |
| BRIK-LLM-016 | llm.has_error | LLM | Mau.Bricks.LLM/LlmBrick.cs |  |  | ✅ |
| BRIK-LLM-017 | llm.ctx_checkpoint | LLM | Mau.Bricks.LLM/ContextBrick.cs |  |  | ✅ |
| BRIK-LLM-018 | llm.ctx_rollback | LLM | Mau.Bricks.LLM/ContextBrick.cs |  |  | ✅ |
| BRIK-LLM-019 | llm.ctx_push_error | LLM | Mau.Bricks.LLM/ContextBrick.cs |  |  | ✅ |
| BRIK-LLM-020 | llm.read_chunk | LLM | Mau.Bricks.LLM/LlmBrick.cs |  |  | ✅ |
| BRIK-LLM-021 | llm.finish | LLM | Mau.Bricks.LLM/LlmBrick.cs |  |  | ✅ |
| BRIK-LOG-001 | log.write | LOG | Mau.Bricks.Log/LogBrick.cs |  |  | ✅ |
| BRIK-LOG-002 | log.all | LOG | Mau.Bricks.Log/LogBrick.cs |  |  | ✅ |
| BRIK-LOG-003 | log.count | LOG | Mau.Bricks.Log/LogBrick.cs |  |  | ✅ |
| BRIK-LOG-004 | log.clear | LOG | Mau.Bricks.Log/LogBrick.cs |  |  | ✅ |
| BRIK-MATH-001 | math.is_all_digits | MATH | Mau.Bricks.Standard/MathBrick.cs |  |  | ✅ |
| BRIK-MATH-002 | math.format_size | MATH | Mau.Bricks.Standard/MathBrick.cs |  |  | ✅ |
| BRIK-MATH-003 | math.result_preview | MATH | Mau.Bricks.Standard/MathBrick.cs |  |  | ✅ |
| BRIK-OA-001 | oa.post | OA | Mau.Bricks.Standard/OaBrick.cs |  |  | ✅ |
| BRIK-OA-002 | oa.list | OA | Mau.Bricks.Standard/OaBrick.cs |  |  | ✅ |
| BRIK-OA-003 | oa.claim | OA | Mau.Bricks.Standard/OaBrick.cs |  |  | ✅ |
| BRIK-OA-004 | oa.complete | OA | Mau.Bricks.Standard/OaBrick.cs |  |  | ✅ |
| BRIK-OA-005 | oa.settle | OA | Mau.Bricks.Standard/OaBrick.cs |  |  | ✅ |
| BRIK-OA-006 | oa.set_int | OA | Mau.Bricks.Standard/OaBrick.cs |  |  | ✅ |
| BRIK-OA-007 | oa.set_str | OA | Mau.Bricks.Standard/OaBrick.cs |  |  | ✅ |
| BRIK-OA-008 | oa.get_int | OA | Mau.Bricks.Standard/OaBrick.cs |  |  | ✅ |
| BRIK-OA-009 | oa.get_str | OA | Mau.Bricks.Standard/OaBrick.cs |  |  | ✅ |
| BRIK-OA-010 | oa.claim_one | OA | Mau.Bricks.Standard/OaBrick.cs |  |  | ✅ |
| BRIK-OA-011 | oa.is_closed | OA | Mau.Bricks.Standard/OaBrick.cs |  |  | ✅ |
| BRIK-OFFICE-001 | excel.read | OFFICE | Mau.Bricks.Office/ExcelBrick.cs |  |  | ✅ |
| BRIK-OFFICE-002 | excel.write | OFFICE | Mau.Bricks.Office/ExcelBrick.cs |  |  | ✅ |
| BRIK-OFFICE-003 | docx.read | OFFICE | Mau.Bricks.Office/DocxBrick.cs |  |  | ✅ |
| BRIK-OFFICE-004 | docx.write | OFFICE | Mau.Bricks.Office/DocxBrick.cs |  |  | ✅ |
| BRIK-SHELL-001 | shell.exec | SHELL | Mau.Bricks.Shell/ShellBrick.cs |  |  | ✅ |
| BRIK-TEXT-001 | text.md_parse | TEXT | Mau.Bricks.Text/TextBrick.cs |  |  | ✅ |
| BRIK-TOOL-001 | tool.exec | TOOL | Mau.Bricks.Standard/ToolBrick.cs |  |  | ✅ |
| BRIK-TOOL-002 | tool.dispatch_next | TOOL | Mau.Bricks.Standard/ToolBrick.cs |  |  | ✅ |
| BRIK-TOOL-003 | tool.claim_next | TOOL | Mau.Bricks.Standard/ToolBrick.cs |  |  | ✅ |
| BRIK-TOOL-004 | tool.collect_one | TOOL | Mau.Bricks.Standard/ToolBrick.cs |  |  | ✅ |
| BRIK-TOOL-005 | tool.is_name | TOOL | Mau.Bricks.Standard/ToolBrick.cs |  |  | ✅ |
| BRIK-TOOL-006 | tool.run_file_read | TOOL | Mau.Bricks.Standard/ToolBrick.cs |  |  | ✅ |
| BRIK-TOOL-007 | tool.run_file_write | TOOL | Mau.Bricks.Standard/ToolBrick.cs |  |  | ✅ |
| BRIK-TOOL-008 | tool.run_shell_exec | TOOL | Mau.Bricks.Shell/ShellToolBrick.cs |  |  | ✅ |
| BRIK-TOOL-009 | tool.dispatch_one | TOOL | Mau.Bricks.Standard/ToolBrick.cs |  |  | ✅ |
| BRIK-TOOL-010 | tool.dispatch | TOOL | Mau.Bricks.Standard/ToolBrick.cs |  |  | ✅ |
| BRIK-TOOL-011 | tool.collect | TOOL | Mau.Bricks.Standard/ToolBrick.cs |  |  | ✅ |

---

_版本：v3.0 | 2026-08-06 | 自动生成——`mau bricks index --update`（注册表唯一真相源；来源/依赖列为人工维护区）_
