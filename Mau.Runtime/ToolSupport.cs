using System.Collections.Generic;

namespace Mau.Runtime
{
    /// <summary>
    /// 工具分发支撑（程序级）——游标表经 DataBox scope 存储（BRIK 唯一数据协议）
    /// </summary>
    public static class ToolSupport
    {
        /// <summary>
        /// 分发游标表——sessionKey → 下一个待发工具索引
        /// </summary>
        public static Dictionary<string, long> Cursors
        {
            get { return DataBox.GetOrCreate<Dictionary<string, long>>("tool", "cursors"); }
        }

        /// <summary>
        /// 游标表锁
        /// </summary>
        public static object Sync
        {
            get { return DataBox.GetOrCreate<object>("tool", "sync"); }
        }
    }
}
