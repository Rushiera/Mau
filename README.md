# Mau

> 埃及语"猫"（mau/mjw）。古埃及太阳神 Ra 亦被称为"伟大的猫 Mau"。

**Mau** 是一门架在 .NET 8 之上的声明式方言环境——把程序逻辑的定义权从框架转移到语句，由约定协议规定程序最顶层的设计和时序关系。

> 版本：v0.55 | 更新：2026-08-07（审查修复 v0.54 + 防护定性 v0.55）

---

## 核心理念

```
公式层（.mau 声明）— 系统是什么
    ↓ 构筑：解析 → IR → 静态验证
翻译层（C#）— 解析器 → 验证 → 代码生成（BrickIndex 查询 + BRIKGROUP 内嵌）
    ↓ 生成 C# 源码（骨架 + 积木源码复制，自包含）
运行时（Mau.Runtime）— Tick / ALC / OA / 快照 / DataBox
```

- **公式层是宪法，C# 是执行者** —— 系统结构真相全部由 Mau 承载
- **翻译层归我们所有** —— 翻译器和积木用 C# 实现，可重写可移植
- **只在基座下有效** —— 语义精确到函数级
- **积木是文本语料，翻译器产物必须完备** —— 生成物自包含所用积木源码（复制即单包），运行时零积木程序集

---

## 三件套

| 件 | 位置 | 职责 |
|:--|:--|:--|
| `Mau.exe` | `Mau.Cli/` | 唯一工具集——翻译 / 构筑 / 测试 / 检测（verify / gen / build / test / check / bricks / serve ...） |
| `MauRuntime` | `Mau.Runtime/` | 基座 lib——机制（Cube/OA/Command/FlowALC/Inbox/ThreadGuard）+ **DataBox 中台**（BRIK 唯一数据协议）+ 程序级服务 |
| `Bricks/` | 仓库根 | **文本资产库**——84 积木（BRIK-{ID}_{name}.cs，单文件 + 文件头十字段 + SHA256 校验尾）+ index.json v3（**源码驱动**契约索引） |

**构筑闭环：** `.mau` → Mau.exe（BrickIndex 查索引 → 闭包收集 → 校验尾验证 → BRIK-ID 重命名 → BRIKGROUP 内嵌）→ Roslyn Emit → 自包含 dll。组模式多语料共享 BRIKGROUP。

**索引闭环（唯一寻路）：** `Bricks/{类别}/*.cs`（文件头十字段 + 静态方法签名 = 契约唯一真相源）→ `mau bricks index --update`（Roslyn 提取 inputs/outputs/return + 文件头时长/线程）→ index.json v3 + INDEX.md → 翻译器 / bricks 命令 / check 积木谱全部经 BrickIndex 查询——不存在第二条积木寻路。

---

## 工程纪律

> 项目规格文档统一在 CCBP 知识网络：`Data/CatCatBigParty/Project/Mau/`（project.md / design-mau*.md / todo.md / CHANGELOG.md / mau-usage.md）。本 README 只承载仓库身份与纲领——大版本更新时同步。

### 纲领一：基座零控制台依赖

**Mau.Runtime 库内部禁止使用控制台限定 API（Console.WriteLine 等）输出日志。** 诊断与日志必须走可注入通道——trace 事件（MauTrace）/ 日志委托——由宿主决定呈现形态。基座是 lib，控制台是宿主形态。

### 纲领二：入口只是入口

**入口的唯一职责是引导**——组装（DataBox 装配/初始化基座）、启动、等待信号、退出。业务逻辑、构筑逻辑、观测逻辑全部在基座与工具中，入口零业务。

### 纲领三：工具链全 C#

**门禁/构筑/检查一律 C# 实现（`mau test` / `mau check` / `mau build`），禁止 .ps1/.py 等本机脚本承载工程逻辑。** 唯一的外部命令是环境 .NET SDK（dotnet build，由 C# Process 调用）。

### 纲领四：DataBox 唯一数据协议

**所有 BRIK 的共享状态/服务必须经 DataBox（Bind/Resolve + scope 存储 + Capture），禁止绕过中台直连。** 严格封装——BRIK 只见 API。SUPPORT 类目已消亡：契约类型入 Mau.Runtime、状态表走 DataBox scope、纯辅助内联或程序级。

### 纲领五：包依赖红线

**Mau 是 .NET 生态的方言层——产物 = dll，运行 = Mau.exe（依赖 .NET 环境），SDK 本身就是生态集合（Roslyn 是独立开源包但即 .NET 一部分，Mau 同理）。** 依赖一切 NuGet 生态可拉取、来源稳定、质量合格的包（ClosedXML/OpenXml/Roslyn 全部直引）——**直接引用，不设中间层**。唯一禁区：**拉不到的包（私有闭源/手工分发/来源不明）一律禁止进入 Bricks 依赖**。不做"自包含闭合第三方包"式工程——那是对 .NET 生态的重复劳动。

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
| `Bricks/` | 文本资产库——84 积木（十字段 + 校验尾）+ index.json v3（源码驱动） |
| `Mau.Runtime` | 基座——机制 + DataBox 中台 + 程序级服务（FileSystemService/LlmBridge/LogStore 等） |
| `Mau.Contracts` | 契约类型——BrickContract/端口/导出属性 |
| `Mau.Translator` | 翻译器——解析→IR→静态验证→生成（BrickIndex/BrickEmbedder/CompileGroup） |
| `Mau.Cli` | 唯一工具——命令分发（verify/gen/build/test/check/bricks/serve/ps 等） |
| `Mau.Development` | 口袋编译——Roslyn Emit/源码工作区 |
| `Mau.Serve` / `Mau.Observer` | NamedPipe 服务层 / 观测器 |
| `Mau.Host` | 入口壳——引导/组装/启动 |
| `Mau.Corpus` | 语料模板蓝图（设计蓝图，不入门禁） |
| `Mau.Snapshots` | 门禁基准——cases（语料）/ expected（黄金）/ checks（全谱） |
| `*.Tests` | 测试——翻译器/运行时/开发/服务/E2E |

---

## Git 版本管理规范

### 分支策略

**主干开发（Trunk-Based Development）。**

| 分支 | 用途 | 规则 |
|:--|:--|:--|
| `master` | 唯一真相源 | 始终可编译、可运行门禁 |
| `feature/*` | 新功能/修复 | 从 master 切出，合并回 master |

- 小改动（<200 行）直接在 master 提交
- 中大型改动（≥200 行或跨多项目）走 `feature/` 分支，合并前跑完整门禁
- 不设 `develop`、`release` 等长驻分支

### 提交粒度

**一个提交 = 一个可独立理解的最小变更单元。**

| 规则 | 说明 |
|:--|:--|
| 原子性 | 一个提交只做一件事 |
| 可编译 | 每次提交后 `dotnet build Mau.sln` 零错误 |
| 门禁不倒退 | 不提交已知会破坏门禁的代码 |
| 禁止批量提交 | 不把攒了一周的杂项塞进一个 commit |

**提交信息格式：** `<阶段/范围>: <一句话摘要>` + 可选详细说明（为什么这么做，不是什么）。

### 标签规则

版本号 `v<主>.<次>.<修>`：主=架构变更（接口断裂）；次=新功能（单元/积木/验证规则）；修=修复（bug/文档/重构）。

### 纳入与排除策略

**纳入 Git：**

| 路径 | 内容 | 理由 |
|:--|:--|:--|
| `Bricks/` | 积木文本资产（含校验尾） | **真相源**——翻译器产物完备的素材 |
| `Mau.Snapshots/expected/` | 黄金文件（含 SHA256 校验尾） | 确定性校验基准 |
| `Mau.Snapshots/cases/` + `checks/` | 门禁语料/全谱 | 语料即测试用例 |
| 全部源码 + `.mau` | 工程本体 | — |

**排除 Git（.gitignore）：**

| 路径 | 理由 |
|:--|:--|
| `bin/` `obj/` | 编译中间产物，可重建 |
| `Mau.Snapshots/generated-run/` | 运行产物，`mau gen` 随时再生 |
| `CatTemp/` | 缓存区——收工清理 |
| `public/` | 构筑产物，可重建 |

**变更检查清单：**
1. 翻译器/积木文本变更 → 跑 `mau test` + `mau check`
2. Bricks 文本变更 → **校验尾由工具更新（re-seal），不得手改校验尾行**——校验失败 = 门禁拒绝
3. 黄金文件对比失败 → 确认差异是预期行为 → 更新 `expected/` → 一并提交，提交信息说明原因
4. 门禁标记（MAU_CHECKS_OK）不得随代码提交——它是运行结果不是文件

### 远程库

| 项目 | 值 |
|:--|:--|
| 远程地址 | `git@gitee.com:wu_lisha/mau.git` |
| 本地路径 | `C:\Users\ASUS\Desktop\Work\Gitee\mau` |

---

## 快速开始

```bash
# 构筑
dotnet build Mau.sln

# 运行门禁
dotnet run --project Mau.Cli test

# 全谱自查
dotnet run --project Mau.Cli check

# 验证 .mau 文件（语法 + 静态验证）
dotnet run --project Mau.Cli verify <file.mau>

# 翻译 .mau 文件（生成 C# 源码——骨架 + BRIKGROUP 内嵌）
dotnet run --project Mau.Cli gen <file.mau>

# 构筑（单文件 / 组 mauproj）
dotnet run --project Mau.Cli build <file.mau|组.mauproj> -o <dir>

# 积木索引——查询 / 源码重建 / 一致性校验（V1-V9）
dotnet run --project Mau.Cli bricks list
dotnet run --project Mau.Cli bricks index --update
dotnet run --project Mau.Cli bricks index --verify

# 独立部署 + 发布后冒烟
dotnet run --project Mau.Cli publish -o <dir>

# 黄金文件校验尾更新（翻译器变更重建黄金后必跑）
dotnet run --project Mau.Cli checksum --update
```

> 完整命令见 `mau-usage.md`（CCBP 知识网络：`Data/CatCatBigParty/Project/Mau/mau-usage.md`）。

---

## 许可证

MIT License · 详见 [LICENSE](./LICENSE)
