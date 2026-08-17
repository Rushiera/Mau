# Mau

> 埃及语"猫"（mau/mjw）。古埃及太阳神 Ra 亦被称为"伟大的猫 Mau"。
> 仓库历史一直在——Mau 这个名字是象征。

---

## 定位

**Mau = CH4 自举工具箱——我的私人方言。** 不考虑市场，不考虑通用性，只服务 CH4 一个消费者。

三位一体：

| 位 | 定义 |
|:--|:--|
| **私人方言** | 行为表达格式——语法为 LLM 输出稳定性设计（符号化 + 白名单词法），用确定性验证填补 LLM 的逻辑盲区 |
| **自检仪** | 分析能力 = 门禁的一部分——LLM 负责表达，确定性代码负责验真（分析失败 = 拒绝生成） |
| **工具箱** | 构筑/验证/观测工具链 + 积木原子能力——自举循环的把手 |

**现状（v3.0.2）：** 基座阶段完成——语言内核 v3、Runtime 纯化、观测支柱、分析门禁、积木最小化（跑测工具定位）、工具链收拢全部落地。CH4 介入模式待重新评估。

## 语言 v3 一句话

**FSM 网络是唯一语义，事件是唯一驱动，传感器是唯一感知，TokenId 是唯一内核词汇，外观表是我的私人拼写。**

| 单元 | 语义 |
|:--|:--|
| 状态机 `'S_X'` | 我在哪——互斥单值，事件沿转移，自环保持 |
| 传感器 `'P_X'` | 外界什么样——被动=触发器（事件沿），主动=采样（周期帧门控） |
| 导线 `'T_X'` | 什么响了→敲什么→去哪——可并联 [par]、可分叉 \|、可互联 |
| 槽 `'R_X'` | 能装多少——容量声明 + 观测快照 |

**TokenId 词法内核：** 翻译器只认编号，不认字符——字符是可替换的外观层（`ISymbolAppearance`）。黄金哈希对象 = TokenId 流——换外观不漂移，哨兵只对语义变更报警。

**编译链：** 词法 → 解析 → 验证（E2xx 引用 + E4xx 积木门禁）→ 分析（E3xx 图论验证——失败拒绝生成）→ 生成（强类型积木直调 + 内嵌积木源）。

**积木现状（最小化）：** 4 件——probe.source / probe.sink / probe.sink_slow（跑测探针）+ csharp.bridge（Roslyn 桥）。积木定位 = 跑测工具（验证框架稳定与翻译器性能），完备性随 CH4 介入按需立项。

## 工程结构（sln 8 项目）

| 项目 | 职责 |
|:--|:--|
| Mau.Contracts | 合约层——TokenId 表 / ISymbolAppearance / 积木契约 |
| Mau.Runtime | 数字电路机制——Tick/Cube/Inbox/OA/CommandBus/DataBox/FlowALC/ThreadGuard |
| Mau.Translator | 五阶段流水线——词法/解析/验证/分析/生成 + BrickIndex |
| Mau.Development | Roslyn 工具——PocketCompiler / 积木契约提取 / 积木谱跑测 |
| Mau.Cli | 命令入口——指令编号化路由（CommandIds 匹配表） |
| Mau.Runtime.Tests | Runtime 机制测试 + 跑测薄封装 |
| Mau.Translator.Tests | 翻译器 46 测试 |
| Mau.Development.Tests | Pocket 编译器测试 |

## CLI 命令面

| 命令 | 用途 |
|:--|:--|
| `mau verify <file.mau>` | 全链编译（不产出） |
| `mau gen <file.mau> -o <dir>` | 全链编译 + C# 生成物 |
| `mau proj <组.mauproj> [-o <srcDir>] [--build] [--out <dllDir>]` | **统一构筑链**——组翻译落盘 public/src/<组>/（csproj+BRIKGROUP.cs+FL_*.cs）；--build 走 dotnet build → public/app/Flows/FL_<组>.dll |
| `mau build <组.mauproj>` | 统一链路由——转发 mau proj --build（Roslyn Emit 退役；单 .mau 提示建组） |
| `mau test [--update]` | 三段门禁——[1/3] L2 翻译器+Runtime 测试 [2/3] L3 黄金哈希 [3/3] L4 积木谱 |
| `mau check` | 语法谱 5/5 + 负例谱 4/4 + 关键路径报告 |
| `mau debug <file.mau> [--ticks N] [--step] [--pause-on S_X=Y]` | 四柱状态表 + 单步 + 状态断点 |
| `mau bricks list` | 积木索引枚举 |
| `mau bricks index --update` | 源码扫描（文件头 + Roslyn 签名）+ 校验尾重算 + 索引重建 |
| `mau bricks index --verify` | 索引与实际文件一致性校验 |

命令名是外观层——内部路由经 CommandIds 匹配表（命令名 → 编号 → switch）。

## 工程不变项

1. **基座零控制台依赖** — Runtime 库内部禁止 Console 输出——诊断与日志走可注入通道
2. **入口只是入口** — 入口唯一职责是引导。业务/构筑/观测逻辑全部在基座与工具中
3. **工具链全 C#** — 门禁/构筑/检查一律 C# 实现，禁止本机脚本承载工程逻辑

## 文档指引

| 文档 | 位置 |
|:--|:--|
| 语法 v3 设计（定稿） | CCBP `Project/Mau/design-mau-v3.md` |
| 项目档案（定位/焦土边界/基座判据） | CCBP `Project/Mau/project.md` |
| 重建路线图（P0-P7 + P7a） | CCBP `Project/Mau/todo.md` |
| 术语表 | CCBP `Project/Mau/glossary.md` |
| 规格兑现对照 | CCBP `Project/Mau/design-status.md` |
| 变更历史 | CCBP `Project/Mau/CHANGELOG.md` |
| v2 全量归档 | CCBP `L3/history/Mau/v2-final/`（14 文件） |
| v2 代码终版 | git tag `v2.1.2-final`（2026-08-13 封印） |

## Git 版本管理规范

- **主干开发**：master 唯一真相源，始终可编译可跑门禁；中大型改动走 `feature/` 分支
- **提交信息**：`vX.Y: 变更摘要`（≤30 字，动词开头）；一次提交一个主题
- **标签**：`v<主>.<次>.<修>`——主=架构变更，次=新功能，修=修复
- **远程**：`git@gitee.com:wu_lisha/mau.git`

---

## 许可证

MIT License · 详见 [LICENSE](./LICENSE)

---

_版本：v3.0.2 | 2026-08-14 | 基座阶段完成——README 重写为 v3 现状页（命令面/工程结构/积木最小化现状）；v3.0.0 重建期状态页见 git 历史 148bdeb 前_

## 部署架构（2026-08-17 统一构筑链）

**编译链：** `.mau → mauproj 打组 → public/src/<组>/（csproj + BRIKGROUP.cs + FL_*.cs）→ dotnet build → public/app/Flows/FL_<组>.dll`

**产物双区：**
- `public/`——CH4 部署区：`src/<组>/`（翻译中间产物，一组一文件夹）+ `app/`（CH4.exe publish 平铺 + `Flows/` 语料 dll）
- `Mau-public/`——Mau 基座部署区（dotnet publish 平铺 + `Flows/` 未来自举语料占位）

**引用源铁律：** FL csproj Reference `Mau-public/` dll（编译/运行时同源），不 ProjectReference 源码。改基座 → 先 publish Mau-public → 再 proj 编译 FL。

**Roslyn 定位：** 退出构筑主链，保留基座工具能力（debug/bricks/test——Mau.Development 库形态）。

**入口：** CH4.exe 无参启动 = 程序入口（FindRepoRoot 定位仓库根 → 默认 Flows/ 加载）。

> 规格权威：CCBP `Project/CH4/design-ch4-deploy.md`
