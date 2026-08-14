using System;
using System.Collections.Generic;

namespace Mau.Translator
{
    /// <summary>
    /// v3 解析结果——FSM 网络 IR（四柱声明集合）
    /// </summary>
    public sealed class MauDocV3
    {
        /// <summary>
        /// 状态机声明
        /// </summary>
        public List<StateMachineDefV3> StateMachines = new List<StateMachineDefV3>();

        /// <summary>
        /// 传感器声明（被动触发器 + 主动采样）
        /// </summary>
        public List<SensorDefV3> Sensors = new List<SensorDefV3>();

        /// <summary>
        /// 导线声明（信号线——条件 + 动作 → 结果）
        /// </summary>
        public List<WireDefV3> Wires = new List<WireDefV3>();

        /// <summary>
        /// 槽声明（约束与占用）
        /// </summary>
        public List<SlotDefV3> Slots = new List<SlotDefV3>();

        /// <summary>
        /// 解析诊断（E1xx）
        /// </summary>
        public List<MauDiagnostic> Diagnostics = new List<MauDiagnostic>();

        /// <summary>
        /// 解析是否通过
        /// </summary>
        public bool Success;
    }

    /// <summary>
    /// 状态机声明——互斥单值 FSM。首元素 = 初始状态；出度 0 = 终态（分析器图论判定）。
    /// </summary>
    public sealed class StateMachineDefV3
    {
        /// <summary>
        /// 状态机名——'S_Talk'
        /// </summary>
        public string Name = "";

        /// <summary>
        /// 状态集合——首元素为初始状态
        /// </summary>
        public List<string> States = new List<string>();

        /// <summary>
        /// 声明行号
        /// </summary>
        public int Line;
    }

    /// <summary>
    /// 传感器声明——探针（乱东西）→ 规范参数（输出）的适配器。
    /// 双形态：被动触发器（⇐ 事件沿——外部投递）/ 主动采样（↻ 周期采样——积木规范化）。
    /// </summary>
    public sealed class SensorDefV3
    {
        /// <summary>
        /// 传感器名——'P_X'
        /// </summary>
        public string Name = "";

        /// <summary>
        /// true=被动触发器（消费即清）；false=主动采样（每 EveryFrames 帧采样一次）
        /// </summary>
        public bool Passive;

        /// <summary>
        /// 主动采样周期帧数（0=每帧采样）
        /// </summary>
        public long EveryFrames;

        /// <summary>
        /// 主动采样积木名（被动为空）
        /// </summary>
        public string BrickName = "";

        /// <summary>
        /// 主动采样参数原文（被动为空）
        /// </summary>
        public List<string> BrickArgs = new List<string>();

        /// <summary>
        /// 探测捕获盒子 Key——壳探测积木 out 端口落盒（空=无捕获）
        /// </summary>
        public string CaptureTarget = "";

        /// <summary>
        /// 成功侧动作——探测返回 true 时执行的积木调用列表（汇总写）
        /// </summary>
        public List<SensorActionV3> TrueActions = new List<SensorActionV3>();

        /// <summary>
        /// 失败侧动作——探测返回 false 时执行的积木调用列表
        /// </summary>
        public List<SensorActionV3> FalseActions = new List<SensorActionV3>();

        /// <summary>
        /// 声明行号
        /// </summary>
        public int Line;
    }

    /// <summary>
    /// 导线声明——信号线：什么响了，什么就被敲一下。
    /// 独立声明：条件（事件沿检测 + 状态断言）+ 动作（积木调用）→ 结果（状态转移分叉）。
    /// </summary>
    public sealed class WireDefV3
    {
        /// <summary>
        /// 导线名——'T_Start'
        /// </summary>
        public string Name = "";

        /// <summary>
        /// 时限帧数（0=无时限）
        /// </summary>
        public long Timeout;

        /// <summary>
        /// 并联标记——动作后台执行（Task.Run → Inbox 回投）
        /// </summary>
        public bool Parallel;

        /// <summary>
        /// 汇合标记——后台结果回投主线程
        /// </summary>
        public bool Join;

        /// <summary>
        /// 日志标记——[!] 触发观测（零语义影响）
        /// </summary>
        public bool Logging;

        /// <summary>
        /// 条件列表——传感器事件沿（'P_X'）或状态断言（'S_X' = 'Y'），& 合取
        /// </summary>
        public List<ConditionV3> Conditions = new List<ConditionV3>();

        /// <summary>
        /// 动作积木名（纯转移导线为空）
        /// </summary>
        public string BrickName = "";

        /// <summary>
        /// 动作参数原文
        /// </summary>
        public List<string> BrickArgs = new List<string>();

        /// <summary>
        /// 捕获目标——动作积木首个 out 端口落盒 Key（@key=私有 / key=全局；空=无捕获）
        /// </summary>
        public string CaptureTarget = "";

        /// <summary>
        /// 结果列表——'S_X' = 'W' 状态转移，| 分叉顺序（首项=成功侧，次项=失败侧，多路=名称返回分发）
        /// </summary>
        public List<ResultV3> Results = new List<ResultV3>();

        /// <summary>
        /// 声明行号
        /// </summary>
        public int Line;
    }



    /// <summary>
    /// 导线条件——状态断言 / 传感器沿检测 / 盒子判真 三形态
    /// </summary>
    public sealed class ConditionV3
    {
        /// <summary>
        /// true=状态断言（'S_X' = 'Y'）；false=传感器沿（'P_X'）或盒子判真（@key）
        /// </summary>
        public bool IsStateAssert;

        /// <summary>
        /// 盒子判真标记——@key 引用（true=盒子 Key 判真，非沿）
        /// </summary>
        public bool IsBoxAssert;

        /// <summary>
        /// 盒子 Key（判真引用用；@key=私有 / key=全局）
        /// </summary>
        public string BoxName = "";

        /// <summary>
        /// 传感器名（沿检测用）
        /// </summary>
        public string SensorName = "";

        /// <summary>
        /// 状态机名（状态断言用）
        /// </summary>
        public string StateName = "";

        /// <summary>
        /// 状态值（状态断言用）
        /// </summary>
        public string StateValue = "";
    }

    /// <summary>
    /// 传感器动作——壳分支侧积木调用（成功侧/失败侧汇总写）
    /// </summary>
    public sealed class SensorActionV3
    {
        /// <summary>
        /// 积木名
        /// </summary>
        public string BrickName = "";

        /// <summary>
        /// 参数原文
        /// </summary>
        public List<string> BrickArgs = new List<string>();
    }

    /// <summary>
    /// 导线结果——状态转移目标
    /// </summary>
    public sealed class ResultV3
    {
        /// <summary>
        /// 状态机名
        /// </summary>
        public string StateName = "";

        /// <summary>
        /// 状态值
        /// </summary>
        public string StateValue = "";
    }

    /// <summary>
    /// 槽声明——约束与占用：配额（容量上限）/ 独占（容量 1）/ 令牌（占位）。
    /// </summary>
    public sealed class SlotDefV3
    {
        /// <summary>
        /// 槽名——'R_Slot'
        /// </summary>
        public string Name = "";

        /// <summary>
        /// 容量（1=独占；初始值 = 容量——v2.0.7 判例）
        /// </summary>
        public long Capacity;

        /// <summary>
        /// 声明行号
        /// </summary>
        public int Line;
    }
}
