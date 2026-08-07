# Mau 积木索引 — INDEX

> 版本：v3.1 | 创建：2026-08-04 | 更新：2026-08-07（v3.1：`mau bricks index --update` 源码驱动——文件头 + 静态签名 = 契约唯一真相源）
> 全量积木登记——一行一条。ID 永不重用。

## 全部积木

| ID | 名字 | 类别 | 工程路径 | 依赖 | 状态 | 来源 |
|:--|:--|:--|:--|:--|:--|:--|
| BRIK-APPROVAL-001 | approval.request | APPROVAL | APPROVAL/BRIK-APPROVAL-001_approval.request.cs | 无 | active |  |
| BRIK-APPROVAL-002 | approval.resolve | APPROVAL | APPROVAL/BRIK-APPROVAL-002_approval.resolve.cs | 无 | active |  |
| BRIK-APPROVAL-003 | approval.reject | APPROVAL | APPROVAL/BRIK-APPROVAL-003_approval.reject.cs | 无 | active |  |
| BRIK-APPROVAL-004 | approval.pending | APPROVAL | APPROVAL/BRIK-APPROVAL-004_approval.pending.cs | 无 | active |  |
| BRIK-CMD-001 | cmd.register | CMD | CMD/BRIK-CMD-001_cmd.register.cs | 无 | active |  |
| BRIK-CMD-002 | cmd.unregister | CMD | CMD/BRIK-CMD-002_cmd.unregister.cs | 无 | active |  |
| BRIK-CMD-003 | cmd.consume | CMD | CMD/BRIK-CMD-003_cmd.consume.cs | 无 | active |  |
| BRIK-CMD-004 | cmd.set | CMD | CMD/BRIK-CMD-004_cmd.set.cs | 无 | active |  |
| BRIK-CMD-005 | cmd.clean | CMD | CMD/BRIK-CMD-005_cmd.clean.cs | 无 | active |  |
| BRIK-CMD-006 | cmd.is_key | CMD | CMD/BRIK-CMD-006_cmd.is_key.cs | 无 | active |  |
| BRIK-DATA-001 | data.snapshot_encode | DATA | DATA/BRIK-DATA-001_data.snapshot_encode.cs | 无 | active |  |
| BRIK-DATA-002 | data.snapshot_decode | DATA | DATA/BRIK-DATA-002_data.snapshot_decode.cs | 无 | active |  |
| BRIK-DATA-003 | data.box_set | DATA | DATA/BRIK-DATA-003_data.box_set.cs | 无 | active |  |
| BRIK-DATA-004 | data.box_get | DATA | DATA/BRIK-DATA-004_data.box_get.cs | 无 | active |  |
| BRIK-DATA-005 | data.box_set_dic | DATA | DATA/BRIK-DATA-005_data.box_set_dic.cs | 无 | active |  |
| BRIK-DATA-006 | data.box_get_dic | DATA | DATA/BRIK-DATA-006_data.box_get_dic.cs | 无 | active |  |
| BRIK-DOG-001 | dog.create | DOG | DOG/BRIK-DOG-001_dog.create.cs | 无 | active |  |
| BRIK-DOG-002 | dog.set_int | DOG | DOG/BRIK-DOG-002_dog.set_int.cs | 无 | active |  |
| BRIK-DOG-003 | dog.set_str | DOG | DOG/BRIK-DOG-003_dog.set_str.cs | 无 | active |  |
| BRIK-DOG-004 | dog.is_closed | DOG | DOG/BRIK-DOG-004_dog.is_closed.cs | 无 | active |  |
| BRIK-DOG-005 | dog.is_timeout | DOG | DOG/BRIK-DOG-005_dog.is_timeout.cs | 无 | active |  |
| BRIK-DOG-006 | dog.get_int | DOG | DOG/BRIK-DOG-006_dog.get_int.cs | 无 | active |  |
| BRIK-DOG-007 | dog.get_str | DOG | DOG/BRIK-DOG-007_dog.get_str.cs | 无 | active |  |
| BRIK-DOG-008 | dog.finish | DOG | DOG/BRIK-DOG-008_dog.finish.cs | 无 | active |  |
| BRIK-FILE-001 | file.convert | FILE | FILE/BRIK-FILE-001_file.convert.cs | 无 | active |  |
| BRIK-FILE-002 | file.read | FILE | FILE/BRIK-FILE-002_file.read.cs | 无 | active |  |
| BRIK-FILE-003 | file.write | FILE | FILE/BRIK-FILE-003_file.write.cs | 无 | active |  |
| BRIK-FILE-004 | file.append | FILE | FILE/BRIK-FILE-004_file.append.cs | 无 | active |  |
| BRIK-FILE-005 | file.replace | FILE | FILE/BRIK-FILE-005_file.replace.cs | 无 | active |  |
| BRIK-FILE-006 | file.read_lines | FILE | FILE/BRIK-FILE-006_file.read_lines.cs | 无 | active |  |
| BRIK-FILE-007 | file.tree | FILE | FILE/BRIK-FILE-007_file.tree.cs | 无 | active |  |
| BRIK-FILE-008 | file.find | FILE | FILE/BRIK-FILE-008_file.find.cs | 无 | active |  |
| BRIK-FILE-009 | file.move | FILE | FILE/BRIK-FILE-009_file.move.cs | 无 | active |  |
| BRIK-FILE-010 | file.delete | FILE | FILE/BRIK-FILE-010_file.delete.cs | 无 | active |  |
| BRIK-FILE-011 | file.batch | FILE | FILE/BRIK-FILE-011_file.batch.cs | 无 | active |  |
| BRIK-LLM-001 | llm.chat | LLM | LLM/BRIK-LLM-001_llm.chat.cs | 无 | active |  |
| BRIK-LLM-002 | llm.stream | LLM | LLM/BRIK-LLM-002_llm.stream.cs | 无 | active |  |
| BRIK-LLM-003 | llm.ctx_set_system | LLM | LLM/BRIK-LLM-003_llm.ctx_set_system.cs | 无 | active |  |
| BRIK-LLM-004 | llm.ctx_push_user | LLM | LLM/BRIK-LLM-004_llm.ctx_push_user.cs | 无 | active |  |
| BRIK-LLM-005 | llm.ctx_push_assistant | LLM | LLM/BRIK-LLM-005_llm.ctx_push_assistant.cs | 无 | active |  |
| BRIK-LLM-006 | llm.ctx_trim | LLM | LLM/BRIK-LLM-006_llm.ctx_trim.cs | 无 | active |  |
| BRIK-LLM-007 | llm.ctx_count | LLM | LLM/BRIK-LLM-007_llm.ctx_count.cs | 无 | active |  |
| BRIK-LLM-008 | llm.ctx_build_prompt | LLM | LLM/BRIK-LLM-008_llm.ctx_build_prompt.cs | 无 | active |  |
| BRIK-LLM-009 | llm.ctx_clear | LLM | LLM/BRIK-LLM-009_llm.ctx_clear.cs | 无 | active |  |
| BRIK-LLM-010 | llm.ctx_push_tool | LLM | LLM/BRIK-LLM-010_llm.ctx_push_tool.cs | 无 | active |  |
| BRIK-LLM-011 | llm.ctx_push_assistant_tool_calls | LLM | LLM/BRIK-LLM-011_llm.ctx_push_assistant_tool_calls.cs | 无 | active |  |
| BRIK-LLM-012 | llm.ctx_build_messages_json | LLM | LLM/BRIK-LLM-012_llm.ctx_build_messages_json.cs | 无 | active |  |
| BRIK-LLM-013 | llm.completions | LLM | LLM/BRIK-LLM-013_llm.completions.cs | 无 | active |  |
| BRIK-LLM-014 | llm.is_end | LLM | LLM/BRIK-LLM-014_llm.is_end.cs | 无 | active |  |
| BRIK-LLM-015 | llm.is_tool | LLM | LLM/BRIK-LLM-015_llm.is_tool.cs | 无 | active |  |
| BRIK-LLM-016 | llm.has_error | LLM | LLM/BRIK-LLM-016_llm.has_error.cs | 无 | active |  |
| BRIK-LLM-017 | llm.ctx_checkpoint | LLM | LLM/BRIK-LLM-017_llm.ctx_checkpoint.cs | 无 | active |  |
| BRIK-LLM-018 | llm.ctx_rollback | LLM | LLM/BRIK-LLM-018_llm.ctx_rollback.cs | 无 | active |  |
| BRIK-LLM-019 | llm.ctx_push_error | LLM | LLM/BRIK-LLM-019_llm.ctx_push_error.cs | 无 | active |  |
| BRIK-LLM-020 | llm.read_chunk | LLM | LLM/BRIK-LLM-020_llm.read_chunk.cs | 无 | active |  |
| BRIK-LLM-021 | llm.finish | LLM | LLM/BRIK-LLM-021_llm.finish.cs | 无 | active |  |
| BRIK-LOG-001 | log.write | LOG | LOG/BRIK-LOG-001_log.write.cs | 无 | active |  |
| BRIK-LOG-002 | log.all | LOG | LOG/BRIK-LOG-002_log.all.cs | 无 | active |  |
| BRIK-LOG-003 | log.count | LOG | LOG/BRIK-LOG-003_log.count.cs | 无 | active |  |
| BRIK-LOG-004 | log.clear | LOG | LOG/BRIK-LOG-004_log.clear.cs | 无 | active |  |
| BRIK-MATH-001 | math.is_all_digits | MATH | MATH/BRIK-MATH-001_math.is_all_digits.cs | 无 | active |  |
| BRIK-MATH-002 | math.format_size | MATH | MATH/BRIK-MATH-002_math.format_size.cs | 无 | active |  |
| BRIK-MATH-003 | math.result_preview | MATH | MATH/BRIK-MATH-003_math.result_preview.cs | math.format_size | active |  |
| BRIK-OA-001 | oa.post | OA | OA/BRIK-OA-001_oa.post.cs | 无 | active |  |
| BRIK-OA-002 | oa.list | OA | OA/BRIK-OA-002_oa.list.cs | 无 | active |  |
| BRIK-OA-003 | oa.claim | OA | OA/BRIK-OA-003_oa.claim.cs | 无 | active |  |
| BRIK-OA-004 | oa.complete | OA | OA/BRIK-OA-004_oa.complete.cs | 无 | active |  |
| BRIK-OA-005 | oa.settle | OA | OA/BRIK-OA-005_oa.settle.cs | 无 | active |  |
| BRIK-OA-006 | oa.set_int | OA | OA/BRIK-OA-006_oa.set_int.cs | 无 | active |  |
| BRIK-OA-007 | oa.set_str | OA | OA/BRIK-OA-007_oa.set_str.cs | 无 | active |  |
| BRIK-OA-008 | oa.get_int | OA | OA/BRIK-OA-008_oa.get_int.cs | 无 | active |  |
| BRIK-OA-009 | oa.get_str | OA | OA/BRIK-OA-009_oa.get_str.cs | 无 | active |  |
| BRIK-OA-010 | oa.claim_one | OA | OA/BRIK-OA-010_oa.claim_one.cs | 无 | active |  |
| BRIK-OA-011 | oa.is_closed | OA | OA/BRIK-OA-011_oa.is_closed.cs | 无 | active |  |
| BRIK-OA-012 | oa.complete_simple | OA | OA/BRIK-OA-012_oa.complete_simple.cs | 无 | active |  |
| BRIK-OA-013 | oa.complete_str | OA | OA/BRIK-OA-013_oa.complete_str.cs | 无 | active |  |
| BRIK-OA-014 | oa.claim_one_simple | OA | OA/BRIK-OA-014_oa.claim_one_simple.cs | 无 | active |  |
| BRIK-OA-015 | oa.complete_result | OA | OA/BRIK-OA-015_oa.complete_result.cs | 无 | active |  |
| BRIK-OA-016 | oa.claim_next_simple | OA | OA/BRIK-OA-016_oa.claim_next_simple.cs | 无 | active |  |
| BRIK-OFFICE-001 | excel.read | OFFICE | OFFICE/BRIK-OFFICE-001_excel.read.cs | 无 | active |  |
| BRIK-OFFICE-002 | excel.write | OFFICE | OFFICE/BRIK-OFFICE-002_excel.write.cs | 无 | active |  |
| BRIK-OFFICE-003 | docx.read | OFFICE | OFFICE/BRIK-OFFICE-003_docx.read.cs | 无 | active |  |
| BRIK-OFFICE-004 | docx.write | OFFICE | OFFICE/BRIK-OFFICE-004_docx.write.cs | 无 | active |  |
| BRIK-SHELL-001 | shell.exec | SHELL | SHELL/BRIK-SHELL-001_shell.exec.cs | 无 | active |  |
| BRIK-TEST-001 | probe.source | TEST | TEST/BRIK-TEST-001_probe.source.cs | 无 | active |  |
| BRIK-TEST-002 | probe.sink | TEST | TEST/BRIK-TEST-002_probe.sink.cs | 无 | active |  |
| BRIK-TEXT-001 | text.md_parse | TEXT | TEXT/BRIK-TEXT-001_text.md_parse.cs | 无 | active |  |
| BRIK-TOOL-001 | tool.exec | TOOL | TOOL/BRIK-TOOL-001_tool.exec.cs | 无 | active |  |
| BRIK-TOOL-002 | tool.dispatch_next | TOOL | TOOL/BRIK-TOOL-002_tool.dispatch_next.cs | 无 | active |  |
| BRIK-TOOL-003 | tool.claim_next | TOOL | TOOL/BRIK-TOOL-003_tool.claim_next.cs | 无 | active |  |
| BRIK-TOOL-004 | tool.collect_one | TOOL | TOOL/BRIK-TOOL-004_tool.collect_one.cs | 无 | active |  |
| BRIK-TOOL-005 | tool.is_name | TOOL | TOOL/BRIK-TOOL-005_tool.is_name.cs | 无 | active |  |
| BRIK-TOOL-006 | tool.run_file_read | TOOL | TOOL/BRIK-TOOL-006_tool.run_file_read.cs | file.read | active |  |
| BRIK-TOOL-007 | tool.run_file_write | TOOL | TOOL/BRIK-TOOL-007_tool.run_file_write.cs | file.write | active |  |
| BRIK-TOOL-008 | tool.run_shell_exec | TOOL | TOOL/BRIK-TOOL-008_tool.run_shell_exec.cs | shell.exec | active |  |
| BRIK-TOOL-009 | tool.dispatch_one | TOOL | TOOL/BRIK-TOOL-009_tool.dispatch_one.cs | 无 | active |  |
| BRIK-TOOL-010 | tool.dispatch | TOOL | TOOL/BRIK-TOOL-010_tool.dispatch.cs | 无 | active |  |
| BRIK-TOOL-011 | tool.collect | TOOL | TOOL/BRIK-TOOL-011_tool.collect.cs | 无 | active |  |

---

_版本：v3.1 | 2026-08-07 | 自动生成——`mau bricks index --update`（源码唯一真相源：文件头 + 静态签名；来源列为人工维护区）_
