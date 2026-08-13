using Xunit;

// BoxStore/AuditStore 是进程级静态——测试并行会互相污染（T4 压测台发现：Inventory/TaskPipeline 并行时 Clear 互相踩踏）
[assembly: CollectionBehavior(DisableTestParallelization = true)]
