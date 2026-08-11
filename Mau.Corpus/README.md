# Mau 语料模板库

> 定位：CH4 时代的 .mau 声明蓝图——换产品即换语料。每个模板对应一个 CH3/CH2 已验证机制的结构化声明。
> 维护：2026-08-11 外部评审轮——全部模板对齐 CH4 现行积木契约重写，**全部通过 `mau verify`**；新增模板必须 verify 通过才能标注"已验证"。

## 模板清单

| 文件 | 机制 | 来源 | 状态 | CH4 对应 |
|:--|:--|:--|:--|:--|
| `oa_flow.mau` | OA 工单撮合（Post→Claim→Complete→超时结算） | CH3 Core/CH_OA.cs + CH4 P3.1 落地 | ✅ verify 通过（BRIK-OA-001~005,006~009,012 + log.write） | `CH4.Corpus/Demo/CH4_Cat_OaFlow.mau` |
| `cat_lifecycle.mau` | Cat 生命周期（指令驱动：注册→发现→验证→启用→禁用→卸载） | CH3 ModState/LifeState + CH4 P3.2/P3.5 落地 | ✅ verify 通过（cat.scan_instances + cmd.register/consume/is_key + log.write） | `CH4.Corpus/Demo/CH4_Cat_Lifecycle.mau` |
| `talkcat_fsm.mau` | TalkCat 六态 FSM（空闲/思考/流式/工具/完成/失败——精简版） | CH3 CH_Cat_TalkCat + CH4 P2.2 落地 | ✅ verify 通过（llm.ctx_* + completions/read_chunk/is_end/is_tool/has_error/finish + cat.tools_json + data.box_*） | `CH4.Corpus/CH4_Cat_TalkCat/CH4_Cat_Talk.mau`（完整版 70+ 变迁） |
| `tool_dispatch.mau` | 工具分发（请求→审批→执行→结果回收） | CH3 ToolProvider + CH4 M2b 工具链路 | ✅ verify 通过（approval.request/resolve/reject + tool.exec + log.write） | `CH4.Corpus/CH4_Cat_ToolPoster/CH4_Cat_ToolPoster.mau`（Dog 载体发单完整链路） |
| ~~inbox_consume.mau~~ | ~~Inbox 消费~~ | CH3 CH_MainThreadInbox | 🗑️ 已删除（2026-08-11）——Inbox 是基座原生机制（Mau.Runtime），**永不 Mau 化**（基座铁律） | 无——基座内实现 |

## 使用规则

- **蓝图定位**：模板是 .mau 声明蓝图——CH4 实践验证后的修改同步回写本库（本轮已完成对齐）。
- **可用性**：全部模板 `mau verify` 通过（静态验证——积木存在/端口绑定/无界环）。**运行**需要宿主桥（OA/CommandBus/LLM/Tool 等 DataBox.Bind）——见对应 CH4 语料接入。
- **启用条件**：从 `Mau.Corpus/` 复制到项目语料目录即可使用。
- **门禁**：Corpus 全部模板纳入 `mau check` 验证段（目录即清单）——模板漂移会阻断门禁（2026-08-11 外部评审轮修复：此前 0/5 verify 通过且不入门禁）。
