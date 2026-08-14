using System;

namespace Mau.Contracts
{
    /// <summary>
    /// Mau 语言内核 TokenId 表——翻译器只认编号，不认字符（design-mau-v3 §三）。
    /// 词法级 token：符号 14 个 + 值 6 个 = 20 个。设计稿 §3.3 的语义项（T_STATE/T_SENSOR/
    /// T_WIRE/T_SLOT/T_ATTR_*/T_EVERY）由解析层按前缀与结构推导——单元关键字零化，
    /// 符合词法宪法"语义词退化为符号或推导"。
    /// </summary>
    public static class TokenIds
    {
        // ── 符号 token（外观层映射——ISymbolAppearance）──

        /// <summary>§ 段首标记——段 = § 开头到下一 § 前</summary>
        public const uint Section = 1;

        /// <summary>:= 声明符——v3 语法暂未消费，保留给未来糖/声明扩展</summary>
        public const uint Declare = 2;

        /// <summary>= 相等——状态断言 / 状态集合定义</summary>
        public const uint Eq = 3;

        /// <summary>: 冒号——导线头 / 槽声明的分隔</summary>
        public const uint Colon = 4;

        /// <summary>⇐ 被动输入——传感器触发器端口（别名 &lt;-）</summary>
        public const uint In = 5;

        /// <summary>↻ 主动采样——传感器周期采样（壳协程探测）</summary>
        public const uint Sample = 6;

        /// <summary>→ 结果——导线结果转移</summary>
        public const uint Arrow = 7;

        /// <summary>| 分支——互斥分叉（成功/失败两侧都转移）</summary>
        public const uint Branch = 8;

        /// <summary>&amp; 合取——条件合取</summary>
        public const uint And = 9;

        /// <summary>{ 集合开——状态集合定义</summary>
        public const uint SetOpen = 10;

        /// <summary>} 集合闭——状态集合定义</summary>
        public const uint SetClose = 11;

        /// <summary>, 分隔——状态集合/参数列表分隔</summary>
        public const uint Sep = 12;

        /// <summary>[ 参数开——参数/属性定界</summary>
        public const uint ParamOpen = 13;

        /// <summary>] 参数闭——参数/属性定界</summary>
        public const uint ParamClose = 14;

        // ── 值 token（承载内容——不进外观映射）──

        /// <summary>'...' 专有名词内容——标识符/积木名/状态名/糖名（白名单 [A-Za-z_.]+）</summary>
        public const uint Name = 15;

        /// <summary>数值 [0-9.]+——参数数值/槽配额</summary>
        public const uint Num = 16;

        /// <summary>"..." 字符串内容——任意字符</summary>
        public const uint Str = 17;

        /// <summary>裸词——参数内容（引用/属性键如 t= 左侧）</summary>
        public const uint Word = 18;

        /// <summary>// 行注释——词法丢弃（元信息，可作分组标题）</summary>
        public const uint Comment = 19;

        /// <summary>&gt; 捕获——导线动作 out 端口捕获落盒（@key 私有 / key 全局）</summary>
        public const uint Capture = 21;

        /// <summary>⇚ Command 输入——传感器绑定 CommandBus 外部指令源（唯一外部输入总线）</summary>
        public const uint CmdIn = 22;

        /// <summary>文件尾</summary>
        public const uint Eof = 20;
    }
}
