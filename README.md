# Mau

> 埃及语"猫"（mau/mjw）。古埃及太阳神 Ra 亦被称为"伟大的猫 Mau"。

**Mau** 是一门架在 .NET 8 之上的声明式方言环境——把程序逻辑的定义权从框架转移到语句，由约定协议规定程序最顶层的设计和时序关系。

---

## 核心理念

```
公式层（.mau 声明）— 系统是什么
    ↓ 构筑：解析 → IR → 静态验证
翻译层（C#）— 解析器 → 验证 → 代码生成
    ↓ 生成 C# 源码
积木层（C#）— 语义原语（叶子黑盒）
    ↓ Roslyn Emit
运行时（Mau.Runtime）— Tick / ALC / OA / 快照
```

- **公式层是宪法，C# 是执行者** —— 系统结构真相全部由 Mau 承载
- **翻译层归我们所有** —— 翻译器和积木用 C# 实现，可重写可移植
- **只在基座下有效** —— 语义精确到函数级

---

## 工程纪律

> 项目规格文档统一在 CCBP 知识网络：`Data/CatCatBigParty/Project/Mau/`（project.md / design-mau*.md / todo.md / CHANGELOG.md / mau-usage.md）。本 README 只承载仓库身份与纲领——大版本更新时同步。

### 纲领一：基座零控制台依赖

**Mau.Runtime / Mau.Contracts / Mau.Bricks 等库内部禁止使用控制台限定 API（Console.WriteLine 等）输出日志。** 诊断与日志必须走可注入通道——trace 事件（MauTraceHub）/ 日志委托——由宿主决定呈现形态。

基座是 lib，控制台是宿主形态，不是基座形态。任何基座库引入 Console 依赖 = 纲领违反。

### 纲领二：入口只是入口

**Mau.Host（consoleapp）的唯一职责是引导**——组装（注册积木/初始化基座）、启动、等待信号、退出。业务逻辑、构筑逻辑、观测逻辑全部在基座与工具中，入口零业务。

### 纲领三：工具链全 C#

**门禁/构筑/检查一律 C# 实现（`mau test` / `mau build`），禁止 .ps1/.py 等本机脚本承载工程逻辑。** 本机环境重依赖是历史教训（CH3 的 Codex 脚本堆是重构理由之一）。Mau 是自包含方言环境——工具链全部在 C# 内，唯一的外部命令是环境 .NET SDK（dotnet build，由 C# Process 调用）。

---

## 五逻辑单元

| 单元 | 含义 |
|:--|:--|
| 命题 Proposition | "什么成立"——状态：条件/信号/事实 |
| 变迁 Transition | "什么触发什么"——前置→动作→双后置 |
| 通道 Channel | "什么流向什么"——跨线程/跨进程 |
| 组合 Composition | "什么与什么并列/串/选择/重试" |
| 资源 Resource | "什么被消耗/独占"——令牌/引用/配额 |

---

## 工程结构

| 项目 | 定位 |
|:--|:--|
| `Mau.Runtime` | 基座——Cube/IFlow/Inbox/IClock/MauTrace |
| `Mau.Contracts` | 积木契约——注册表/端口/契约条目 |
| `Mau.Translator` | 翻译器——解析→IR→静态验证→代码生成 |
| `Mau.Bricks.Standard` | 标准积木——file.convert 等 |
| `Mau.Cli` | 命令行——verify/gen/test |
| `Mau.Host` | 入口壳——引导/组装/启动 |
| `Mau.*.Tests` | 测试——翻译器/运行时/积木/契约/E2E |
| `Mau.Snapshots` | 黄金文件——语料+预期生成物 |

---

## 快速开始

```bash
# 构筑
dotnet build Mau.sln

# 运行门禁
dotnet run --project Mau.Cli test

# 翻译 .mau 文件
dotnet run --project Mau.Cli gen --input cases/demo.mau
```

---

## 许可证

MIT License · 详见 [LICENSE](./LICENSE)
