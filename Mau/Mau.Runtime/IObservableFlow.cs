namespace Mau.Runtime
{
    /// <summary>
    /// 生成物接口——宿主驱动的契约。
    /// Tick(int frame)——帧号由宿主统一注入（数字电路时钟脉冲外部注入语义）。
    /// </summary>
    public interface IFlow
    {
        /// <summary>
        /// 每帧驱动——宿主主循环调用，帧号注入
        /// </summary>
        /// <param name="frame">宿主帧号（FlowRunner 统一注入）</param>
        void Tick(int frame);

        /// <summary>
        /// Flow 自曝元数据——加载必需信息（组名 + 认领工具清单）的规范化 JSON。
        /// 宿主装配（扫描 dll 建路由表）/ 外观层（工具归属展示）统一经此接口读取——Flow 自己说话。
        /// </summary>
        /// <returns>规范化 JSON：{"group":"TextCat","claims":["text-read","text-write"]}</returns>
        string GetMetaJson();

        /// <summary>
        /// Flow 自曝工具定义——本组全部工具的 OpenAI 兼容定义（name/description/parameters）JSON。
        /// 宿主聚合工具池（请求体拼装原料）唯一来源；BRIK tools.&lt;flowName&gt; 提供（改描述只动积木）。
        /// </summary>
        /// <returns>规范化 JSON：{"group":"TextCat","tools":[{"name":"text-read","description":"...","parameters":{...}}]}</returns>
        string GetToolsJson();
    }

    /// <summary>
    /// 可观察生成物接口——IFlow + 四柱快照查询面（P3 观测支柱）。
    /// GetStatus 返回 FlowStatusV3 全量截面——主线程调用契约（与 SysCommand 同规）。
    /// </summary>
    public interface IObservableFlow : IFlow
    {
        /// <summary>
        /// 获取运行时状态快照——四柱截面（状态机枚举值/传感器实测/槽余量/导线状态）
        /// </summary>
        /// <returns>四柱快照</returns>
        FlowStatusV3 GetStatus();

        /// <summary>
        /// 获取实体自述——模块自己的声音（多行自由形态，自然语言优先）。
        /// 数据源 DataBox scope=flowId key=self_desc（积木 self.desc/self.desc.add 写入）。
        /// </summary>
        /// <returns>自述行数组（空数组=无自述）</returns>
        string[] GetSelfDesc();
    }

    /// <summary>
    /// 四柱快照——生成物观测的唯一形态（v1 的 RuntimeStatus 已退役）。
    /// 状态行 = 排错第一锚点；导线状态 = par 在途/Busy/超时观测。
    /// </summary>
    public sealed class FlowStatusV3
    {
        /// <summary>
        /// 生成物最近驱动帧号
        /// </summary>
        public long Frame;

        /// <summary>
        /// 状态机枚举值——"S_Talk=Thinking" 每机一行（GetState 一行显示）
        /// </summary>
        public string[] StateLines = new string[0];

        /// <summary>
        /// 主动传感器实测值（被动传感器是事件沿——消费即清，不进快照）
        /// </summary>
        public SignalValueV3[] SensorValues = new SignalValueV3[0];

        /// <summary>
        /// 槽余量（可用/容量）
        /// </summary>
        public SlotValueV3[] SlotLevels = new SlotValueV3[0];

        /// <summary>
        /// 导线状态（Busy/在途/超时/最近触发帧）
        /// </summary>
        public WireStatusV3[] WireStatuses = new WireStatusV3[0];
    }

    /// <summary>
    /// 传感器实测值条目
    /// </summary>
    public sealed class SignalValueV3
    {
        /// <summary>
        /// 传感器名
        /// </summary>
        public string Name = "";

        /// <summary>
        /// 最近采样实测值
        /// </summary>
        public bool Value;
    }

    /// <summary>
    /// 槽余量条目
    /// </summary>
    public sealed class SlotValueV3
    {
        /// <summary>
        /// 槽名
        /// </summary>
        public string Name = "";

        /// <summary>
        /// 当前可用（初始 = 容量——v2.0.7 判例）
        /// </summary>
        public long Available;

        /// <summary>
        /// 容量上限
        /// </summary>
        public long Capacity;
    }

    /// <summary>
    /// 导线状态条目
    /// </summary>
    public sealed class WireStatusV3
    {
        /// <summary>
        /// 导线名
        /// </summary>
        public string Name = "";

        /// <summary>
        /// Busy 门——后台动作在途（防重入）
        /// </summary>
        public bool Busy;

        /// <summary>
        /// 最近触发帧号（0=未触发）
        /// </summary>
        public long LastTriggerFrame;

        /// <summary>
        /// 最近一次是否超时结束（par 时限耗尽丢弃迟到结果）
        /// </summary>
        public bool TimedOut;
    }
}
