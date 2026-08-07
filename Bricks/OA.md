# OA 机制积木 — OA

> 类别码：OA | ID 段：BRIK-OA-### | 类别说明：包装 Mau.Runtime 机制的拓扑动作积木——实例由宿主注入，语料只声明谁发布/谁认领/什么工具响应

| ID | 名字 | 工程路径 | 状态 | 说明 |
|:--|:--|:--|:--|:--|
| BRIK-OA-001 | oa.post | Mau.Bricks.Standard/OaBrick.cs | ✅ | 上架工单——返回 OfficeId |
| BRIK-OA-002 | oa.list | Mau.Bricks.Standard/OaBrick.cs | ✅ | 查 Open 单——大类 + 候选 OfficeName |
| BRIK-OA-003 | oa.claim | Mau.Bricks.Standard/OaBrick.cs | ✅ | 锁单——逐个尝试认领，返回锁成功名单 |
| BRIK-OA-004 | oa.complete | Mau.Bricks.Standard/OaBrick.cs | ✅ | 完成——写回执 → Closed |
| BRIK-OA-005 | oa.settle | Mau.Bricks.Standard/OaBrick.cs | ✅ | 结算——Work 退回 Open；已终结确认 |
| BRIK-OA-006 | oa.set_int | Mau.Bricks.Standard/OaBrick.cs | ✅ | 写请求载荷 int 值（本人/Open） |
| BRIK-OA-007 | oa.set_str | Mau.Bricks.Standard/OaBrick.cs | ✅ | 写请求载荷 str 值（本人/Open） |
| BRIK-OA-008 | oa.get_int | Mau.Bricks.Standard/OaBrick.cs | ✅ | 读请求载荷 int 值（执行方消费） |
| BRIK-OA-009 | oa.get_str | Mau.Bricks.Standard/OaBrick.cs | ✅ | 读请求载荷 str 值（执行方消费） |
| BRIK-OA-010 | oa.claim_one | Mau.Bricks.Standard/OaBrick.cs | ✅ | 单单认领——工具循环展开用 |
| BRIK-OA-011 | oa.is_closed | Mau.Bricks.Standard/OaBrick.cs | ✅ | 单状态判断——轮询用 |

---

_版本：v1.1 | 2026-08-06 | 语义治理：补 OA-006~011 双字典/单单/状态判断（v0.40 双字典 + v0.42 工具循环已注册，INDEX 同步）_
