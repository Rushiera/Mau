# 工具机制积木 — TOOL

> 类别码：TOOL | ID 段：BRIK-TOOL-### | 类别说明：工具分发机制积木——语料声明分发拓扑，执行器由宿主注入

| ID | 名字 | 工程路径 | 状态 | 说明 |
|:--|:--|:--|:--|:--|
| BRIK-TOOL-001 | tool.exec | Bricks/TOOL/BRIK-TOOL-001_tool.exec.cs | ✅ | 工具执行——按工具名分发，返回结果文本 |
| BRIK-TOOL-002 | tool.dispatch_next | Bricks/TOOL/BRIK-TOOL-002_tool.dispatch_next.cs | ✅ | 游标式逐单发——sessionKey 游标（规避数组） |
| BRIK-TOOL-003 | tool.claim_next | Bricks/TOOL/BRIK-TOOL-003_tool.claim_next.cs | ✅ | 游标式认领 TOOL 单（file.read/write/shell.exec 候选） |
| BRIK-TOOL-004 | tool.collect_one | Bricks/TOOL/BRIK-TOOL-004_tool.collect_one.cs | ✅ | 单值收集工具结果（callId+content） |
| BRIK-TOOL-005 | tool.is_name | Bricks/TOOL/BRIK-TOOL-005_tool.is_name.cs | ✅ | 工具名匹配判断（返回=判断结果） |
| BRIK-TOOL-006 | tool.run_file_read | Bricks/TOOL/BRIK-TOOL-006_tool.run_file_read.cs | ✅ | file.read 适配器——展平参数→执行→回执→Complete |
| BRIK-TOOL-007 | tool.run_file_write | Bricks/TOOL/BRIK-TOOL-007_tool.run_file_write.cs | ✅ | file.write 适配器——展平参数→执行→回执→Complete |
| BRIK-TOOL-008 | tool.run_shell_exec | Bricks/TOOL/BRIK-TOOL-008_tool.run_shell_exec.cs | ✅ | shell.exec 适配器——args.command→ShellBrick.Exec→回执→Complete（原 SHELL-002 并入） |
| BRIK-TOOL-009 | tool.dispatch_one | Bricks/TOOL/BRIK-TOOL-009_tool.dispatch_one.cs | ✅ | 单单发——逐单发（非游标） |
| BRIK-TOOL-010 | tool.dispatch | Bricks/TOOL/BRIK-TOOL-010_tool.dispatch.cs | ✅ | 批量发单——工具集分发 |
| BRIK-TOOL-011 | tool.collect | Bricks/TOOL/BRIK-TOOL-011_tool.collect.cs | ✅ | 批量收集工具结果 |
| BRIK-TOOL-012 | tool.create_next | Bricks/TOOL/BRIK-TOOL-012_tool.create_next.cs | ✅ | Dog 载体逐单发——游标 + DogBase + 载荷展平（M2b ToolPoster 发单核心） |
| BRIK-TOOL-013 | tool.run_generic | Bricks/TOOL/BRIK-TOOL-013_tool.run_generic.cs | ✅ | 六域通用工具适配器（file/shell/office/system/csharp/mau——result/error 回执自动 Complete） |

---

_版本：v1.2 | 2026-08-06 | 语义治理：补 TOOL-009~011（dispatch_one/dispatch/collect 已注册未登记）+ TOOL-008 注明原 SHELL-002 并入_
