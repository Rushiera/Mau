using System;
using System.Collections.Generic;

namespace Mau.Runtime
{
    /// <summary>
    /// 积木契约类型名 → Type 映射——唯一实现（审查修复轮 2026-08-11 决策：BrickIndex.MapType 与 CommandBricks.TypeFromName 原两份同表收拢）。
    /// 兼容两种输入格式：JSON 类型名（List`1 / Dictionary`2）与 C# 源码类型文本（List&lt;MarkdownPart&gt; / Dictionary&lt;string, List&lt;string&gt;&gt;）。
    /// </summary>
    public static class TypeMap
    {
        /// <summary>
        /// 类型名 → Type 映射——覆盖积木契约全量类型；未知类型回退 string
        /// </summary>
        /// <param name="typeName">类型名（JSON 名或 C# 文本，可带 ? 后缀）</param>
        /// <returns>映射 Type</returns>
        public static Type Map(string typeName)
        {
            string t = (typeName ?? "").Trim();
            if (t.EndsWith("?"))
            {
                t = t.Substring(0, t.Length - 1);
            }
            switch (t)
            {
                case "string": return typeof(string);
                case "long": return typeof(long);
                case "int": return typeof(int);
                case "bool": return typeof(bool);
                case "double": return typeof(double);
                case "string[]": return typeof(string[]);
                case "long[]": return typeof(long[]);
                case "int[]": return typeof(int[]);
                case "bool[]": return typeof(bool[]);
                case "Office[]": return typeof(Office[]);
                case "Office": return typeof(Office);
                case "OfficeData": return typeof(OfficeData);
                case "Dictionary<string, List<string>>": return typeof(Dictionary<string, List<string>>);
                case "Dictionary`2": return typeof(Dictionary<string, List<string>>);
                default:
                    return typeof(string);
            }
        }
    }
}
