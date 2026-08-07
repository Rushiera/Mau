# Mau 积木百科全书 — Bricks

> 版本：v2.0 | 更新：2026-08-07（R1 文本库形态 + 源码驱动索引）
> 定位：Mau 积木的**文本资产库**——84 个 BRIK 单文件（复制即单包），翻译器内嵌进生成物。
> 积木是 Mau 生态的"包"。CH4 所需积木预计 100+，从第一天建立统一登记。

---

## 一、积木 ID 规范

**格式：`BRIK-{类别}-{三位序号}`**

| 段 | 规则 |
|:--|:--|
| `BRIK` | 固定前缀——Mau Brick 命名空间 |
| `{类别}` | 大写类别码：FILE / MATH / DATA / TEXT / SHELL / LLM / APPROVAL / OFFICE / LOG / CMD / OA / TOOL / TEST |
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
4. **验证** — `mau bricks index --verify` + `mau check`（积木谱 84/84 全过）

**删除流程：** 标记 `🗑️ Deprecated` + 保留 ID + 在 INDEX 注明废弃原因。代码可删，登记保留（ID 不释放）。

---

## 五、契约提取规则（--update 内部）

- **契约方法选择**：首个 `public static` 且返回 `bool` 的方法（辅助方法如 Configure/Classify 自动跳过）；无 bool 时回退首个 public static void（排除 Configure 前缀）
- **inputs**：非 out 参数（名 + C# 类型 → 契约类型）
- **outputs**：out 参数
- **return**：bool → Bool / void → Void
- **duration / thread**：文件头 `时长:` / `线程:` 字段（缺省 Sync / main）

---

## 六、类别目录

| 目录 | 内容 | 数量 |
|:--|:--|:--:|
| `FILE/` | 文件类积木 | 11 |
| `MATH/` | 数学类积木 | 3 |
| `DATA/` | 数据类积木（snapshot/box） | 6 |
| `TEXT/` | 文本类积木 | 1 |
| `SHELL/` | Shell 类积木（能力分类 + 审批制） | 1 |
| `LLM/` | LLM 类积木（chat/stream/ctx 族） | 21 |
| `APPROVAL/` | 审批类积木 | 4 |
| `OFFICE/` | Office 类积木（excel/docx） | 4 |
| `LOG/` | 日志类积木 | 4 |
| `CMD/` | 指令机制积木 | 5 |
| `OA/` | OA 机制积木（双字典） | 11 |
| `TOOL/` | 工具机制积木（分发/认领/适配器） | 11 |
| `TEST/` | 测试探针积木 | 2 |

---

_版本：v2.0 | 2026-08-07 | 重写：R1 文本库形态（一积木一文件 + 十字段 + 校验尾）+ 源码驱动索引（--update 契约提取）+ V1-V9 校验 + 登记四步骤_
