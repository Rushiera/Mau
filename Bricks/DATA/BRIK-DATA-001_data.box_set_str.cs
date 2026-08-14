// ═══════════════════════════════════════════════════
// 积木: data.box_set_str
// ID:   BRIK-DATA-001
// 类别: DATA
// 作用: 写 DataBox 数据区 str 值——scope/key 作用域键值（薄壳：DataBox 机制入口）
// 依赖: 无
// 引用: Mau.Runtime（DataBox）
// 原理: DataBox.Set<string>(scope, key, value)
// 常用: CH4 第一轮 tool_test_cat 语料——结果暂存（sys.box 观测可见）
// ═══════════════════════════════════════════════════
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// 数据积木——data.box_set_str 写 str 值（薄壳转发 DataBox）
    /// </summary>
    public static class DataBoxSetStrBrick
    {
        /// <summary>
        /// 写 DataBox 数据区 str 值
        /// </summary>
        /// <param name="scope">作用域</param>
        /// <param name="key">键</param>
        /// <param name="value">str 值</param>
        /// <returns>true=已写入</returns>
        public static bool SetStr(string scope, string key, string value)
        {
            DataBox.Set<string>(scope, key, value);
            return true;
        }
    }
}
// #MAU_CHECKSUM:SHA256:AFA6B1EEB967A369533F6E4945D0708678C2300F02C5ED6F0FD661A8078B6EF1
