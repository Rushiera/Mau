namespace Mau.Runtime
{
    /// <summary>
    /// 指令邮件。一个模块一次收到的所有指令。由 CommandBus 在 Tick 中汇集并分发给注册者。
    /// 双轨 payload：CmdValues(int) 和 CmdTexts(string)，与 CmdKeys 一一对应。
    /// </summary>
    public struct CommandPack
    {
        /// <summary>
        /// 接收者全局唯一 ID
        /// </summary>
        public long OwnerLongId;

        /// <summary>
        /// 指令键数组，与 CmdValues / CmdTexts 一一对应
        /// </summary>
        public string[] CmdKeys;

        /// <summary>
        /// int payload——通过 Set(key, int) 写入
        /// </summary>
        public int[] CmdValues;

        /// <summary>
        /// string payload——通过 SetText(key, string) 写入。未写入时为 null
        /// </summary>
        public string[] CmdTexts;
    }
}
