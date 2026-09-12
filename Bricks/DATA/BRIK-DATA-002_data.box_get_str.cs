// ═══════════════════════════════════════════════════
// 积木: data.box_get_str
// ID:   BRIK-DATA-002
// 类别: DATA
// 作用: 读 DataBox 数据区 str 值——scope/key 作用域键值（薄壳：DataBox 机制入口）
// 依赖: 无
// 引用: Mau.Runtime（DataBox）
// 原理: DataBox.TryGet<string>(scope, key, out value)
// 常用: CH4 第一轮三 Cat 语料——结果读取/状态检查（sys.box 观测同源）
// ═══════════════════════════════════════════════════
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// 数据积木——data.box_get_str 读 str 值（薄壳转发 DataBox）
    /// </summary>
    public static class DataBoxGetStrBrick
    {
        /// <summary>
        /// 读 DataBox 数据区 str 值
        /// </summary>
        /// <param name="scope">作用域</param>
        /// <param name="key">键</param>
        /// <param name="value">str 值——Key 不存在时为 default（null）；返回 false 表示键缺失（R3-P3-07 精确化）</param>
        /// <returns>true=Key 存在</returns>
        public static bool GetStr(string scope, string key, out string value)
        {
            return DataBox.TryGet<string>(scope, key, out value);
        }
    }
}
// #MAU_CHECKSUM:SHA256:41BD9B9A58F4CCDD473E8649DD9D17C8BEED5AF1366130CA9A55474773CA5C06
