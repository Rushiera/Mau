# P7 主线审计——Mau/CH4 单会话全链反证

> 版本：v1.0 | 审计日期：2026-08-18 | 审计人：Coder（CH4 P7 主线）
> 定位：框架健壮性反推——五线对照反证顶层框架是否存在逻辑漏洞或工程缺口；温故知新——回摘超越顶层设计的真实实现。
> 方法：CH3 Phase 6 审计模式轻量复用——静态纵览（设计承诺 vs 实现事实）+ 动态复核（观测数据复盘 + 门禁确认），不修外观细节。
> 基准：CCBP 哲学（结晶/勿增实体/数据全局性/命名即架构）· Mau 灵魂四条 · 语言铁律 V1-V6 · CH4 边界七拍板

---

## 一、审计线总览

| 线 | 审什么 | 范围 | 主检对象 |
|:--|:--|:--|:--|
| L1 语言哲学线 | V1-V6 兑现度 / 自检仪覆盖 / 黄金哨兵 | 抽审 | Translator 五段流水线 + 负例谱 |
| L2 机制线 | 数字电路自洽 / 线程契约 / 生命周期 / 错误路径 | 重点 | FlowRunner/OA/CommandBus/DataBox/FlowALC/ThreadGuard/Cube/Inbox |
| L3 宿主链路线 | 全链健壮性 / ChatBridge 定性 / 热重载 | 重点 | CH4.Entry（Program/Program.Chat/HttpHost） |
| L4 数据面线 | 盒子两档隔离 / 信号沿 / 全局盒残留 | 重点 | DataBox + 四语料盒子用法 + 快照截面 |
| L5 部署观测线 | 构筑链 / 双区 / 引用源 / 可见度 | 抽审 | deploy 实践 + LogStore/AuditStore/sys.* |

---

## 二、五线审计结果

### L1 语言哲学线（抽审）

| # | 设计承诺 | 实现事实 | 裁决 |
|:--|:--|:--|:--:|
| 1 | 白名单词法——99% 风险前置 | MauLexerV3 全量收集 E001-E004；四类词法元素严格白名单 | ✅ |
| 2 | 分析门禁四分析——分析失败拒绝生成 | MauAnalyzerV3：E300 无界环（DFS 三色）/ E301 不可达（BFS）/ E302 失败侧无恢复 / 关键路径报告 | ✅ |
| 3 | 验证层引用完整 | E200 重名 / E201-E203 引用 / E204 壳缺积木 / E206 Command key 唯一 | ✅ |
| 4 | 多路分叉拒绝 | E205 结果 >2 拒绝生成（名称返回积木体系待扩展） | ✅ |
| 5 | 翻译器只认 TokenId，字符是外观 | TokenIds 21 内核 + ISymbolAppearance 双向映射 + 黄金哈希（TokenId 流 SHA256） | ✅ |
| 6 | 语法面小、无隐藏行为 | 四柱由名前缀+结构推导，零单元关键字；白名单外字符词法层拒绝 | ✅ |
| — | 诊断定位精度 | 词法 E002 未闭合专有名词分支 col 计算自指（`i=j` 后 `col=col+(j-i)+1`）——诊断列号可能偏差，语义不影响 | 🟡 |

**结论：** 语言哲学承诺高兑现。"LLM 表达 · 代码验真"闭环真实运转（负例谱 10/10 正确拒绝 + 语法谱 5/5）。黄金哈希 + TokenId 编号追加约定（新增 token 一律追加末尾，既有编号不变 = 哈希零漂移）是设计之外的工程智慧——**上升候选 1**。

### L2 机制线（重点）

| # | 设计承诺 | 实现事实 | 裁决 |
|:--|:--|:--|:--:|
| 1 | 数字电路映射帧序 | FlowRunner.Tick 五段：指令冻结 → Inbox 排空（单回调 try-catch 隔离）→ 指令分发（try-catch 隔离）→ 实体帧驱动（FlowContext 注入/清理）→ 传感器壳 → OA 结算 | ✅ |
| 2 | 线程契约 | ThreadGuard 构造绑定 + AssertMainThread 全入口硬检查；Inbox ConcurrentQueue 跨线程；FlowContext ThreadStatic（par 后台不传播，身份随捕获载荷回投） | ✅ |
| 3 | OA 工单撮合 | Post/Claim/Complete/Relist/Cancel/超时结算全生命周期；RemoveByOwner/ReleaseByWorker 回收面（幽灵工单防泄漏） | ✅ |
| 4 | CommandBus 冻结消费 | 双缓冲池（pending→frozen）+ 原子注册事务（副本校验后提交）+ key 唯一索引 + Unregister 全清理 | ✅ |
| 5 | 热重载 | FlowALC collectible + LoadShared（FileShare.ReadWrite\|Delete 不锁文件）+ ExecuteReload 事务三段式（新 Load → 试跑验证 → 失败回滚旧 dll 全新实例 / 成功换句柄 + 旧 ALC TryUnload） | ✅ |
| 6 | 生命周期清理 | UnregisterFlow 三段：传感器协程移除 + CommandBus 注销 + DataBox.ClearScope（D1 闭环） | ✅ |
| 7 | 观测埋点 | AuditStore 注入式（FlowRunner/OA/CommandBus/DataBox 统一 record）+ LogStore C/O 类专属渠道 | ✅ |
| — | 功能隔离一致性 | FlowRunner.Tick 段3 flow.Tick 与段3b 传感器壳**无 try-catch**（仅 finally 清上下文）——异常将打穿帧循环（OA 结算/后续 Flow 全停）。生成物内导线动作自带 try-catch 兜底，实际风险低；但与段1/段2 的隔离承诺不一致 | 🟡 |
| — | CmdPump 懒注册窗口 | 生成物首 Tick 才注册 CommandBus key——由宿主 10 帧预热显式处理（魔法数字依赖宿主纪律，非机制保证） | 🟡 |

**结论：** 机制自洽性高，线程面/生命周期/错误路径全闭合。两条 🟡 属于"容错一致性"与"时序约定机制化"的边界补充——**修复候选 1、2**。

### L3 宿主链路线（重点）

| # | 设计承诺 | 实现事实 | 裁决 |
|:--|:--|:--|:--:|
| 1 | 业务代码 0 行 / 宿主只承载 agent 循环基建 | Bootstrap 组装四 Cat + 预热帧 + ConfigStore/SessionStore 恢复；业务逻辑全部在 corpus/ch4 四语料 | ✅ |
| 2 | 信号名即解析结果 | DispatchCommand 宿主字符串识别 → 分支 key 投递（ReadText/QuickCat 双 key 同帧/Chat）——语料面零值比较 | ✅ |
| 3 | 工具循环 ≤3 轮收敛 | ChatBridge：RunLlmInBackground（Task.Run + SSE 推送）→ WaitForLlm 帧上限 → DispatchToolCalls（Batch_Start + Exec/Skip 每线必投其一）→ CollectToolResults（按序回传 + 清盒 + 未知工具 ERR）→ 续轮 | ✅ |
| 4 | 工具失败回流可见 | 174301 实测：STREAM_ERROR\|ERR\|TRANSPORT → OA DONE → TOOL DONE——失败侧全链闭环（v1.14 教训兑现） | ✅ |
| 5 | 热重载忙时拒绝 | _toolBatchActive 标志——工具批次执行中拒绝 reload（防旧批次完成信号永不置位） | ✅ |
| 6 | 快照 ThreadGuard 契约 | HttpHost 后台置标志 → 主线程 PumpMainThread 构建缓存 → HTTP 线程只读（OA.GetSnapshot 仅主线程） | ✅ |
| 7 | 端口冲突不闪退 | Kestrel bind 失败分类提示 + 交互模式 ReadKey 暂停（无人值守不暂停） | ✅ |
| — | 工具写死两件 + 固定 prompt + MaxToolRounds=3 | P5 测试期形态——P8 自举工具组替换（已知路线，不算缺口） | 🟡 已知 |
| — | HandleChat 同步阻塞驱动 | 单会话 OK——P9 多猫并发需重构（已知路线） | 🟡 已知 |
| — | 宿主-语料握手协议 | llm_tool_done/llm_result_read/llm_result_ask 全局盒契约（宿主轮询 → 语料合流置位 → 宿主读走清盒）——设计中未承诺的新契约形态 | 💡 **上升候选 3** |

**结论：** 宿主链路线完整闭环。宿主-语料分工（C# 循环基建 / 语料工具执行）定性兑现；握手协议是实战长出的契约形态——**上升候选 3**。

### L4 数据面线（重点）

| # | 设计承诺 | 实现事实 | 裁决 |
|:--|:--|:--|:--:|
| 1 | 盒子两档 + 严格校验 | 私有 @key（翻译器拼 FlowId）/ 全局 key（B1 豁免）；E402 类型 / E407 读需写源 / E408 写源唯一 | ✅ |
| 2 | 信号沿原子 | RegisterSignal/Signal（CAS 0→1）/TryPeek（预检不消费）/TryPoll（CAS 消费）；未注册 fail fast；覆盖合并是特征（注释明示） | ✅ |
| 3 | 全局盒残留安全 | Set 覆盖 + 沿触发——旧值不自行触发导线；固定 key 集无无限增长 | ✅ |
| 4 | 清理原语 | ClearScope 原子（热重载/会话失效——F1 语法糖的运行时支撑已就位） | ✅ |
| 5 | 快照透明 | boxes 截面 + IsInternalBox 屏蔽内部实现盒 + SummarizeBoxValue ≤160 字符摘要（D5） | ✅ |
| — | 信号无队列（覆盖合并）| 同帧多脉冲合并为单沿——对"信号即事件"语义是设计特征；若未来需脉冲计数/队列语义需新原语（F1 触发条件之一） | 💡 观察 |

**结论：** 数据全局性哲学落地严整，无泄漏路径。"真实存在的东西都是全局的"在实现层（scope+key+信号沿）完全兑现。

### L5 部署观测线（抽审）

| # | 设计承诺 | 实现事实 | 裁决 |
|:--|:--|:--|:--:|
| 1 | 统一构筑链 | mau proj 组翻译三件套（csproj + BRIKGROUP.cs + FL_*.cs）→ dotnet build → app/Flows/；四 Cat mauproj 全就位 | ✅ |
| 2 | 产物双区 + 引用源铁律 | public/（src/ + app/）+ Mau-public/；FL csproj Reference HintPath 不 ProjectReference | ✅ |
| 3 | 日志缓冲写 | LogStore 常驻 StreamWriter + 1s flush + FileShare.ReadWrite（D5） | ✅ |
| 4 | 审计环形 + 落盘 | AuditStore 10000 条环形缓冲（查询唯一源）+ MD 落盘（人类/AI 阅读）+ sys.* 11 指令查询面 | ✅ |
| 5 | 数据根统一 | FindRepoRoot(AppContext.BaseDirectory) 仓库根优先——cwd 依赖消除 | ✅ |
| 6 | 可见度分级 L0-L3 | trace.sample 不落盘 / CHUNK 改结算行 / Kestrel 静默 / 内部盒屏蔽 / 短摘要——量级对拍 Log 28→5 行、Audit 356→24 事件 | ✅ |
| — | 审计窗口边界 | 活跃查询可回溯深度 = 最近 10000 事件（环形覆盖）；MD 落盘只读不查——长会话审计深度受限 | 🟡 |

**结论：** 部署观测闭环。"自举有眼睛"兑现；审计环形窗口是已知边界，P9 多猫并发前评估是否需要分段存储。

---

## 三、动态复核（观测数据复盘 + 门禁确认）

**观测证据链（帧号时序第一语言）：**
- `20260818_120303` 冒烟：F10 CMD.SET_TEXT ACCEPT ×2 → F11 OA.POST #1 → F12 OA.CLAIM worker=3 → F152 STREAM 结算（7 chunk = 1 行）——D5 结算行形态实测
- `20260817_174301` 失败路径：F427 STREAM_ERROR|ERR|TRANSPORT → F429 OA.DONE → F430 TOOL.DONE——失败可见性全链闭环
- 审计 MD（20260818.md）：app.start → flow.register ×4 → signal.post/consume → trace.fire/state 逐帧可回溯
- QuickCat 语料：POST→CLAIM→读双参→llm.stream→chunk 探测→complete 全生命周期帧号对齐

**门禁确认（2026-08-18 12:33）：**
- `mau test` → **MAU_CHECKS_OK**（L2 翻译器+运行时 / L3 黄金哈希 / L4 积木谱，18s）
- `mau check` → **MAU_CHECK_OK**（语法谱 5/5 + 负例谱 10/10 + 关键路径报告）

**说明：** 未实跑新会话（跑测纪律：宿主运行须前台 cmd 可见——审计会话无前台）。现有证据链覆盖成功/失败双路径；活体验证按需补跑。

---

## 四、温故知新——超越顶层设计的真实实现（上升候选）

| # | 实现 | 为什么够格 | 建议落点 | 状态 |
|:--|:--|:--|:--|:--:|
| 1 | **TokenId 追加末尾编号零漂移**——新 token 一律追加，既有编号不变 = 黄金哈希零漂移（CmdIn 判例） | 设计只承诺"换外观零漂移"；追加约定把"语义变更反证"也确定性化 | design-mau-v3 §3.3 | 🔵 待入档 |
| 2 | **信号即解析结果 / 分支拓扑**——Command Key 字符串识别放宿主，语料面多 ⇚ 传感器 = 分支粒度 | 语言哲学的模式化落地（"写进信号名的解析"） | Wisdom / design-mau-v3 | 🔵 Dream 已记，待正式化 |
| 3 | **宿主-语料握手协议**（llm_tool_done/llm_result_* 全局盒契约） | agent 循环基建与语料工具层之间的事实契约形态——设计中未承诺 | CH4 边界文档（design-ch4-*） | 🔵 待入档 |
| 4 | **热重载事务三段式**（新Load→试跑→回滚→换句柄） | 机制级工程模式——试跑验证 + 失败保留旧 + 回滚全新实例 | Mau exp / C# exp | 🔵 待入档 |
| 5 | **可见度分级 L0-L3**（trace 不落盘/结算行/缓冲写/静默/屏蔽/摘要） | 观测架构通用模式——量级对拍实证有效 | Mau exp 观测章节 | 🔵 待入档 |
| 6 | **数据根统一 + 引用源铁律**（部署跟随运行环境） | 部署哲学——稳定分支即运行基座 | deploy §八 已在案 | ✅ 已入档 |
| 7 | FlowContext ThreadStatic（对象 ID 运行时通道） | 语料面零感知 ID——机制完整性验证充分 | 已实现 | ✅ 已实现 |
| 8 | OA 存活校验（注入式 isLivingOwner/isLivingWorker） | 幽灵工单防泄漏 | 已实现 | ✅ 已实现 |
| 9 | 冻结快照迭代模式（Ids/Entries 快照） | 迭代安全通用模式 | C# exp §1.18 已沉淀 | ✅ 已入档 |

---

## 五、三段式结论

### 保留（设计承诺兑现——不动）
- 语言哲学四柱 + 白名单词法 + 四分析门禁 + 黄金哈希哨兵
- 数字电路机制（帧序五段 / 线程守卫 / 双缓冲 / 事务注册）
- 盒子数据面 + 信号沿（两档作用域 + 严格校验 + 原子消费）
- 统一构筑链 + 产物双区 + 引用源铁律
- 宿主-语料分工（C# agent 循环基建 / 语料工具执行）
- 可见度架构 L0-L3

### 扬弃（需修/需补——待莎拍板）
| # | 项 | 级别 | 处置建议 |
|:--|:--|:--|:--|
| 1 | FlowRunner.Tick 段3/段3b 异常隔离（与段1/2 对齐） | 🟡 容错一致性 | P7b 或独立小轮——try-catch 包 flow.Tick + 传感器壳，异常记 RuntimeLog 不打断帧 |
| 2 | CmdPump 懒注册预热帧魔法数字 | 🟡 时序约定 | 机制化（FlowRunner 注册后统一预热 N 帧 API）或文档化入 exp；低优先 |
| 3 | AuditStore 环形窗口 10000 | 🟡 观测边界 | P9 多猫并发前评估分段存储 |
| 4 | 词法 E002 col 计算自指 | 🟡 诊断精度 | 顺手修（一行）——并入 P7b |
| 5 | MaxToolRounds 写死 / Chat 同步阻塞 / 工具写死 | 🟡 已知路线 | P8/P9 替换——不在此修 |

### 新增（上升为顶层设计的候选——待莎拍板后入档）
- 候选 1-5（§四）——TokenId 零漂移 / 信号即解析 / 握手协议 / 热重载事务 / 可见度分级

---

## 六、五判据复核（P3d 遗产不倒退）

| 判据 | 复核结果 |
|:--|:--|
| 1 语料表达 | ✅ 四 Cat 业务 100% 语料；审计复核 CmdPump/导线链/盒子面——宿主零业务 |
| 2 热重载 | ✅ ExecuteReload 事务 + FlowALC 不锁文件（LoadShared）——设计承诺保持 |
| 3 门禁负例 | ✅ mau check 10/10 正确拒绝 + 语法谱 5/5 + mau test MAU_CHECKS_OK |
| 4 观测全链 | ✅ 日志/审计/快照/CLI 四出口 + 全局帧号可回溯——证据链见 §三 |
| 5 积木框架 | ✅ Bricks 16 件 + L4 积木谱全绿 |

---

## 七、审计结论

**无 🔴 框架级逻辑漏洞。** 顶层设计（语言哲学/数字电路/数据全局性/部署观测）在实现层全部兑现，未发现"设计承诺与实现事实"的框架级断裂。

**四条 🟡 工程缺口**（容错一致性 / 时序机制化 / 观测窗口 / 诊断精度）均为边界补充，不影响当前单会话自举循环运转。

**九项温故知新候选**——其中 6 项（TokenId 零漂移 / 信号即解析 / 握手协议 / 热重载事务 / 可见度分级 / 数据根）是实战长出的超越原设计的真实实现，建议上升为顶层设计正式条目。

---

> 后续动作：修复项与入档项等莎拍板；P7 主线审计核销同步 CCBP design-status.md。