# Shell 类积木 — SHELL

> 类别码：SHELL | ID 段：BRIK-SHELL-###

| ID | 名字 | 工程路径 | 状态 | 说明 |
|:--|:--|:--|:--|:--|
| BRIK-SHELL-001 | shell.exec | Mau.Bricks.Shell/ShellBrick.cs | ✅ | 有界 PowerShell 执行（白名单/超时/进程树） |

> **去重说明（2026-08-06）：** `tool.run_shell_exec`（ShellToolBrick.cs）原登记为 SHELL-002，与 TOOL-008 同一实现——按工具适配器语义归 TOOL 类，ID 并入 BRIK-TOOL-008。ShellToolBrick 源码保留在 Mau.Bricks.Shell 工程（物理位置不变），登记归属以 INDEX.md 为准。

---

_版本：v1.2 | 2026-08-06 | 语义治理：SHELL-002 去重并入 TOOL-008（同一实现 ShellToolBrick.cs）_
