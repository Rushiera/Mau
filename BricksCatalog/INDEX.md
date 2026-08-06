# Mau 积木索引 — INDEX

> 版本：v2.3 | 创建：2026-08-04 | 更新：2026-08-06（v2.3：语义治理——补 CMD/OA 双字典/TOOL 分发 3 项登记，SHELL-002 去重并入 TOOL-008，标题数修正 47→82）
> 全量积木登记——一行一条。ID 永不重用。

## 全部积木（82 个）

| ID | 名字 | 类别 | 工程路径 | 状态 | 来源 |
|:--|:--|:--|:--|:--|:--|
| BRIK-FILE-001 | file.convert | FILE | Mau.Bricks.Standard/FileBrick.cs | ✅ | 第一期 P3 |
| BRIK-FILE-002 | file.read | FILE | Mau.Bricks.Standard/FileBrick.cs | ✅ | CH3 CH_FileToolCatalog |
| BRIK-FILE-003 | file.write | FILE | Mau.Bricks.Standard/FileBrick.cs | ✅ | CH3 CH_FileToolCatalog |
| BRIK-FILE-004 | file.append | FILE | Mau.Bricks.Standard/FileBrick.cs | ✅ | CH3 CH_FileToolCatalog |
| BRIK-FILE-005 | file.replace | FILE | Mau.Bricks.Standard/FileBrick.cs | ✅ | CH3 CH_FileToolCatalog |
| BRIK-FILE-006 | file.read_lines | FILE | Mau.Bricks.Standard/FileBrick.cs | ✅ | CH3 CH_FileToolCatalog |
| BRIK-FILE-007 | file.tree | FILE | Mau.Bricks.Standard/FileBrick.cs | ✅ | CH3 CH_FileToolCatalog |
| BRIK-FILE-008 | file.find | FILE | Mau.Bricks.Standard/FileBrick.cs | ✅ | CH3 CH_FileToolCatalog |
| BRIK-FILE-009 | file.move | FILE | Mau.Bricks.Standard/FileBrick.cs | ✅ | CH3 CH_FileToolCatalog |
| BRIK-FILE-010 | file.delete | FILE | Mau.Bricks.Standard/FileBrick.cs | ✅ | CH3 CH_FileToolCatalog |
| BRIK-FILE-011 | file.batch | FILE | Mau.Bricks.Standard/FileBrick.cs | ✅ | CH3 CH_FileToolCatalog |
| BRIK-MATH-001 | math.is_all_digits | MATH | Mau.Bricks.Standard/MathBrick.cs | ✅ | CH3 CH_Tool_Math |
| BRIK-MATH-002 | math.format_size | MATH | Mau.Bricks.Standard/MathBrick.cs | ✅ | CH3 CH_Tool_Math |
| BRIK-MATH-003 | math.result_preview | MATH | Mau.Bricks.Standard/MathBrick.cs | ✅ | CH3 CH_Tool_Math |
| BRIK-DATA-001 | data.snapshot_encode | DATA | Mau.Bricks.Data/DataBrick.cs | ✅ | CH3 CH_Tool_Serialize |
| BRIK-DATA-002 | data.snapshot_decode | DATA | Mau.Bricks.Data/DataBrick.cs | ✅ | CH3 CH_Tool_Serialize |
| BRIK-DATA-003 | data.box_set | DATA | Mau.Bricks.Data/ValueBox.cs | ✅ | CH3 CH_ValueBox |
| BRIK-DATA-004 | data.box_get | DATA | Mau.Bricks.Data/ValueBox.cs | ✅ | CH3 CH_ValueBox |
| BRIK-DATA-005 | data.box_set_dic | DATA | Mau.Bricks.Data/ValueBox.cs | ✅ | CH3 CH_ValueBox |
| BRIK-DATA-006 | data.box_get_dic | DATA | Mau.Bricks.Data/ValueBox.cs | ✅ | CH3 CH_ValueBox |
| BRIK-TEXT-001 | text.md_parse | TEXT | Mau.Bricks.Text/TextBrick.cs | ✅ | CH3 CH_Tool_MD |
| BRIK-SHELL-001 | shell.exec | SHELL | Mau.Bricks.Shell/ShellBrick.cs | ✅ | CH3 CH_ShellTool |
| BRIK-LLM-001 | llm.chat | LLM | Mau.Bricks.LLM/LlmBrick.cs | ✅ | CH3 CH_DeepSeekProvider |
| BRIK-LLM-002 | llm.stream | LLM | Mau.Bricks.LLM/LlmBrick.cs | ✅ | CH3 CH_DeepSeekProvider |
| BRIK-LLM-002b | llm.read_chunk | LLM | Mau.Bricks.LLM/LlmBrick.cs | ✅ | CH4 P2.2 流式分片消费 |
| BRIK-LLM-002c | llm.finish | LLM | Mau.Bricks.LLM/LlmBrick.cs | ✅ | CH4 P2.2 终止流式会话 |
| BRIK-LLM-003 | llm.ctx_set_system | LLM | Mau.Bricks.LLM/ContextBrick.cs | ✅ | CH3 CH_Kit_ContextManager |
| BRIK-LLM-004 | llm.ctx_push_user | LLM | Mau.Bricks.LLM/ContextBrick.cs | ✅ | CH3 CH_Kit_ContextManager |
| BRIK-LLM-005 | llm.ctx_push_assistant | LLM | Mau.Bricks.LLM/ContextBrick.cs | ✅ | CH3 CH_Kit_ContextManager |
| BRIK-LLM-006 | llm.ctx_trim | LLM | Mau.Bricks.LLM/ContextBrick.cs | ✅ | CH3 CH_Kit_ContextManager |
| BRIK-LLM-007 | llm.ctx_count | LLM | Mau.Bricks.LLM/ContextBrick.cs | ✅ | CH3 CH_Kit_ContextManager |
| BRIK-LLM-008 | llm.ctx_build_prompt | LLM | Mau.Bricks.LLM/ContextBrick.cs | ✅ | CH3 CH_Kit_ContextManager |
| BRIK-LLM-009 | llm.ctx_clear | LLM | Mau.Bricks.LLM/ContextBrick.cs | ✅ | CH3 CH_Kit_ContextManager |
| BRIK-LLM-010 | llm.ctx_push_tool | LLM | Mau.Bricks.LLM/ContextBrick.cs | ✅ | CH4 P2.2 工具回执 |
| BRIK-LLM-011 | llm.ctx_push_assistant_tool_calls | LLM | Mau.Bricks.LLM/ContextBrick.cs | ✅ | CH4 P2.2 Assistant 工具声明 |
| BRIK-LLM-012 | llm.ctx_build_messages_json | LLM | Mau.Bricks.LLM/ContextBrick.cs | ✅ | CH4 P2.2 结构化导出 |
| BRIK-LLM-013 | llm.completions | LLM | Mau.Bricks.LLM/LlmBrick.cs | ✅ | CH4 P2.2 结构化流式 |
| BRIK-LLM-014 | llm.is_end | LLM | Mau.Bricks.LLM/LlmBrick.cs | ✅ | CH4 P2.2 终态判断 |
| BRIK-LLM-015 | llm.is_tool | LLM | Mau.Bricks.LLM/LlmBrick.cs | ✅ | CH4 P2.2 工具判断 |
| BRIK-LLM-016 | llm.has_error | LLM | Mau.Bricks.LLM/LlmBrick.cs | ✅ | CH4 P2.2a 错误判断 |
| BRIK-LLM-017 | llm.ctx_checkpoint | LLM | Mau.Bricks.LLM/ContextBrick.cs | ✅ | CH4 P2.2a 回滚断点 |
| BRIK-LLM-018 | llm.ctx_rollback | LLM | Mau.Bricks.LLM/ContextBrick.cs | ✅ | CH4 P2.2a 回滚 |
| BRIK-LLM-019 | llm.ctx_push_error | LLM | Mau.Bricks.LLM/ContextBrick.cs | ✅ | CH4 P2.2a 错误码入流 |
| BRIK-APPROVAL-001 | approval.request | APPROVAL | Mau.Bricks.Approval/ApprovalBrick.cs | ✅ | CH3 CH_ApprovalBroker |
| BRIK-APPROVAL-002 | approval.resolve | APPROVAL | Mau.Bricks.Approval/ApprovalBrick.cs | ✅ | CH3 CH_ApprovalBroker |
| BRIK-APPROVAL-003 | approval.reject | APPROVAL | Mau.Bricks.Approval/ApprovalBrick.cs | ✅ | CH3 CH_ApprovalBroker |
| BRIK-APPROVAL-004 | approval.pending | APPROVAL | Mau.Bricks.Approval/ApprovalBrick.cs | ✅ | CH3 CH_ApprovalBroker |
| BRIK-OFFICE-001 | excel.read | OFFICE | Mau.Bricks.Office/ExcelBrick.cs | ✅ | CH2 CH_Kit_Excel |
| BRIK-OFFICE-002 | excel.write | OFFICE | Mau.Bricks.Office/ExcelBrick.cs | ✅ | CH2 CH_Kit_Excel |
| BRIK-OFFICE-003 | docx.read | OFFICE | Mau.Bricks.Office/DocxBrick.cs | ✅ | CH2 CH_Kit_Word |
| BRIK-OFFICE-004 | docx.write | OFFICE | Mau.Bricks.Office/DocxBrick.cs | ✅ | CH2 CH_Kit_Word |
| BRIK-LOG-001 | log.write | LOG | Mau.Bricks.Log/LogBrick.cs | ✅ | CH2 CH_Tool_Log |
| BRIK-LOG-002 | log.all | LOG | Mau.Bricks.Log/LogBrick.cs | ✅ | CH2 CH_Tool_Log |
| BRIK-LOG-003 | log.count | LOG | Mau.Bricks.Log/LogBrick.cs | ✅ | CH2 CH_Tool_Log |
| BRIK-LOG-004 | log.clear | LOG | Mau.Bricks.Log/LogBrick.cs | ✅ | CH2 CH_Tool_Log |
| BRIK-CMD-001 | cmd.register | CMD | Mau.Bricks.Standard/CmdBrick.cs | ✅ | CH4 P2.2 指令入口 |
| BRIK-CMD-002 | cmd.unregister | CMD | Mau.Bricks.Standard/CmdBrick.cs | ✅ | CH4 P2.2 指令入口 |
| BRIK-CMD-003 | cmd.consume | CMD | Mau.Bricks.Standard/CmdBrick.cs | ✅ | CH4 P2.2 指令入口 |
| BRIK-CMD-004 | cmd.set | CMD | Mau.Bricks.Standard/CmdBrick.cs | ✅ | CH4 P2.2 指令入口 |
| BRIK-CMD-005 | cmd.clean | CMD | Mau.Bricks.Standard/CmdBrick.cs | ✅ | CH4 P2.2 指令入口 |
| BRIK-OA-001 | oa.post | OA | Mau.Bricks.Standard/OaBrick.cs | ✅ | CH4 P1.3 机制积木（包装 Mau.Runtime.OA） |
| BRIK-OA-002 | oa.list | OA | Mau.Bricks.Standard/OaBrick.cs | ✅ | CH4 P1.3 机制积木（包装 Mau.Runtime.OA） |
| BRIK-OA-003 | oa.claim | OA | Mau.Bricks.Standard/OaBrick.cs | ✅ | CH4 P1.3 机制积木（包装 Mau.Runtime.OA） |
| BRIK-OA-004 | oa.complete | OA | Mau.Bricks.Standard/OaBrick.cs | ✅ | CH4 P1.3 机制积木（包装 Mau.Runtime.OA） |
| BRIK-OA-005 | oa.settle | OA | Mau.Bricks.Standard/OaBrick.cs | ✅ | CH4 P1.3 机制积木（包装 Mau.Runtime.OA） |
| BRIK-OA-006 | oa.set_int | OA | Mau.Bricks.Standard/OaBrick.cs | ✅ | CH4 P1.3 双字典载荷（v0.40 升级） |
| BRIK-OA-007 | oa.set_str | OA | Mau.Bricks.Standard/OaBrick.cs | ✅ | CH4 P1.3 双字典载荷（v0.40 升级） |
| BRIK-OA-008 | oa.get_int | OA | Mau.Bricks.Standard/OaBrick.cs | ✅ | CH4 P1.3 双字典载荷（v0.40 升级） |
| BRIK-OA-009 | oa.get_str | OA | Mau.Bricks.Standard/OaBrick.cs | ✅ | CH4 P1.3 双字典载荷（v0.40 升级） |
| BRIK-OA-010 | oa.claim_one | OA | Mau.Bricks.Standard/OaBrick.cs | ✅ | CH4 P2.2 单单认领（工具循环展开） |
| BRIK-OA-011 | oa.is_closed | OA | Mau.Bricks.Standard/OaBrick.cs | ✅ | CH4 P2.2 单状态判断（轮询用） |
| BRIK-TOOL-001 | tool.exec | TOOL | Mau.Bricks.Standard/ToolBrick.cs | ✅ | CH4 P2 机制积木（工具分发执行器） |
| BRIK-TOOL-002 | tool.dispatch_next | TOOL | Mau.Bricks.Standard/ToolBrick.cs | ✅ | CH4 P2.2 游标发单 |
| BRIK-TOOL-003 | tool.claim_next | TOOL | Mau.Bricks.Standard/ToolBrick.cs | ✅ | CH4 P2.2 游标认领 |
| BRIK-TOOL-004 | tool.collect_one | TOOL | Mau.Bricks.Standard/ToolBrick.cs | ✅ | CH4 P2.2 单值收集 |
| BRIK-TOOL-005 | tool.is_name | TOOL | Mau.Bricks.Standard/ToolBrick.cs | ✅ | CH4 P2.2 工具名匹配 |
| BRIK-TOOL-006 | tool.run_file_read | TOOL | Mau.Bricks.Standard/ToolBrick.cs | ✅ | CH4 P2.2 读适配器 |
| BRIK-TOOL-007 | tool.run_file_write | TOOL | Mau.Bricks.Standard/ToolBrick.cs | ✅ | CH4 P2.2 写适配器 |
| BRIK-TOOL-008 | tool.run_shell_exec | TOOL | Mau.Bricks.Shell/ShellToolBrick.cs | ✅ | CH4 P2.3 Shell 适配器（原 SHELL-002 并入） |
| BRIK-TOOL-009 | tool.dispatch_one | TOOL | Mau.Bricks.Standard/ToolBrick.cs | ✅ | CH4 P2.2 单单发 |
| BRIK-TOOL-010 | tool.dispatch | TOOL | Mau.Bricks.Standard/ToolBrick.cs | ✅ | CH4 P2.2 批量发单 |
| BRIK-TOOL-011 | tool.collect | TOOL | Mau.Bricks.Standard/ToolBrick.cs | ✅ | CH4 P2.2 批量收集 |

---

_版本：v2.3 | 2026-08-06 | 语义治理：补 CMD 5 / OA 双字典 6 / TOOL 分发 3；SHELL-002 去重并入 TOOL-008（同一实现 ShellToolBrick.cs）；总数修正 47→82（含 002b/002c 子编号）_

> 2026-08-05 追加：+TOOL 机制积木 1 个（BRIK-TOOL-001 tool.exec）——工具分发执行器，Mau.Corpus oa_flow/tool_dispatch 模板前置
> 2026-08-06 语义治理：INDEX 补登记 CMD（5）/ OA 双字典（set_int/set_str/get_int/get_str/claim_one/is_closed）/ TOOL（dispatch_one/dispatch/collect）——代码已注册但 INDEX 缺失；SHELL-002 与 TOOL-008 同一实现（ShellToolBrick.cs）→ 去重保留 TOOL-008

## 机制积木说明（OA）

> OA 积木与叶子积木的区别：叶子积木（file.*/log.* 等）自带实现；机制积木（oa.*）包装 Mau.Runtime 机制，实例由宿主注入（`OaBrick.Configure(IOA)`）——语料只声明拓扑动作，机制留基座。

| 积木 | 语义 | 线程 |
|:--|:--|:--|
| oa.post | 上架工单——挂单方投递，返回 OfficeId | main |
| oa.list | 查 Open 单——大类 + 候选 OfficeName | main |
| oa.claim | 锁单——逐个尝试认领，返回锁成功名单 | main |
| oa.complete | 完成——写回执 → Closed | main |
| oa.settle | 结算——Work 单退回 Open（重投）；已终结单确认 | main |
| oa.set_int/set_str | 写请求载荷（本人/Open） | main |
| oa.get_int/get_str | 读请求载荷（执行方消费） | main |
| oa.claim_one | 单单认领——工具循环展开用 | main |
| oa.is_closed | 单状态判断——轮询用 | main |
