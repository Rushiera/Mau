using System;

namespace Mau.Runtime
{
    /// <summary>
    /// 指令推入服务——外部投递口子（CLI/管道/UI）统一入口。
    /// CommandBus.Set 已通用；本服务把"投递"封装为宿主可挂载的 DataBox 服务，
    /// 宿主只做入口参数映射（args → Push），不直接碰总线。
    /// </summary>
    public sealed class CommandPump
    {
        /// <summary>
        /// 指令总线——投递目标
        /// </summary>
        private readonly ICommandBus _bus;

        /// <summary>
        /// 构造指令泵
        /// </summary>
        /// <param name="bus">指令总线</param>
        public CommandPump(ICommandBus bus)
        {
            if (bus == null)
            {
                throw new ArgumentNullException("bus");
            }
            _bus = bus;
        }

        /// <summary>
        /// 投递文本指令——key 必须已被模块注册（SetText 语义）
        /// </summary>
        /// <param name="key">指令 key</param>
        /// <param name="text">指令文本</param>
        public void PushText(string key, string text, string source)
{
            _bus.SetText(key, text, source);
        }
        /// <summary>
        /// 投递值指令——key 必须已被模块注册（Set 语义）
        /// </summary>
        /// <param name="key">指令 key</param>
        /// <param name="value">指令值</param>
        public void Push(string key, int value, string source)
{
            _bus.Set(key, value, source);
        }
        /// <summary>
        /// 底层总线访问——宿主/UI 高级操作（注册/清理）
        /// </summary>
        public ICommandBus Bus
        {
            get { return _bus; }
        }
    }
}
