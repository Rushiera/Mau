# Mau — 工程纪律

> 位置：workspace 根。项目规格文档统一在 CCBP 知识网络：`Data/CatCatBigParty/Project/Mau/`（project.md / design-mau*.md / todo.md / CHANGELOG.md）。

---

## 纲领

### 纲领一：基座零控制台依赖

**Mau.Runtime / Mau.Contracts / Mau.Bricks 等库内部禁止使用控制台限定 API（Console.WriteLine 等）输出日志。** 诊断与日志必须走可注入通道——trace 事件（MauTraceHub）/ 日志委托——由宿主决定呈现形态。

基座是 lib，控制台是宿主形态，不是基座形态。任何基座库引入 Console 依赖 = 纲领违反。

### 纲领二：入口只是入口

**Mau.Host（consoleapp）的唯一职责是引导**——组装（注册积木/初始化基座）、启动、等待信号、退出。业务逻辑、构筑逻辑、观测逻辑全部在基座与工具中，入口零业务。

### 纲领三：工具链全 C#

**门禁/构筑/检查一律 C# 实现（`mau test` / `mau build`），禁止 .ps1/.py 等本机脚本承载工程逻辑。** 本机环境重依赖是历史教训（CH3 的 Codex 脚本堆是重构理由之一）。Mau 是自包含方言环境——工具链全部在 C# 内，唯一的外部命令是环境 .NET SDK（dotnet build，由 C# Process 调用）。

---

## 结构

workspace 目录结构见 `README.md`。

## 门禁

```bash
mau test
```
成功标记：`MAU_CHECKS_OK`（L2 翻译器测试 + L5 生成物真实运行 + L3 黄金文件对比，全 C# 编排）
