using System;
using System.Collections.Generic;

namespace Mau.Translator
{
    /// <summary>
    /// 命题类型——由写入源推导（信号/条件/事实）
    /// </summary>
    public enum PropKindV2
    {
        /// <summary>未推导</summary>
        Unknown,
        /// <summary>信号——事件，触发即消费（⇐ 写入）</summary>
        Signal,
        /// <summary>条件——实测值，每帧刷新（测量写入）</summary>
        Condition,
        /// <summary>事实——记录，单调保持（结果侧注册）</summary>
        Fact,
    }

    /// <summary>
    /// 系统边界方向
    /// </summary>
    public enum BoundaryDirV2
    {
        /// <summary>外部输入（信号 Fire / OA 接收）</summary>
        In,
        /// <summary>外部输出（OA 发送）</summary>
        Out,
    }

    /// <summary>
    /// 属性标记——控制律/测量修饰
    /// </summary>
    public sealed class LawAttrsV2
    {
        /// <summary>时限 τ（null = ∞ 无限）</summary>
        public int? Timeout;

        /// <summary>帧/采样周期 ω（null = 未声明）</summary>
        public int? PollFrame;

        /// <summary>worker 线程 ∥</summary>
        public bool IsWorker;

        /// <summary>inbox 汇合 ⋈</summary>
        public bool IsJoin;

        /// <summary>日志标记 !（自动生成结构化日志）</summary>
        public bool IsLogging;
    }

    /// <summary>
    /// 条件种类——控制律前置
    /// </summary>
    public enum CondKindV2
    {
        /// <summary>状态断言——'S_X' = 'Y'</summary>
        StateEquals,
        /// <summary>命题引用——'P_X'（信号触发/条件/事实检查）</summary>
        PropRef,
        /// <summary>资源引用——'R_X'（获取，动作完成后释放）</summary>
        ResourceRef,
        /// <summary>合取——∧ 全部成立</summary>
        And,
        /// <summary>析取——∨ 任一成立</summary>
        Or,
    }

    /// <summary>
    /// 条件项——控制律前置条件树
    /// </summary>
    public sealed class CondV2
    {
        /// <summary>种类</summary>
        public CondKindV2 Kind;

        /// <summary>状态断言——状态机名</summary>
        public string? MachineName;

        /// <summary>状态断言——状态名</summary>
        public string? StateName;

        /// <summary>命题/资源引用名</summary>
        public string? RefName;

        /// <summary>And/Or 子项</summary>
        public List<CondV2>? Items;
    }

    /// <summary>
    /// 结果种类——控制律结果侧
    /// </summary>
    public enum ResultKindV2
    {
        /// <summary>状态转移——'S_X' = 'Y'</summary>
        StateTransfer,
        /// <summary>命题注册——'P_X'</summary>
        PropRegister,
    }

    /// <summary>
    /// 结果项——| 分隔互斥分支
    /// </summary>
    public sealed class ResultV2
    {
        /// <summary>种类</summary>
        public ResultKindV2 Kind;

        /// <summary>状态转移——状态机名</summary>
        public string? MachineName;

        /// <summary>状态转移——状态名</summary>
        public string? StateName;

        /// <summary>命题注册——命题名</summary>
        public string? PropName;
    }

    /// <summary>
    /// 积木调用——操作/测量采样
    /// </summary>
    public sealed class BrickCallV2
    {
        /// <summary>积木名</summary>
        public string BrickName = "";

        /// <summary>参数容器（已解析）</summary>
        public List<MauParamV2> Params = new List<MauParamV2>();

        /// <summary>捕获别名——:= 左侧（显式捕获时非空）</summary>
        public string? CaptureName;
    }

    /// <summary>
    /// 控制律——反应方程式（条件 + 操作 → 结果）
    /// </summary>
    public sealed class LawV2
    {
        /// <summary>控制律名</summary>
        public string Name = "";

        /// <summary>属性</summary>
        public LawAttrsV2 Attrs = new LawAttrsV2();

        /// <summary>条件树（∧ 顶层合取）</summary>
        public CondV2 Conditions = new CondV2();

        /// <summary>操作——积木调用序列（顺序执行）</summary>
        public List<BrickCallV2> Ops = new List<BrickCallV2>();

        /// <summary>结果分支——| 分隔互斥</summary>
        public List<ResultV2> Results = new List<ResultV2>();

        /// <summary>结果侧单一类型（状态转移 / 命题注册）</summary>
        public ResultKindV2 ResultKind;
    }

    /// <summary>
    /// 测量——周期采样写条件
    /// </summary>
    public sealed class MeasureV2
    {
        /// <summary>测量名</summary>
        public string Name = "";

        /// <summary>采样周期 ω（1 = 每帧）</summary>
        public int Frame = 1;

        /// <summary>目标条件命题</summary>
        public string Target = "";

        /// <summary>采样积木调用</summary>
        public BrickCallV2 Sample = new BrickCallV2();
    }

    /// <summary>
    /// 状态机
    /// </summary>
    public sealed class MachineV2
    {
        /// <summary>状态机名</summary>
        public string Name = "";

        /// <summary>状态列表（首元素 = 初始）</summary>
        public List<string> States = new List<string>();

        /// <summary>嵌套绑定（∈）</summary>
        public List<MachineBindV2> Binds = new List<MachineBindV2>();
    }

    /// <summary>
    /// 嵌套绑定——'S_Child' ∈ 'S_Parent.State'
    /// </summary>
    public sealed class MachineBindV2
    {
        /// <summary>子状态机名</summary>
        public string ChildName = "";

        /// <summary>父状态机名</summary>
        public string ParentName = "";

        /// <summary>父状态名</summary>
        public string ParentState = "";
    }

    /// <summary>
    /// 命题声明
    /// </summary>
    public sealed class PropositionV2
    {
        /// <summary>命题名</summary>
        public string Name = "";

        /// <summary>类型（推导）</summary>
        public PropKindV2 Kind = PropKindV2.Unknown;

        /// <summary>写入源标识——boundary/测量名/控制律名</summary>
        public string WriteSource = "";
    }

    /// <summary>
    /// 资源约束
    /// </summary>
    public sealed class ResourceV2
    {
        /// <summary>资源名</summary>
        public string Name = "";

        /// <summary>配额（1 = 独占）</summary>
        public int Quota = 1;
    }

    /// <summary>
    /// 系统边界——⇐ 输入 / ⇒ 输出
    /// </summary>
    public sealed class BoundaryV2
    {
        /// <summary>方向</summary>
        public BoundaryDirV2 Dir;

        /// <summary>信号名（Fire/OA 端口）</summary>
        public string SignalName = "";

        /// <summary>映射目标——OA 接收解包后写入的命题 / OA 发送的键（无映射为空）</summary>
        public string? MappedName;

        /// <summary>载荷参数（Fire 签名 / OA 载荷键）</summary>
        public List<MauParamV2> Params = new List<MauParamV2>();
    }

    /// <summary>
    /// 语法糖——SEQ/PAR/FBK
    /// </summary>
    public sealed class SugarV2
    {
        /// <summary>糖名——SEQ/PAR/FBK</summary>
        public string Name = "";

        /// <summary>引用序列（控制律/测量名）</summary>
        public List<string> RefNames = new List<string>();
    }

    /// <summary>
    /// 解析文档——完整 IR
    /// </summary>
    public sealed class MauDocV2
    {
        /// <summary>语法版本——'Mau' 声明</summary>
        public string? SyntaxVersion;

        /// <summary>运行时基座版本——'Mau.Runtime' 声明</summary>
        public string? RuntimeVersion;

        /// <summary>实现接口——@ 声明</summary>
        public List<string> Interfaces = new List<string>();

        /// <summary>注入字段——$ 声明</summary>
        public List<string> Injections = new List<string>();

        /// <summary>状态机列表</summary>
        public List<MachineV2> Machines = new List<MachineV2>();

        /// <summary>命题列表</summary>
        public List<PropositionV2> Propositions = new List<PropositionV2>();

        /// <summary>资源列表</summary>
        public List<ResourceV2> Resources = new List<ResourceV2>();

        /// <summary>系统边界列表</summary>
        public List<BoundaryV2> Boundaries = new List<BoundaryV2>();

        /// <summary>测量列表</summary>
        public List<MeasureV2> Measures = new List<MeasureV2>();

        /// <summary>控制律列表</summary>
        public List<LawV2> Laws = new List<LawV2>();

        /// <summary>语法糖列表</summary>
        public List<SugarV2> Sugars = new List<SugarV2>();

        /// <summary>
        /// 查状态机
        /// </summary>
        /// <param name="name">状态机名</param>
        /// <returns>状态机（不存在为 null）</returns>
        public MachineV2? FindMachine(string name)
        {
            for (int i = 0; i < Machines.Count; i++)
            {
                if (Machines[i].Name == name)
                {
                    return Machines[i];
                }
            }
            return null;
        }

        /// <summary>
        /// 查命题
        /// </summary>
        /// <param name="name">命题名</param>
        /// <returns>命题（不存在为 null）</returns>
        public PropositionV2? FindProposition(string name)
        {
            for (int i = 0; i < Propositions.Count; i++)
            {
                if (Propositions[i].Name == name)
                {
                    return Propositions[i];
                }
            }
            return null;
        }
    }
}
