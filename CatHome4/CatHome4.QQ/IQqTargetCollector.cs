using System;
using System.Collections.Generic;

namespace CatHome4.QQ
{
    /// <summary>
    /// qqbot 绑定目标收集面——QQ 域消费（输入路由广播注入 + 输出转发轮询），入口壳实现（默认猫 + 多猫注册表）。
    /// S3 解耦：QQBotService 不再直接引用 Program 静态面——经本接口注入获取目标。
    /// </summary>
    public interface IQqTargetCollector
    {
        /// <summary>收集绑定指定 qqbot 的猫——默认猫 + 多猫（R2.3.4 输入路由广播注入面）</summary>
        /// <param name="qqBotId">qqbot 配置身份</param>
        /// <returns>绑定目标列表（空=未绑定任何猫）</returns>
        List<QqTarget> CollectByBot(Guid qqBotId);

        /// <summary>收集所有绑定 qqbot 的猫——默认猫 + 多猫（R2.3.5 输出转发轮询面）</summary>
        /// <returns>全部绑定目标（QqBotId 非空）</returns>
        List<QqTarget> CollectAll();
    }
}
