# Mau 积木库 — Bricks

> 定位：积木文本资产库——翻译器构筑期经 BrickIndex（index.json）唯一寻路。
> 现状（v3.0.2 最小化）：4 件积木——跑测工具定位（验证框架稳定与翻译器性能），完备性随 CH4 介入按需立项。

## 现状清单

| ID | 名字 | 类别 | 用途 |
|:--|:--|:--|:--|
| BRIK-TEST-001 | probe.source | TEST | 固定输出源——数据流绑定验证 |
| BRIK-TEST-002 | probe.sink | TEST | 消费输入输出结果——强类型直调验证 |
| BRIK-TEST-003 | probe.sink_slow | TEST | 延迟探针——∥ Busy 门/超时语义压测 |
| BRIK-PACK-003 | csharp.bridge | PACK | C# 工具桥——Roslyn 能力声明（PACK 类，实现 = Mau.Development.MauRoslynBridge） |

## 积木文件规范

**一积木一文件**，文件名 `BRIK-{类别}-{三位序号}_{名字}.cs`，ID 永不重用。

**文件头十字段**（索引提取的真相源）：

```csharp
// 积木: probe.sink
// ID:   BRIK-TEST-002
// 类别: TEST
// 作用: 消费输入并输出结果
// 依赖: 无
// 包: 无                                  ← PACK 类声明包；普通积木必须"无"
// 引用: System
// 原理: a/b 拼接为 result
// 常用: 翻译器数据流测试
// 时长: Sync            ← 缺省 Sync（Async 时声明）
// 线程: main            ← 缺省 main（worker 时声明）
```

文件末尾校验尾：`// #MAU_CHECKSUM:SHA256:{hex}`——修改文件后由 `mau bricks index --update` 自动重算。

## 索引机制（源码唯一真相源）

```
Bricks/{类别}/BRIK-*.cs（文件头十字段 + 静态方法签名）
  → mau bricks index --update（Roslyn 提取 inputs/outputs/return + 文件头时长/线程）
  → index.json（机器索引——BrickIndex 查询器构筑期唯一入口）
  → INDEX.md（人类可读登记表）
```

**契约提取规则：** 首个 `public static` 返回 `bool` 的方法（排除 Configure 前缀）；无 bool 回退首个 public static void。非 out 参数 = inputs，out 参数 = outputs。

## 命令

| 命令 | 用途 |
|:--|:--|
| `mau bricks list` | 索引枚举 |
| `mau bricks index --update` | 扫描 + 校验尾重算 + index.json/INDEX.md 重建 |
| `mau bricks index --verify` | 索引与实际文件一致性校验 |

## 消费链

- 验证门禁：`mau check` / 翻译器 Validator E4xx——积木名/参数数量/参数类别构筑期校验，未知积木拒绝生成
- 生成物：引用到的积木源码内嵌（复制即单包，剥 using 行）+ 强类型直调（编译期验型）
- 跑测：`mau test` [3/3] 段——BrickSpecRunner（brickflow 语料全链执行断言）

---

_版本：v3.0.2 | 2026-08-14 | 重写为 v3 最小版（4 件现状 + 文件规范 + 索引机制）；v2 积木百科（125 积木/PACK 协议/T3 纯化史）见 git 历史_
