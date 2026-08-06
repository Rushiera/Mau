# 工具机制积木 — TOOL

> 类别码：TOOL | ID 段：BRIK-TOOL-### | 类别说明：工具分发机制积木——语料声明分发拓扑，执行器由宿主注入

| ID | 名字 | 工程路径 | 状态 | 说明 |
|:--|:--|:--|:--|:--|
| BRIK-TOOL-001 | tool.exec | Mau.Bricks.Standard/ToolBrick.cs | ✅ | 工具执行——按工具名分发，返回结果文本 |
| BRIK-TOOL-002 | tool.dispatch_next | Mau.Bricks.Standard/ToolBrick.cs | ✅ | 游标式逐单发——sessionKey 游标（规避数组） |
| BRIK-TOOL-003 | tool.claim_next | Mau.Bricks.Standard/ToolBrick.cs | ✅ | 游标式认领 TOOL 单（file.read/write/shell.exec 候选） |
| BRIK-TOOL-004 | tool.collect_one | Mau.Bricks.Standard/ToolBrick.cs | ✅ | 单值收集工具结果（callId+content） |
| BRIK-TOOL-005 | tool.is_name | Mau.Bricks.Standard/ToolBrick.cs | ✅ | 工具名匹配判断（返回=判断结果） |
| BRIK-TOOL-006 | tool.run_file_read | Mau.Bricks.Standard/ToolBrick.cs | ✅ | file.read 适配器——展平参数→执行→回执→Complete |
| BRIK-TOOL-007 | tool.run_file_write | Mau.Bricks.Standard/ToolBrick.cs | ✅ | file.write 适配器——展平参数→执行→回执→Complete |
| BRIK-TOOL-008 | tool.run_shell_exec | Mau.Bricks.Shell/ShellToolBrick.cs | ✅ | shell.exec 适配器——args.command→ShellBrick.Exec→回执→Complete |

---

_版本：v1.1 | 2026-08-06 | +工具循环适配器族（dispatch/claim/collect/is_name/run_*——CH4 P2.2 工具循环）_
