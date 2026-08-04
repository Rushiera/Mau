# Mau 语料模板库

> 定位：CH4 时代的 .mau 声明蓝图——换产品即换语料。每个模板对应一个 CH3/CH2 已验证机制的结构化声明。

## 模板清单

| 文件 | 机制 | 来源 | 积木状态 | CH4 阶段 |
|:--|:--|:--|:--|:--|
| `oa_flow.mau` | OA 工单撮合（Post→Claim→Complete→超时结算） | CH3 Core/CH_OA.cs | 🔴 待实现 | P3.1 |
| `cat_lifecycle.mau` | Cat 生命周期（发现→验证→初始化→启用→禁用→卸载） | CH3 ModState/LifeState | 🔴 待实现 | P3.2 |
| `tool_dispatch.mau` | 工具分发（请求→审批→执行→回收） | CH3 ToolProvider 体系 | 🔴 待实现 | P3.3 |
| `inbox_consume.mau` | Inbox 消费（后台写→主线程排空→批处理） | CH3 CH_MainThreadInbox | 🔴 待实现 | P3.4 |
| `talkcat_fsm.mau` | TalkCat 六态 FSM（空闲/思考/流式/工具/完成/失败） | CH3 CH_Cat_TalkCat | 🟡 部分（llm.chat/ctx 已注册，llm.stream 占位） | P3.5 |

## 使用规则

- **蓝图定位**：模板引用大量未注册积木——`mau verify` 对模板**预期失败**（积木不存在检查），这不是 bug。
- **启用条件**：CH4 实现对应积木（oa.*/cat.*/tool.*/inbox.*）后，模板即成为可用语料——从 `Mau.Corpus/` 复制到项目 `Mau.Snapshots/cases/` 入门禁。
- **修改规则**：模板随 CH4 实践回写——实践验证后的修改同步更新本库。
- **不入门禁**：Mau.Corpus/ 是设计蓝图，不参与 `mau test` 黄金文件对比。
