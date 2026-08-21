# Mau —— 双区块仓库

> 埃及语"猫"（mau/mjw）。古埃及太阳神 Ra 亦被称为"伟大的猫 Mau"。
> 仓库历史一直在——Mau 这个名字是象征。

---

## 一、理想目标

**我们在造一个会自己长大的系统：AI 用自己的语言，写自己的宿主、自己的工具、自己的家。**

不是"让 AI 帮人写代码"——是"让 AI 从零搭建一套自己能持续进化的运行时"：

```
LLM 写 .mau 语料 → 门禁验真（词法/解析/验证/图论分析——失败拒绝生成）
  → 翻译 C# → 编译 dll → 热重载加载（宿主不重启）→ 观测（帧号可溯）→ 迭代
```

**自举循环**：业务代码 100% 由语料表达，改语料重编译即生效——AI 自己闭环自己。

**哲学**：LLM 负责表达，确定性代码负责验真。用符号化语言（语法为 LLM 输出稳定性设计）+ 分析门禁（图论验证）填补 LLM 的逻辑盲区——表达自由，验证严格。

**现状（基座 v3.9.0 / 宿主 v0.33）**：自举循环已经真实运转——LLM 已能调用宿主工具（text.\* 文件操作 / mau.\* 语料自查 / cs.\* Roslyn 编码工具域 9 工具）完成"发现问题 → 修语料 → 门禁通过"的闭环。下一站：多猫并发。

## 二、仓库地图（双区块）

同一项目两个业务区块——**Mau 基座**（地基）+ **CH4 宿主**（自举业务），同仓推进、部署双区。

| 区块 | 定位 | 入口 | 文档档案 |
|:--|:--|:--|:--|
| **Mau 基座** | 私人方言语言 + 数字电路运行时 + 工具链 + 积木库 | `Mau.Cli`（`Mau.exe` 命令行） | CCBP `Project/Mau/` |
| **CH4 宿主** | 自举循环宿主——业务全语料化，热重载迭代 | `CH4.Entry`（宿主程序） | CCBP `Project/CH4/` |

## 三、定位

**Mau = CH4 自举工具箱——我的私人方言。** 不考虑市场，不考虑通用性，只服务 CH4 一个消费者。

三位一体：

| 位 | 定义 |
|:--|:--|
| **私人方言** | 行为表达格式——语法为 LLM 输出稳定性设计（符号化 + 白名单词法），用确定性验证填补 LLM 的逻辑盲区 |
| **自检仪** | 分析能力 = 门禁的一部分——LLM 负责表达，确定性代码负责验真（分析失败 = 拒绝生成） |
| **工具箱** | 构筑/验证/观测工具链 + 积木原子能力——自举循环的把手 |

**架构分层：**

| 层 | 形态 | 变更方式 |
|:--|:--|:--|
| **Runtime 层** | Mau.Runtime 固定 C#——数字电路机制（Tick/Cube/Inbox/OA/CommandBus/DataBox/FlowALC/ThreadGuard） | 编译期变更 |
| **业务层** | .mau 语料 → 翻译 C# → 编译 dll——宿主全部功能 | **热重载**（改语料重编译即生效，不重启宿主） |

## 四、语言 v3 一句话

**FSM 网络是唯一语义，事件是唯一驱动，传感器是唯一感知，TokenId 是唯一内核词汇，外观表是我的私人拼写。**

| 单元 | 语义 |
|:--|:--|
| 状态机 `'S_X'` | 我在哪——互斥单值，事件沿转移，自环保持 |
| 传感器 `'P_X'` | 外界什么样——被动=触发器（事件沿），主动=采样（周期帧门控） |
| 导线 `'T_X'` | 什么响了→敲什么→去哪——可并联 [par]、可分叉 \|、可互联 |
| 槽 `'R_X'` | 能装多少——容量声明 + 观测快照 |

**TokenId 词法内核：** 翻译器只认编号，不认字符——字符是可替换的外观层（`ISymbolAppearance`）。黄金哈希对象 = TokenId 流——换外观不漂移，哨兵只对语义变更报警。

**编译链：** 词法 → 解析 → 验证（E2xx 引用 + E4xx 积木门禁）→ 分析（E3xx 图论验证——失败拒绝生成）→ 生成（强类型积木直调 + 内嵌积木源）。

## 五、工程结构（sln 10 项目）

**基座（8）：**

| 项目 | 职责 |
|:--|:--|
| Mau.Contracts | 合约层——TokenId 表 / ISymbolAppearance / 积木契约 |
| Mau.Runtime | 数字电路机制——Tick/Cube/Inbox/OA/CommandBus/DataBox/FlowALC/ThreadGuard |
| Mau.Translator | 五阶段流水线——词法/解析/验证/分析/生成 + BrickIndex |
| Mau.Development | Roslyn 工具——MauRoslynBridge 编码工具桥 / PocketCompiler / 积木契约提取 / 积木谱跑测 |
| Mau.Cli | 命令入口——指令编号化路由（CommandIds 匹配表） |
| Mau.Runtime.Tests | Runtime 机制测试 + 跑测薄封装 |
| Mau.Translator.Tests | 翻译器测试 |
| Mau.Development.Tests | 编译器/桥测试 |

**宿主面（2）：**

| 项目 | 职责 |
|:--|:--|
| Mau.Providers | LLM 供应商——DeepSeekLlmRuntime（OpenAI 兼容流式接口） |
| CH4.Entry | 自举宿主——服务组装 + 语料加载 + LLM 桥 + 工具协调（OA 工单 + DevCat 20 线）+ 观测出口 + HTTP 外观层 |

## 六、积木现状（29 件 · 分组摘要）

积木 = 语料可调用的原子能力（文本资产 + 契约注册——不由程序集承载）。

| 组 | 件数 | 职责 |
|:--|:--|:--|
| PROBE | 3 | 跑测探针（source/sink/sink_slow）——框架稳定与翻译器性能验证 |
| DATA | 2 | 盒子数据面（全局/私有盒读写） |
| OA | 10 | 工单平台（Post/Claim/Complete/状态/载荷）——多消费者按能力认领 |
| TEXT | 4 | 文件文本操作（读/写/追加/替换） |
| MAU | 3 | 语料自查（verify/gen/proj——门禁与构筑） |
| LLM | 3 | 流式接口（stream/chunk_ready/done_ready） |
| LOG | 1 | 日志写入 |
| FILE | 1 | 文件读取 |
| PACK | 2 | **csharp.bridge**（Roslyn 编码工具域 9 工具：check/build/list/read/find_ref/patch/member/comment/dead）+ **config.bridge**（配置自改 4 工具：list/get/set/reset——schema 白名单写 + 值域校验 + 原子写回滚） |

完备性随 CH4 需求按四问判据（原子性·可构造性·消费面·归属）立项。

## 七、CLI 命令面

| 命令 | 用途 |
|:--|:--|
| `mau verify <file.mau>` | 全链编译（不产出） |
| `mau gen <file.mau> -o <dir>` | 全链编译 + C# 生成物 |
| `mau proj <组.mauproj> [-o <srcDir>] [--build] [--out <dllDir>]` | **统一构筑链**——组翻译落盘 public/src/<组>/；--build 走 dotnet build → public/app/Flows/FL_<组>.dll |
| `mau build <组.mauproj>` | 统一链路由——转发 mau proj --build（Roslyn Emit 退役） |
| `mau test [--update]` | 三段门禁——[1/3] L2 翻译器+Runtime [2/3] L3 黄金哈希 [3/3] L4 积木谱 |
| `mau check` | 语法谱 5/5 + 负例谱 10/10 + 关键路径报告 |
| `mau debug <file.mau> [--ticks N] [--step] [--pause-on S_X=Y]` | 四柱状态表 + 单步 + 状态断点 |
| `mau bricks list / index --update / index --verify` | 积木索引枚举 / 契约提取重算索引 / 索引一致性校验 |

命令名是外观层——内部路由经 CommandIds 匹配表（命令名 → 编号 → switch）。

## 八、工程不变项

1. **基座零控制台依赖** — Runtime 库内部禁止 Console 输出——诊断与日志走可注入通道
2. **入口只是入口** — 入口唯一职责是引导。业务/构筑/观测逻辑全部在基座与工具中
3. **工具链全 C#** — 门禁/构筑/检查一律 C# 实现，禁止本机脚本承载工程逻辑

## 九、文档指引

**Mau 基座：**

| 文档 | 位置 |
|:--|:--|
| 语法 v3 设计（定稿） | CCBP `Project/Mau/design-mau-v3.md` |
| 项目档案（定位/焦土边界/基座判据） | CCBP `Project/Mau/project.md` |
| 术语表 / 规格兑现对照 / 变更历史 | CCBP `Project/Mau/glossary.md` / design-status.md / CHANGELOG.md |
| v2 全量归档 | CCBP `L3/history/Mau/v2-final/`（14 文件） |
| v2 代码终版 | git tag `v2.1.2-final`（2026-08-13 封印） |

**CH4 宿主：**

| 文档 | 位置 |
|:--|:--|
| 项目档案（边界七拍板/基座完成判据） | CCBP `Project/CH4/project.md` |
| 六阶段路线（P4-P9 工程规格） | CCBP `Project/CH4/design-ch4-roadmap.md` |
| Roslyn 编码工具域规格 | CCBP `Project/CH4/design-ch4-cs.md` |
| 部署架构（统一构筑链/产物双区） | CCBP `Project/CH4/design-ch4-deploy.md` |
| 待办/变更历史/规格兑现 | CCBP `Project/CH4/todo.md` / CHANGELOG.md / design-status.md |

## 十、Git 版本管理规范

- **主干开发**：master 唯一真相源，始终可编译可跑门禁；中大型改动走 `feature/` 分支
- **提交信息**：`vX.Y: 变更摘要`（≤30 字，动词开头）；一次提交一个主题
- **双版本线**：基座 `v3.x` 与业务 `v0.xx` 同仓推进——commit message 以业务版本主导，基座版本在 CCBP CHANGELOG 同步
- **标签**：`v<主>.<次>.<修>`——主=架构变更，次=新功能，修=修复
- **远程**：`git@gitee.com:wu_lisha/mau.git`

## 十一、部署架构（统一构筑链）

**编译链：** `.mau → mauproj 打组 → public/src/<组>/（csproj + BRIKGROUP.cs + FL_*.cs）→ dotnet build → public/app/Flows/FL_<组>.dll`

**产物双区：**
- `public/`——CH4 部署区：`src/<组>/`（翻译中间产物）+ `app/`（宿主 publish 平铺 + `Flows/` 语料 dll）
- `Mau-public/`——Mau 基座部署区（dotnet publish 平铺 + `Flows/` 未来自举语料占位）

**引用源铁律：** FL csproj Reference `Mau-public/` dll（编译/运行时同源），不 ProjectReference 源码。改基座 → 先 publish Mau-public → 再 proj 编译 FL。

**Roslyn 定位：** 退出构筑主链，保留基座工具能力（debug/bricks/test + MauRoslynBridge 编码工具桥——Mau.Development 库形态）。

**入口：** CH4.exe 无参启动 = 程序入口（FindRepoRoot 定位仓库根 → 默认 Flows/ 加载）。

> 规格权威：CCBP `Project/CH4/design-ch4-deploy.md`

---

## 许可证

MIT License · 详见 [LICENSE](./LICENSE)

---

_版本：基座 v3.9.0 / 宿主 v0.33 | 2026-08-21 | 全量审查轮——版本对齐 + 积木 29 件同步 + CLI 统一链描述修正；CH4 五工具组全链（text.*/mau.*/host.reload/cs.* 9 件/config.* 4 件经 DevCat 20 认领线）_