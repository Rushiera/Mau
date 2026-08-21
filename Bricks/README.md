# Mau 积木库 — Bricks

> 定位：积木文本资产库——翻译器构筑期经 BrickIndex（index.json）唯一寻路。
> 现状：29 件 active（探针 3 + DATA 2 + OA 10 + TEXT 4 + MAU 3 + LLM 3 + LOG 1 + FILE 1 + PACK 2）——完备性随 CH4 需求按四问判据立项（原子性·可构造性·消费面·归属）。

## 现状清单

| ID | 名字 | 类别 | 用途 |
|:--|:--|:--|:--|
| BRIK-TEST-001 | probe.source | TEST | 固定输出源——数据流绑定验证 |
| BRIK-TEST-002 | probe.sink | TEST | 消费输入输出结果——强类型直调验证 |
| BRIK-TEST-003 | probe.sink_slow | TEST | 延迟探针——par Busy 门/超时语义压测 |
| BRIK-DATA-001/002 | data.box_set_str / box_get_str | DATA | 盒子数据面——全局/私有盒读写 |
| BRIK-OA-001~010 | oa.post / set_str / claim / complete / get / is_* / done_ready | OA | 工单平台——Post/Claim/Complete/状态/载荷 |
| BRIK-TEXT-001~004 | text.read / write / append / replace | TEXT | 文件文本操作（受控根边界） |
| BRIK-MAU-001~003 | mau.verify / gen / proj | MAU | 语料自查——门禁与构筑（统一链） |
| BRIK-LLM-002~004 | llm.stream / chunk_ready / done_ready | LLM | 流式接口薄壳（Runtime ILlmRuntime） |
| BRIK-LOG-001 | log.write | LOG | 日志写入 |
| BRIK-FILE-001 | file.read | FILE | 文件读取 |
| BRIK-PACK-003 | csharp.bridge | PACK | C# 工具桥——Roslyn 编码工具域 9 工具 |
| BRIK-PACK-004 | config.bridge | PACK | 配置自改桥——list/get/set/reset（schema 白名单写 + 值域校验 + 原子写回滚） |

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

_更新：2026-08-21 全量审查轮——现状清单 4→29 件同步（CH4 P8/P8.5d 立项全记录）；v2 积木百科（125 积木/PACK 协议/T3 纯化史）见 git 历史_