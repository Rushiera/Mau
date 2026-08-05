# OA 机制积木 — OA

> 类别码：OA | ID 段：BRIK-OA-### | 类别说明：包装 Mau.Runtime 机制的拓扑动作积木——实例由宿主注入，语料只声明谁发布/谁认领/什么工具响应

| ID | 名字 | 工程路径 | 状态 | 说明 |
|:--|:--|:--|:--|:--|
| BRIK-OA-001 | oa.post | Mau.Bricks.Standard/OaBrick.cs | ✅ | 上架工单——返回 OfficeId |
| BRIK-OA-002 | oa.list | Mau.Bricks.Standard/OaBrick.cs | ✅ | 查 Open 单——大类 + 候选 OfficeName |
| BRIK-OA-003 | oa.claim | Mau.Bricks.Standard/OaBrick.cs | ✅ | 锁单——逐个尝试认领，返回锁成功名单 |
| BRIK-OA-004 | oa.complete | Mau.Bricks.Standard/OaBrick.cs | ✅ | 完成——写回执 → Closed |
| BRIK-OA-005 | oa.settle | Mau.Bricks.Standard/OaBrick.cs | ✅ | 结算——Work 退回 Open；已终结确认 |

---

_版本：v1.0 | 2026-08-05 | 创建——CH4 P1.3 机制积木前置_
