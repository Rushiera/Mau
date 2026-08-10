// ═══════════════════════════════════════════════════
// 积木: cmd.set
// ID:   BRIK-CMD-004
// 类别: CMD
// 作用: 投递指令——int 与 text 双轨
// 依赖: 无
// 引用: Mau.Runtime
// 原理: 经 CmdBridge 实例 Set 值 + 非空文本 SetText
// 常用: 宿主/测试向 Cat 投递用户指令
// ═══════════════════════════════════════════════════
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// 指令积木——cmd.set 投递指令（依赖 CmdBridge）
    /// </summary>
    public static class CmdSetBrick
    {
        /// <summary>
        /// 投递指令——int 与 text 双轨；text 非空时同时写入文本池（source="brick"——C 类 Log 来源标识 2026-08-10）
        /// </summary>
        /// <param name="key">指令 key（必须已注册）</param>
        /// <param name="value">int 指令值</param>
        /// <param name="text">文本指令（空=不写文本）</param>
        /// <returns>true=投递完成（未注册 key 静默拒绝，与 CommandBus 语义一致）</returns>
        public static bool Set(string key, int value, string text)
        {
            ICommandBus? bus;
            DataBox.TryResolve<ICommandBus>(out bus);
            if (bus == null)
            {
                return false;
            }
            bus.Set(key, value, "brick");
            if (!string.IsNullOrEmpty(text))
            {
                bus.SetText(key, text, "brick");
            }
            return true;
        }
    }
}
// #MAU_CHECKSUM:SHA256:29521ADAAA32ABF8E24AF0F933EE36E55C9EE48179E7F4D2B8336AB7B64C014F
