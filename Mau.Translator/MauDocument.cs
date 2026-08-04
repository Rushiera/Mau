using System.Collections.Generic;

namespace Mau.Translator
{
    /// <summary>
    /// 命题类型——条件/信号/事实
    /// </summary>
    public enum PropositionKind
    {
        /// <summary>
        /// 条件——查询，真值自由变化，不消费
        /// </summary>
        Condition,

        /// <summary>
        /// 信号——通知，消费即清除
        /// </summary>
        Signal,

        /// <summary>
        /// 事实——记录，置位后保持，显式/轮末重置
        /// </summary>
        Fact
    }

    /// <summary>
    /// 命题——IR 节点
    /// </summary>
    public sealed class IrProposition
    {
        /// <summary>
        /// 命题名——P_ 前缀
        /// </summary>
        public string Name;

        /// <summary>
        /// 命题类型
        /// </summary>
        public PropositionKind Kind;

        /// <summary>
        /// 初始值——默认假；事实不允许初始真
        /// </summary>
        public bool Initial;

        /// <summary>
        /// 事实重置策略——显式/轮末，默认显式
        /// </summary>
        public string Reset;

        /// <summary>
        /// 声明行号——诊断定位
        /// </summary>
        public int Line;

        /// <summary>
        /// 构造命题
        /// </summary>
        /// <param name="name">命题名</param>
        /// <param name="kind">命题类型</param>
        /// <param name="line">声明行号</param>
        public IrProposition(string name, PropositionKind kind, int line)
        {
            Name = name;
            Kind = kind;
            Initial = false;
            Reset = "显式";
            Line = line;
        }
    }

    /// <summary>
    /// 参数绑定——动作参数名与积木端口名的匹配对
    /// </summary>
    public sealed class IrParamBinding
    {
        /// <summary>
        /// 变量名——Mau 侧数据来源
        /// </summary>
        public string Variable;

        /// <summary>
        /// 端口名——积木输入端口名
        /// </summary>
        public string PortName;

        /// <summary>
        /// 构造参数绑定
        /// </summary>
        /// <param name="variable">变量名</param>
        /// <param name="portName">端口名</param>
        public IrParamBinding(string variable, string portName)
        {
            Variable = variable;
            PortName = portName;
        }
    }

    /// <summary>
    /// 变迁——IR 节点
    /// </summary>
    public sealed class IrTransition
    {
        /// <summary>
        /// 变迁名——T_ 前缀
        /// </summary>
        public string Name;

        /// <summary>
        /// 前置命题名列表——全部成立才触发
        /// </summary>
        public List<string> Preconditions;

        /// <summary>
        /// 动作积木名——注册表键
        /// </summary>
        public string BrickName;

        /// <summary>
        /// 参数绑定列表
        /// </summary>
        public List<IrParamBinding> Params;

        /// <summary>
        /// 是否声明了时限
        /// </summary>
        public bool HasTimeout;

        /// <summary>
        /// 时限模式——Total/Idle/None
        /// </summary>
        public string TimeoutMode;

        /// <summary>
        /// 时限帧数——Total/Idle 模式有效
        /// </summary>
        public long TimeoutFrames;

        /// <summary>
        /// 正常后置命题名列表
        /// </summary>
        public List<string> PostOk;

        /// <summary>
        /// 错误后置命题名列表
        /// </summary>
        public List<string> PostError;

        /// <summary>
        /// 线程——main/worker，默认 main
        /// </summary>
        public string Thread;

        /// <summary>
        /// 帧——每帧/第N帧/N帧后，默认每帧
        /// </summary>
        public string FrameSpec;

        /// <summary>
        /// 汇合——直连/inbox，默认直连
        /// </summary>
        public string Join;

        /// <summary>
        /// 声明行号——诊断定位
        /// </summary>
        public int Line;

        /// <summary>
        /// 构造变迁
        /// </summary>
        /// <param name="name">变迁名</param>
        /// <param name="line">声明行号</param>
        public IrTransition(string name, int line)
        {
            Name = name;
            Preconditions = new List<string>();
            BrickName = "";
            Params = new List<IrParamBinding>();
            HasTimeout = false;
            TimeoutMode = "None";
            TimeoutFrames = 0;
            PostOk = new List<string>();
            PostError = new List<string>();
            Thread = "main";
            FrameSpec = "每帧";
            Join = "直连";
            Line = line;
        }
    }

    /// <summary>
    /// 资源——IR 节点（独占/配额）
    /// </summary>
    public sealed class IrResource
    {
        /// <summary>
        /// 资源名——R_ 前缀
        /// </summary>
        public string Name;

        /// <summary>
        /// 资源类型——独占/配额
        /// </summary>
        public string Kind;

        /// <summary>
        /// 配额数——Kind=配额时有效，默认0
        /// </summary>
        public long Quota;

        /// <summary>
        /// 声明行号——诊断定位
        /// </summary>
        public int Line;

        /// <summary>
        /// 构造资源
        /// </summary>
        /// <param name="name">资源名</param>
        /// <param name="line">声明行号</param>
        public IrResource(string name, int line)
        {
            Name = name;
            Kind = "独占";
            Quota = 0;
            Line = line;
        }
    }

    /// <summary>
    /// 通道——IR 节点（六类型）
    /// </summary>
    public sealed class IrChannel
    {
        /// <summary>
        /// 通道名——C_ 前缀
        /// </summary>
        public string Name;

        /// <summary>
        /// 数据源声明
        /// </summary>
        public string Source;

        /// <summary>
        /// 数据目标声明
        /// </summary>
        public string Target;

        /// <summary>
        /// 通道类型——直连/inbox/工单/命令/快照/跨进程
        /// </summary>
        public string ChannelType;

        /// <summary>
        /// 声明行号——诊断定位
        /// </summary>
        public int Line;

        /// <summary>
        /// 构造通道
        /// </summary>
        /// <param name="name">通道名</param>
        /// <param name="line">声明行号</param>
        public IrChannel(string name, int line)
        {
            Name = name;
            Source = "";
            Target = "";
            ChannelType = "直连";
            Line = line;
        }
    }

    /// <summary>
    /// 组合——IR 节点（序列/并行/选择/重试）
    /// </summary>
    public sealed class IrComposition
    {
        /// <summary>
        /// 组合名——FL_ 前缀
        /// </summary>
        public string Name;

        /// <summary>
        /// 序列变迁名列表
        /// </summary>
        public List<string> Sequence;

        /// <summary>
        /// 并行变迁名列表
        /// </summary>
        public List<string> Parallel;

        /// <summary>
        /// 选择分支——T_A → T_B | T_C
        /// </summary>
        public string Choice;

        /// <summary>
        /// 重试次数——0=不重试
        /// </summary>
        public long Retry;

        /// <summary>
        /// 汇合命题——A / B 互斥
        /// </summary>
        public string Merge;

        /// <summary>
        /// 声明行号——诊断定位
        /// </summary>
        public int Line;

        /// <summary>
        /// 构造组合
        /// </summary>
        /// <param name="name">组合名</param>
        /// <param name="line">声明行号</param>
        public IrComposition(string name, int line)
        {
            Name = name;
            Sequence = new List<string>();
            Parallel = new List<string>();
            Choice = "";
            Retry = 0;
            Merge = "";
            Line = line;
        }
    }

    /// <summary>
    /// Mau 文档——解析产物，即 IR 图（第一期 AST 与 IR 合一）
    /// </summary>
    public sealed class MauDocument
    {
        /// <summary>
        /// 语法版本
        /// </summary>
        public string Version;

        /// <summary>
        /// 基座声明
        /// </summary>
        public string BaseName;

        /// <summary>
        /// 命题表
        /// </summary>
        public List<IrProposition> Propositions;

        /// <summary>
        /// 变迁表
        /// </summary>
        public List<IrTransition> Transitions;
        /// <summary>
        /// 资源表
        /// </summary>
        public List<IrResource> Resources;

        /// <summary>
        /// 通道表
        /// </summary>
        public List<IrChannel> Channels;

        /// <summary>
        /// 组合表
        /// </summary>
        public List<IrComposition> Compositions;
        /// <summary>
        /// 构造文档
        /// </summary>
        public MauDocument()
        {
            Version = "";
            BaseName = "";
            Propositions = new List<IrProposition>();
            Transitions = new List<IrTransition>();
            Resources = new List<IrResource>();
            Channels = new List<IrChannel>();
            Compositions = new List<IrComposition>();
        }

        /// <summary>
        /// 按名查命题
        /// </summary>
        /// <param name="name">命题名</param>
        /// <returns>命题或空</returns>
        public IrProposition? FindProposition(string name)
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

        /// <summary>
        /// 按名查变迁
        /// </summary>
        /// <param name="name">变迁名</param>
        /// <returns>变迁或空</returns>
        public IrTransition? FindTransition(string name)
        {
            for (int i = 0; i < Transitions.Count; i++)
            {
                if (Transitions[i].Name == name)
                {
                    return Transitions[i];
                }
            }
            return null;
        }

        /// <summary>
        /// 按名查资源
        /// </summary>
        /// <param name="name">资源名</param>
        /// <returns>资源或空</returns>
        public IrResource? FindResource(string name)
        {
            for (int i = 0; i < Resources.Count; i++)
            {
                if (Resources[i].Name == name)
                {
                    return Resources[i];
                }
            }
            return null;
        }

        /// <summary>
        /// 按名查通道
        /// </summary>
        /// <param name="name">通道名</param>
        /// <returns>通道或空</returns>
        public IrChannel? FindChannel(string name)
        {
            for (int i = 0; i < Channels.Count; i++)
            {
                if (Channels[i].Name == name)
                {
                    return Channels[i];
                }
            }
            return null;
        }

        /// <summary>
        /// 按名查组合
        /// </summary>
        /// <param name="name">组合名</param>
        /// <returns>组合或空</returns>
        public IrComposition? FindComposition(string name)
        {
            for (int i = 0; i < Compositions.Count; i++)
            {
                if (Compositions[i].Name == name)
                {
                    return Compositions[i];
                }
            }
            return null;
        }
    }
}
