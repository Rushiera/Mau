# OA 机制积木 — OA

> 类别码：OA | ID 段：BRIK-OA-### | 类别说明：包装 Mau.Runtime 机制的拓扑动作积木——实例由宿主注入，语料只声明谁发布/谁认领/什么工具响应

| ID | 名字 | 工程路径 | 状态 | 说明 |
|:--|:--|:--|:--|:--|
| BRIK-OA-001 | oa.post | Bricks/OA/BRIK-OA-001_oa.post.cs | ✅ | 上架工单——返回 OfficeId |
| BRIK-OA-002 | oa.list | Bricks/OA/BRIK-OA-002_oa.list.cs | ✅ | 查 Open 单——大类 + 候选 OfficeName |
| BRIK-OA-003 | oa.claim | Bricks/OA/BRIK-OA-003_oa.claim.cs | ✅ | 锁单——逐个尝试认领，返回锁成功名单 |
| BRIK-OA-004 | oa.complete | Bricks/OA/BRIK-OA-004_oa.complete.cs | ✅ | 完成——写回执 → Closed |
| BRIK-OA-005 | oa.settle | Bricks/OA/BRIK-OA-005_oa.settle.cs | ✅ | 结算——Work 退回 Open；已终结确认 |
| BRIK-OA-006 | oa.set_int | Bricks/OA/BRIK-OA-006_oa.set_int.cs | ✅ | 写请求载荷 int 值（本人/Open） |
| BRIK-OA-007 | oa.set_str | Bricks/OA/BRIK-OA-007_oa.set_str.cs | ✅ | 写请求载荷 str 值（本人/Open） |
| BRIK-OA-008 | oa.get_int | Bricks/OA/BRIK-OA-008_oa.get_int.cs | ✅ | 读请求载荷 int 值（执行方消费） |
| BRIK-OA-009 | oa.get_str | Bricks/OA/BRIK-OA-009_oa.get_str.cs | ✅ | 读请求载荷 str 值（执行方消费） |
| BRIK-OA-010 | oa.claim_one | Bricks/OA/BRIK-OA-010_oa.claim_one.cs | ✅ | 单单认领——工具循环展开用 |
| BRIK-OA-011 | oa.is_closed | Bricks/OA/BRIK-OA-011_oa.is_closed.cs | ✅ | 单状态判断——轮询用 |
| BRIK-OA-012 | oa.complete_simple | Bricks/OA/BRIK-OA-012_oa.complete_simple.cs | ✅ | 无回执完成（空 OfficeData → Closed） |
| BRIK-OA-013 | oa.complete_str | Bricks/OA/BRIK-OA-013_oa.complete_str.cs | ✅ | 单键回执完成（result 单键） |
| BRIK-OA-014 | oa.claim_one_simple | Bricks/OA/BRIK-OA-014_oa.claim_one_simple.cs | ✅ | 单数认领（无输出端口） |
| BRIK-OA-015 | oa.complete_result | Bricks/OA/BRIK-OA-015_oa.complete_result.cs | ✅ | result/error 双键回执完成——工具执行结果标准形态 |
| BRIK-OA-016 | oa.claim_next_simple | Bricks/OA/BRIK-OA-016_oa.claim_next_simple.cs | ✅ | 原子接单（M2b 工具 Cat 接单循环） |

---

_版本：v1.1 | 2026-08-06 | 语义治理：补 OA-006~011 双字典/单单/状态判断（v0.40 双字典 + v0.42 工具循环已注册，INDEX 同步）_
