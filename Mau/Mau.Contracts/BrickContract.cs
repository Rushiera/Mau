using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Mau.Contracts
{
    /// <summary>
    /// 积木端口——参数名 + 类型，参数名是传参协议的钥匙
    /// </summary>
    public sealed class BrickPort
    {
        /// <summary>
        /// 端口名——与 Mau 参数绑定名一致
        /// </summary>
        public string Name;

        /// <summary>
        /// 端口类型
        /// </summary>
        public Type Type;

        /// <summary>
        /// 端口描述——生成 XML 注释用
        /// </summary>
        public string Description;

        /// <summary>
        /// 构造端口
        /// </summary>
        /// <param name="name">端口名</param>
        /// <param name="type">端口类型</param>
        public BrickPort(string name, Type type)
        {
            Name = name;
            Type = type;
            Description = "";
        }

        /// <summary>
        /// 构造端口（带描述）
        /// </summary>
        /// <param name="name">端口名</param>
        /// <param name="type">端口类型</param>
        /// <param name="description">端口描述</param>
        public BrickPort(string name, Type type, string description)
        {
            Name = name;
            Type = type;
            Description = description;
        }
    }

    /// <summary>
    /// 积木时长形态——决定翻译器生成的调用形态
    /// </summary>
    public enum BrickDuration
    {
        /// <summary>
        /// 同步——调用返回即完成
        /// </summary>
        Sync,

        /// <summary>
        /// 异步回调——调用返回未完成，回调投递结果
        /// </summary>
        Async,

        /// <summary>
        /// 流式——多次回调（分片/进度）
        /// </summary>
        Streaming
    }

    /// <summary>
    /// 积木返回语义
    /// </summary>
    public enum BrickReturnKind
    {
        /// <summary>
        /// bool——true=成功 false=失败
        /// </summary>
        Bool,

        /// <summary>
        /// void——无返回，结果经输出端口表达
        /// </summary>
        Void,

        /// <summary>
        /// string——名称返回（多路匹配积木——switch(matched) 按名分发）
        /// </summary>
        String
    }

    /// <summary>
    /// 积木契约条目——注册表记录，翻译器构筑期查表
    /// </summary>
    public sealed class BrickContract
    {
        /// <summary>
        /// 积木名称——Mau 文本调用名（命名空间.函数），全系统唯一
        /// </summary>
        public string Name;

        /// <summary>
        /// 实现——C# 静态方法完整签名（命名空间.类.方法）
        /// </summary>
        public string Implementation;

        /// <summary>
        /// 输入端口——参数名 + 类型
        /// </summary>
        public List<BrickPort> Inputs;

        /// <summary>
        /// 输出端口——有返回数据的积木声明（可选）
        /// </summary>
        public List<BrickPort> Outputs;

        /// <summary>
        /// 返回语义
        /// </summary>
        public BrickReturnKind Return;

        /// <summary>
        /// 时长形态
        /// </summary>
        public BrickDuration Duration;

        /// <summary>
        /// 线程约束——main/worker/any
        /// </summary>
        public string Thread;

        /// <summary>
        /// 主值端口——数组输出时的语义主产出端口名（翻译器数组→标量绑定时用）
        /// </summary>
        public string MainOutput;

        /// <summary>
        /// 构造积木契约
        /// </summary>
        /// <param name="name">积木名称</param>
        /// <param name="implementation">实现签名</param>
        public BrickContract(string name, string implementation)
        {
            Name = name;
            Implementation = implementation;
            Inputs = new List<BrickPort>();
            Outputs = new List<BrickPort>();
            Return = BrickReturnKind.Bool;
            Duration = BrickDuration.Sync;
            Thread = "any";
            MainOutput = "";
        }
    }
}
