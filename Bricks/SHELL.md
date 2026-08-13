# Shell 类积木 — SHELL

> 类别码：SHELL | ID 段：BRIK-SHELL-###

| ID | 名字 | 工程路径 | 状态 | 说明 |
|:--|:--|:--|:--|:--|
| BRIK-SHELL-001 | shell.exec | Bricks/SHELL/BRIK-SHELL-001_shell.exec.cs | ✅ | 有界 PowerShell 执行（超时/进程树清理） |

> **去重说明（2026-08-06）：** `tool.run_shell_exec`（ShellToolBrick.cs）原登记为 SHELL-002，与 TOOL-008 同一实现——按工具适配器语义归 TOOL 类，ID 并入 BRIK-TOOL-008。**2026-08-13 退役**——积木内聚原则 B.1 适配器四连退役（读单+执行+回执混装；编排归状态机，调度归 ∥/⋈，执行走原子积木 shell.exec）。

---

_版本：v1.3 | 2026-08-13 | TOOL-008 退役标注（积木内聚原则 B.1）；v1.2 | 2026-08-06 | 语义治理：SHELL-002 去重并入 TOOL-008（同一实现 ShellToolBrick.cs）_
