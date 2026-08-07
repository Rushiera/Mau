# 指令机制积木 — CMD

> 类别码：CMD | ID 段：BRIK-CMD-### | 类别说明：指令总线机制积木——语料声明指令拓扑（注册 key / 消费邮件 / 投递指令），执行器由宿主注入

| ID | 名字 | 工程路径 | 状态 | 说明 |
|:--|:--|:--|:--|:--|
| BRIK-CMD-001 | cmd.register | Mau.Bricks.Standard/CmdBrick.cs | ✅ | 注册模块指令 key 列表（key 三段式 Category_Module_Name） |
| BRIK-CMD-002 | cmd.unregister | Mau.Bricks.Standard/CmdBrick.cs | ✅ | 注销模块并清理残留指令 |
| BRIK-CMD-003 | cmd.consume | Mau.Bricks.Standard/CmdBrick.cs | ✅ | 消费指令邮件——HasCommands=false=模板邮件（本轮无新指令） |
| BRIK-CMD-004 | cmd.set | Mau.Bricks.Standard/CmdBrick.cs | ✅ | 投递指令——int 与 text 双轨（text 空=不写文本） |
| BRIK-CMD-005 | cmd.clean | Mau.Bricks.Standard/CmdBrick.cs | ✅ | 清空模块残留指令 |
| BRIK-CMD-006 | cmd.is_key | Mau.Bricks.Standard/CmdBrick.cs | ✅ | 指令分派判断——指令文本等于目标 key（返回=判断结果，M9） |

---

_版本：v1.1 | 2026-08-08 | +BRIK-CMD-006 is_key（生命周期指令分派——CH4 P3.5 指令闭环）_

---

## 用法

宿主启动时注入总线实例（机制积木静态宿主桥）：

```csharp
CmdBrick.Configure(commandBus);
```

语料变迁动作按拓扑声明：

```
变迁 T_CheckCmd:
  前置: P_Idle
  动作: cmd.consume          // 参数: ownerId → hasCommands/cmdKeys/cmdValues/cmdTexts
  时限: 10帧
  后置: P_UserMessage / P_Idle   // hasCommands=true → 用户消息到达；false → 继续等待
  线程: main
  帧: 每帧
```

外部人机交互投递指令：`cmd.set key=chat_talk_msg value=1 text=你好`（key 必须已注册，未注册静默拒绝——CommandBus 语义）。

## 语义约定

- **模板邮件**：注册后未投递时，consume 返回 HasCommands=false——判断"有实际写入"而非"数组非空"（CmdKeys 永远非空=注册模板）
- **双池冻结**：Set 写 pending 池，宿主每帧 `BeginTickInput()` 冻结后才可消费——帧间生效
- **三段式 key**：`Category_Module_Name`（如 chat_talk_msg）——IsValidKey 校验，非法 key 注册/投递被静默拒绝

---

_版本：v1.0 | 2026-08-05 | 创建——CH4 P2.2 TalkCat 指令入口前置_
