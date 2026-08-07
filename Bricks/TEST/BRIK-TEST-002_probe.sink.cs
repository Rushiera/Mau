// ═══════════════════════════════════════════════════
// 积木: probe.sink
// ID:   BRIK-TEST-002
// 类别: TEST
// 作用: 测试探针——消费输入并输出结果（数据流绑定验证用：多输入单输出）
// 依赖: 无
// 引用: System
// 原理: a/b 拼接为 result——验证箭头绑定与多端口
// 常用: 翻译器数据流测试（箭头/常量绑定）
// ═══════════════════════════════════════════════════
using System;

namespace Mau.Bricks
{
    /// <summary>
    /// 测试探针积木——probe.sink 消费输入输出结果（数据流绑定验证）
    /// </summary>
    public static class ProbeSinkBrick
    {
        /// <summary>
        /// 拼接结果
        /// </summary>
        /// <param name="a">文本输入</param>
        /// <param name="b">整数输入</param>
        /// <param name="result">结果</param>
        /// <returns>true=成功</returns>
        public static bool Sink(string a, int b, out string result)
        {
            result = a + ":" + b.ToString();
            return true;
        }
    }
}
// #MAU_CHECKSUM:SHA256:70C58391B3EF6848F0C99AE941C1E0B74400625715F19A27777595DBDC6D3DC0
