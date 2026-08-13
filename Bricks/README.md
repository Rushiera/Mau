# Mau 积木百科全书 — Bricks

> 版本：v3.0 | 更新：2026-08-08（PACK v2——外部包接口隔离，普通积木零包声明）
> 定位：Mau 积木的**文本资产库**——125 个 BRIK 单文件（复制即单包），翻译器内嵌进生成物。
> 积木是 Mau 生态的"包"。CH4 所需积木 100+，从第一天建立统一登记。

---

## 一、积木 ID 规范

**格式：`BRIK-{类别}-{三位序号}`**

| 段 | 规则 |
|:--|:--|
| `BRIK` | 固定前缀——Mau Brick 命名空间 |
| `{类别}` | 大写类别码：FILE / MATH / DATA / TEXT / SHELL / LLM / APPROVAL / OFFICE / LOG / CMD / OA / DOG / TOOL / TEST / SYSTEM / CSHARP / MAU / PACK |
| `{三位序号}` | 001 起顺序分配，**永不重用**（废弃积木保留 ID 标记 Deprecated） |

**编号铁律（🔴）：**
1. **编号唯一**——全类别空间内一个积木一个 ID
2. **顺序延伸**——新积木从当前类别最大序号 +1 分配，中间不留空（除非废弃保留）
3. **不可再赋值**——已使用的编号永不分配给其他积木
4. **只修改或废弃**——积木变更 → 修改内容保留 ID；积木移除 → 标记 `[Deprecated]` 保留 ID
5. **一积木一文件**——文件名 `BRIK-{ID}_{name}.cs`，文件头 `ID:` 与文件名一致（V1 校验）

**示例：** `BRIK-FILE-001` = `FILE/BRIK-FILE-001_file.convert.cs` = file.convert

---

## 二、文件头规范（十字段 + 校验尾）

每个积木 .cs 文件头部必须包含以下注释块（`--update` 扫描的真相源）：

```csharp
// ═══════════════════════════════════════════════════
// 积木: file.read
// ID:   BRIK-FILE-002
// 类别: FILE
// 作用: 读取 UTF-8 文本（受控路径）
// 依赖: 无
// 包: 无                                  ← PACK 类声明包；普通积木零包（V10 违规）
// 引用: System
// 原理: 经 FileBridge 受控文件系统 ReadText——白名单边界内置
// 常用: CH4 IO 工具组 / 任意文件读取场景
// 时长: Sync            ← 缺省 Sync（Streaming/Async 时声明）
// 线程: main            ← 缺省 main（worker 时声明）
// ═══════════════════════════════════════════════════
```

文件末尾 `// #MAU_CHECKSUM:SHA256:{64位}`——内容哈希校验尾，**修改文件必须重算**（`mau bricks index --update` 不自动重算文件校验尾）。

**字段含义：**

| 字段 | 内容 |
|:--|:--|
| `积木:` | Mau 调用名（`类别.函数`，与 BrickContract.Name 一致） |
| `ID:` | 唯一 ID（BRIK-{类别}-{序号}） |
| `类别:` | 类别码——**权威**（docx.* 属 OFFICE，不按名前缀推断） |
| `作用:` | 一句话职责 |
| `依赖:` | 依赖声明——L1 基座能力 / L2 积木 ID（BRIK-xxx）/ L3 外部库，逗号分隔；无则写"无" |
| `包:` | **PACK 类积木的外部包声明**——`包名@版本`，多个分号分隔；**普通积木必须写"无"**（V10 非 PACK 类声明包=违规——纯净性铁律）；本地程序集（Mau.Runtime 等）写 `Mau.Runtime@local` 不校验 |
| `引用:` | 依赖链——引用了哪些库/服务 |
| `原理:` | 核心机制——怎么做（≤一行） |
| `常用:` | 典型使用场景 |
| `时长:` | 契约元数据——Sync/Async/Streaming，缺省 Sync |
| `线程:` | 契约元数据——main/worker，缺省 main |

---

## 三、索引机制（唯一寻路）

**Bricks 是唯一真相源——文件头 + 静态方法签名 = 契约。**

```
Bricks/{类别}/*.cs（文件头十字段 + 静态方法签名）
  → mau bricks index --update（Roslyn 提取 inputs/outputs/return + 文件头时长/线程）
  → index.json v3（机器索引——翻译器构筑期唯一入口）
  → INDEX.md（人类可读五维表）
```

| 命令 | 作用 |
|:--|:--|
| `mau bricks list` | 注册表实时枚举（BrickIndex 查询） |
| `mau bricks index --update` | **从源码重建 index.json + INDEX.md**（契约提取） |
| `mau bricks index --verify` | V1-V9 一致性校验（含 V4 名称唯一 / V7 依赖一致 / V8 文件头完整） |
| `mau bricks index --check-license` | 文件头注释块完整性（ID/依赖/重名） |
| `mau bricks test` | 全局跑测（枚举→语料→编译→ALC→Fire/Tick→断言） |

**寻路规则：** 翻译器 / bricks 命令 / check 积木谱全部经 `BrickIndex`（index.json 查询器）——不存在第二条积木寻路。

---

## 四、登记流程（新积木四步骤）

1. **写代码** — `Bricks/{类别}/BRIK-{类别}-{序号}_{name}.cs`：文件头十字段 + 静态方法（bool 返回，out 参数 = 输出端口）
2. **写校验尾** — 计算文件 SHA256 追加 `// #MAU_CHECKSUM:SHA256:{hash}`（可参照现有文件格式）
3. **重建索引** — `mau bricks index --update`（自动生成 index.json + INDEX.md，新积木自动登记）
4. **验证** — `mau bricks index --verify` + `mau check`（积木谱 125/125 全过）

**删除流程：** 标记 `🗑️ Deprecated` + 保留 ID + 在 INDEX 注明废弃原因。代码可删，登记保留（ID 不释放）。

---

## 五、契约提取规则（--update 内部）

- **契约方法选择**：首个 `public static` 且返回 `bool` 的方法（辅助方法如 Configure/Classify 自动跳过）；无 bool 时回退首个 public static void（排除 Configure 前缀）
- **inputs**：非 out 参数（名 + C# 类型 → 契约类型）
- **outputs**：out 参数
- **return**：bool → Bool / void → Void
- **duration / thread**：文件头 `时长:` / `线程:` 字段（缺省 Sync / main）

---

## 七、外部包接入协议（PACK——v2 定稿）

> 目标：Mau 生态引入 .NET 开源包时**不污染 BRIK 库**——外部包能力经 PACK 类 BRIK（接口）隔离，普通积木零包声明。长期方向：开源包源码积木化（引用包利好人类，源码积木利好 AI——Mau 第一读者是 AI）。

### 7.1 包归属判定（v2 修订）

| 判定 | 落层 | 例子 |
|:--|:--|:--|
| Q1 运行时机制（任何软件都需要）？ | Mau.Runtime（零 NuGet 铁律） | OA/Command/DataBox 已是 |
| Q2 开发工具（编译/语法分析）？ | Mau.Development（Mau 编译链必要内部支持） | Roslyn（MauPocketCompiler/MauRoslynBridge） |
| Q3 领域能力（Excel/PDF/图像）？ | **独立程序集**（Mau.WorkApp 等增量包——有无不影响 Mau 本体） | ClosedXML / DocumentFormat.OpenXml |
| Q4 重型有状态（需宿主交互）？ | Runtime PACK 接口 + 实现程序集 + DataBox 服务 | ICSharpBridge / IExcelBridge / IWordBridge |

### 7.2 PACK 类 BRIK（v2 核心——取代 v1 的"普通积木直接引用包"）

- **类别 `PACK`**——接口积木（`excel.bridge` / `word.bridge` / `csharp.bridge`），只声明能力，不含实现
- **单方法调度**：`bool Invoke(string method, string argsJson, out string result)`——PACK 包低频调用接受 JSON 损失，统一包管理；不同包体制风格差异被单方法吸收
- **方法白名单**：文件头 `方法:` 字段声明（`excel.read → path,sheet,format`）——V11 门禁校验调用方
- **实现隔离**：接口在 Mau.Runtime（零依赖），实现在独立程序集（Mau.WorkApp/Mau.Development），宿主选装 DataBox.Bind
- **普通积木零包声明**：非 PACK 类声明任何包 = V10 违规（纯净性铁律——移植性关键是包体纯净，不在协议打补丁）

### 7.3 接入流程（新包五步）

1. **判定** — 按 §7.1 定落层（领域能力 → 独立程序集）
2. **接口** — Mau.Runtime 加 `IXxxBridge : IPackBridge`（零依赖）
3. **实现** — 独立程序集实现接口（自包含包逻辑）
4. **桥积木** — `Bricks/PACK/BRIK-PACK-xxx_name.bridge.cs`（`方法:` schema + DataBox 调度薄壳）
5. **消费积木** — 普通积木改调 PACK 桥（`DataBox.TryResolve<IXxxBridge>() → Invoke`），零包声明

### 7.4 门禁

- **V10**：非 PACK 类声明 `包:` = 违规；PACK 类声明包与实现程序集 csproj 一致
- **V11**：调用方积木 method+参数 vs PACK 桥 `方法:` schema 漂移 = FAIL（v0.73 已落地）
- **依赖者**：宿主只需引实现程序集（Mau.WorkApp/Mau.Development），不再背 NuGet 包

### 7.5 长期方向：源码积木化

高频开源包功能 → 精简移植为自包含源码积木（算法路径提取，非全量复制）——零版本漂移、AI 可读可改、许可证保留声明。PACK 接口是过渡期兜底。

---

## 八、类别目录

| 目录 | 内容 | 数量 |
|:--|:--|:--:|
| `FILE/` | 文件类积木 | 11 |
| `MATH/` | 数学类积木 | 4 |
| `DATA/` | 数据类积木（box） | 6 |
| `TEXT/` | 文本类积木 | 1 |
| `SHELL/` | Shell 类积木（超时/进程树） | 1 |
| `LOG/` | 日志类积木 | 4 |
| `CMD/` | 指令机制积木 | 9 |
| `OA/` | OA 机制积木（双字典） | 15 |
| `TEST/` | 测试探针积木 | 2 |
| `SYSTEM/` | 系统信息积木 | 3 |
| `AUDIT/` | 审计读取积木 | 3 |
| `PACK/` | 外部包接口积木（excel/word/csharp 桥） | 3 |

> 纯化说明（2026-08-13 T3）：LLM/TOOL/NOTE/CAT/UI/DOG/OFFICE/WIN/APPROVAL/CSHARP/MAU 十一类迁至 CH4 仓库 CH4.Bricks/（产品积木机制——design-mau-boundary.md §五）。
> 数量权威 = index.json（`mau bricks index --update` 机器重建）；本表为展示页，漂移时以 index.json 为准。

---

_版本：v2.1 | 2026-08-13 | T3 积木库纯化——十一类迁 CH4.Bricks（产品积木机制），类别目录收敛为基座十二类；v2.0 | 2026-08-07 | 重写：R1 文本库形态（一积木一文件 + 十字段 + 校验尾）+ 源码驱动索引（--update 契约提取）+ V1-V9 校验 + 登记四步骤_
