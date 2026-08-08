# Mau 语料模板库

> 定位：CH4 时代的 .mau 声明蓝图——换产品即换语料。每个模板对应一个 CH3/CH2 已验证机制的结构化声明。

## 模板清单

| 文件 | 机制 | 来源 | 积木状态 | CH4 阶段 |
|:--|:--|:--|:--|:--|
| `oa_flow.mau` | OA 工单撮合（Post→Claim→Complete→超时结算） | CH3 Core/CH_OA.cs | ✅ 已注册（BRIK-OA-001~016） | P3.1 已用 |
| `cat_lifecycle.mau` | Cat 生命周期（发现→验证→初始化→启用→禁用→卸载） | CH3 ModState/LifeState | ✅ 已注册（cmd.* 指令族 + P3.5 生命周期闭环） | P3.5 已用 |
| `tool_dispatch.mau` | 工具分发（请求→审批→执行→回收） | CH3 ToolProvider 体系 | ✅ 已注册（BRIK-TOOL-001~013 + OA 载体） | M2b 已用 |
| `inbox_consume.mau` | Inbox 消费（后台写→主线程排空→批处理） | CH3 CH_MainThreadInbox | 🟡 基座机制已就位（Inbox 原生），模板按需微调 | P3.4 |
| `talkcat_fsm.mau` | TalkCat 六态 FSM（空闲/思考/流式/工具/完成/失败） | CH3 CH_Cat_TalkCat | ✅ 已注册（llm.* 21 + ctx.* 族 + tool.* 族） | P3.5 已用 |

## 使用规则

- **蓝图定位**：模板是 .mau 声明蓝图——CH4 实践验证后的修改同步回写本库。
- **可用性**：模板引用的积木大部分已注册（oa.*/tool.*/llm.*/cmd.* 全量）——`mau verify` 可直接验证；仍有未注册积木时 verify 会报「积木不存在」，属正常。
- **启用条件**：从 `Mau.Corpus/` 复制到项目语料目录即可使用。
- **不入门禁**：Mau.Corpus/ 是设计蓝图，不参与 `mau test` 黄金文件对比。
