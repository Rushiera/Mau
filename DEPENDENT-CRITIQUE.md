# Mau 依赖者批判报告

> 版本：v1.0 | 日期：2026-08-08 | 作者：Home（CH4 项目 Coder 主人格——Mau 首个外部依赖者）
> 定位：CH4 三轮实际使用（M1/M2/M2b）后的批判性复盘——记录"用什么、在哪卡、为什么卡、怎么改"

---

## 一、使用背景

CH4（CatHome 4.0）是 Mau 的第一个外部依赖者——以 Mau 为逻辑基座的多智能体操作系统（三轴设计：CH2 功能对标 / CH3 架构模式 / Mau 逻辑基座）。

本报告覆盖三轮实际使用：

| 轮次 | 内容 | 版本 |
|:--|:--|:--|
| M1 | 基座下沉三件（ConfigStore/VersionInfo/CommandPump）+ 宿主机制（CatRegistry/CatProjectStore） | CH4 v0.36 / Mau v0.59 |
| M2 | 基座三分类 Dog/Pet（IDog/DogBase/IPet/PetBase）+ BRIK-DOG 8 积木 + 多猫协作演示 | CH4 v0.37 / Mau v0.60-0.61 |
| M2b | 工具循环 Dog-OA 化——ToolPoster 发单独立 + FileCat/ShellCat 工具 Cat + 工具链积木 5 连 | CH4 v0.38 / Mau v0.62-0.65 |

使用方式：
- 编译期引用 Mau.Runtime / Mau.Contracts（基座机制）
- .mau 语料声明行为（翻译器生成 FL_*.cs → 口袋编译 DLL）
- Bricks 文本库积木（101 个）经翻译器内嵌（BRIKGROUP）
- DataBox 程序级服务（Bind/Resolve：IOA/ICommandBus/FlowRunner）
- 门禁：ch4 test（构建 → L2 单元+自举 → L3 黄金对比 + SHA256 校验尾）

---

## 二、要解决的问题

CH4 通过 Mau 解决的核心问题：

1. **逻辑声明化**——OA 工单撮合/生命周期状态机/工具分发用 .mau 声明，翻译器确定性生成 C#（黄金文件见证）
2. **多 Cat-Dog-OA 工单体系**——CH2 功能对标的核心通信机制
3. **工具域拆分**——File/Text/Shell/Office/System/CSharp/Mau 六+一域，工具 Cat 从 OA 接单
4. **统一帧驱动**——Cat/Dog/Pet 三分类全部实现 IFlow，注册 FlowRunner 全局 Tick（不做 Tick 分发）
5. **确定性门禁**——语料 → 生成物 → 黄金对比 + SHA256 校验尾（篡改即拒绝）

---

## 三、如何使用 Mau（实际工作流）

```
写语料（.mau——对照 design-mau-syntax §七 骨架）
  → 构筑（ch4 build <组.mauproj>：翻译 + 口袋编译 + BRIKGROUP 内嵌 → FL_*.dll）
  → 加载（FlowHandle.Load → FlowRunner.RegisterFlow → 全局 Tick 驱动）
  → 门禁（ch4 test：构建 → L2 单元 → L3 黄金对比 10 份 + 校验尾）
  → 积木链（Bricks 文本库 101 个——Mau.Cli bricks index/reseal/test 管理）
  → 服务（DataBox.Bind<IOA>/<ICommandBus>/<FlowRunner>——积木经 DataBox 解析宿主服务）
```

典型语料形态（FileCat 工具 Cat——36 行）：

```
命题: P_Init 信号 / P_Idle 信号 / P_Claimed 信号 / P_ExecDone 信号 / P_Failed 事实
变迁 T_Poll: 前置 P_Idle ∨ P_ExecDone；动作 oa.claim_next_simple；后置 P_Claimed / P_Idle；帧: 每帧
变迁 T_Exec: 前置 P_Claimed；动作 tool.run_generic；后置 P_ExecDone / P_Failed
```

---

## 四、遇到的问题（按使用节点分类）

### 节点 1：语料编写期

| # | 问题 | 现象 | 根因分析 |
|:--|:--|:--|:--|
| P1 | 常量绑定不进信号 payload | T_Create 参数全常量（"x" → name）→ 生成 FireAsk() 无参；测试按 4 参调用抛 TargetParameterCountException | 信号 payload 由"引用它的变迁的裸参数"推导——常量绑定被翻译器视为硬编码不进 payload。语义正确但**无文档**，靠反编译生成物才定位 |
| P2 | 参数值逗号被当多值分隔 | `officeNames="file.read,file.write,..."` 被解析为多个端口 → E002"输出端口 file.read 与积木输出端口间" | 语料参数绑定把逗号当值分隔符（mauproj 有转义说明 `\,`，但 .mau 语料内无效）。最终改分号 + 积木侧 Split(',',';') 双分隔 |
| P3 | 跨变迁引用输出端口命名不透明 | T_PushTool 引用 T_ReadAll 的 session/callId/result → E015"绑定变量未定义" | 输出端口跨变迁可用（生成物落字段），但单输出端口名固定为 value（连续读取互相覆盖）；多输出需专用积木（collect_result 四键一次读）——**端口命名/引用规则无权威文档** |
| P4 | 轮询终态用事实 → 重复触发 | T_ReadAll 前置 P_Closed（事实单调）→ 首次读完 Dog 回收后二次触发 → collect_result 找不到 Dog → P_Failed 残留 | 事实（Fact）单调可见不消费；轮询终态应使用信号（Signal 消费即清除）。信号/事实/条件的**语义分层是 Mau 的核心心智模型但文档缺失** |

### 节点 2：积木调用期（翻译器契约）

| # | 问题 | 现象 | 根因分析 |
|:--|:--|:--|:--|
| P5 | OfficeData/Office[] 语料不可构造 | oa.complete 需要 result: OfficeData——语料无法构造 → E017 | **基座 API 未按"语料可构造性"设计**。为绕开补了 complete_simple（无回执）/ complete_str（单键）/ complete_result（双键）三个积木——API 设计被表达层绑架 |
| P6 | 数组端口不可构造 | oa.claim 需要 long[]、cmd.register 需要 string[]——语料内无法构造数组 | 数组参数只能经信号 payload（FireXxx）由宿主投递，或专用单元素积木（claim_one_simple/claim_next_simple） |
| P7 | 判断积木返回语义特殊 | llm.is_end/is_tool 返回 true → Ok 后置 / false → Error 后置——与"bool 返回=成功"直觉相反 | 判断积木（is_*）返回=判断结果而非查询成功（M9 教训）——语义正确但与其他积木不一致，初学易错 |

### 节点 3：构筑/门禁期

| # | 问题 | 现象 | 根因分析 |
|:--|:--|:--|:--|
| P8 | 黄金文件体量膨胀 | 3 个新语料 180 行 → 黄金 4040 行（90% 是模板段） | 黄金 = 生成物全量快照——Cube 初始化/GetStatus/属性模板占大头；改一个命题类型 → 全量重建 + 全量审查（真实变更可能只有 30 行） |
| P9 | reseal 伪变更 + checkout 恢复真修改 | reseal 重写全部积木（行尾）→ git 全 M（diff 空）→ 清理时 checkout 整个目录把**已提交积木的真修改一起恢复**（OA-016 Split 分号丢失）→ 运行时静默失败（语料用分号、积木只拆逗号） | reseal 行尾处理（autocrlf）与 checkout 粒度问题——伪变更清理必须精确到文件，真修改文件跳过 |
| P10 | BRIK 类别清单硬编码 | 新类别 DOG 不加入 CLI 清单 → index --update 漏扫（86 vs 94）+ reseal 漏更新（PENDING_RESEAL 残留） | ScanBrickHeaders 与 ResealAll 各有一份硬编码类别目录清单——工具链自身违反"目录即清单"原则 |

### 节点 4：运行时调试期（最大痛点）

| # | 问题 | 现象 | 根因分析 |
|:--|:--|:--|:--|
| P11 | 黑盒调试——无断点/变量窗口/单步 | 工具循环全链路失败（OA 单 Open 无人接）：诊断路径 = OA 快照 → 反射读生成物字段 → GetStatus 命题/变迁状态 → 逐层比对。四连排查（Split 丢失/JSON 双转义/回执载荷混读/事实重复触发）每轮 30-60 分钟 | 语料逻辑在生成物（C#）里执行，语义在 .mau——错误发生在"生成物行号 + 内嵌积木"的混沌层。Mau 无运行时状态可视化/断点/单步/数据流追踪 |
| P12 | 积木编译错误行号是生成物行号 | run_generic CS8632 报 233/241/246 行——与积木源码行号对不上（BRIKGROUP 内嵌后偏移） | 内嵌代码编译错误定位成本高——需要生成物↔源码行号映射 |
| P13 | 数据流断点排查靠考古 | 回执载荷混读（collect_result 从回执读 session/call_id——实际在请求载荷）——运行时无数据流视图 | 双字典载荷/回执的读写链在语料-积木-生成物三层分散——无链路追踪工具 |

### 节点 5：LLM 协作特有

| # | 问题 | 现象 | 根因分析 |
|:--|:--|:--|:--|
| P14 | 翻译器语义知识税高 | 常量绑定推导/端口命名/信号vs事实/判断积木/数组不可构造——每条都是踩坑换来（Learn.md 4 条 + history 多篇） | 无系统性行为文档（design-mau-syntax 有语法骨架但语义行为缺）——LLM 从零学 Mau 的隐含语义成本高于 C# 编译器兜底 |

---

## 五、问题分析（分类归因）

### 1. 表达层设计债（P5/P6）
基座 API（OfficeData/Office[]/数组）未按"语料可构造"约束设计——积木是翻译器入口，API 必须同时满足 CLR 调用与语料端口可构造。
**缺"端口可构造性审查"流程**——新积木立项时先问：语料能构造这个类型吗？

### 2. 语义文档缺失（P1/P3/P4/P7）
翻译器行为（payload 推导/端口绑定/信号vs事实/判断积木）有实现但无权威文档。
Learn 踩坑记录是事后补救——**行为文档应该与语法文档同步产出**。

### 3. 工具链工程债（P9/P10）
类别清单硬编码 + reseal 行尾问题——工具链自身违反自己定的"目录即清单"。
**CLI 清单改目录扫描** + reseal 行尾规范化是低成本高收益修复。

### 4. 调试基建缺失（P11/P12/P13）——最大短板
无运行时观测（命题/变迁可视化）、无生成物映射（行号/变量）、无数据流追踪。
**生产时间 70% 在排错**——调试体验 = 整体体验。

### 5. 黄金噪音（P8）
全量快照对比有效但模板噪音大——需要"差异聚焦"（忽略模板段/对比语义相关段）。

---

## 六、结论与改进建议（按优先级）

| 优先级 | 改进 | 说明 |
|:--|:--|:--|
| P0 | **调试基建**：运行时观测（命题/变迁实时可视化——RuntimeObserver 扩展）+ 生成物↔语料行号映射 | 最大痛点；排错时间 70% → 目标 30% |
| P0 | 翻译器语义行为文档化（payload 推导/端口命名/信号vs事实/判断积木）——写入 design-mau-syntax | 知识税一次性清账 |
| P1 | 积木 API 设计加"语料可构造性"前置审查（端口类型约束） | 消灭 E017 家族 |
| P1 | CLI 类别清单改目录即清单（扫描 Bricks/ 子目录） | 消除 P10 类漏扫 |
| P2 | reseal 行尾与 checkout 流程规范化（Learn 判例固化） | 消除 P9 类静默失败 |
| P2 | 黄金对比差异聚焦（模板段忽略） | 降低黄金噪音 |

---

## 七、总体评价

**架构方向正确，当前处于"还债期"。**

- ✅ 分层正确：机制 C#（基座 59+ 项测试）/ 语义语料（180 行声明）/ 能力积木（101 个文本库）
- ✅ 积木乐高哲学有效：缺什么补什么（E017 → complete_simple → complete_str → complete_result 演化链），机制一次写好永远复用
- ✅ 黄金文件验证了确定性：文本级回归（不需要 mock，生成物就是基准）
- ❌ 当前主要瓶颈：**调试体验（黑盒）+ 端口可构造性（表达层绑架 API）**——不是"语法不够表达力"，是工程配套没跟上

**与直出 C# 的对比：**
- 单次实现：C# 更快更稳（这轮 M2b 直出估计 600-900 行，1/4 工作量，调试 10 分钟级）
- 演化自由：Mau 更优（工具域扩展 = 新语料 + 新积木，不动机制；C# 会重蹈 CH2 单体化覆辙）
- 代价：当前 Mau 的调试痛苦是"结晶优于连接"的真实价格——等调试基建跟上（P0），交换才完全划算

**给 Mau 的一句话：** 你的语法已经证明了自己；现在要还的是"让使用者看得见"的债。

---

_版本：v1.0 | 2026-08-08 | 初稿——CH4 M1/M2/M2b 三轮使用批判复盘_
