using System;

namespace Mau.Runtime
{
    /// <summary>
    /// 查询线程域标注——组件声明查询处理器运行在哪个线程域（design-mau-rebuild.md §2.2 D11/D12）
    /// </summary>
    public enum QueryDomain
    {
        /// <summary>
        /// 任意线程——组件自身线程安全（GAP.1 锁内化契约；CommandBus.GetSnapshot/DataBox.Capture 同规）
        /// </summary>
        Any,

        /// <summary>
        /// 主线程——经通道自动投递（GAP.2 InvokeOnMain 内部机制；FlowRunner.GetStatus/OA.GetSnapshot 守卫组件）
        /// </summary>
        Main
    }
}
