// ═══════════════════════════════════════════════════
// 积木: csharp.compile
// ID:   BRIK-CSHARP-001
// 类别: CSHARP
// 作用: C# 编译占位——走通工具路径（未来接入 Roslyn，照搬 CH2 csharpcode 工具组体系）
// 依赖: 无
// 包: 无
// 引用: System
// 原理: 占位空返回——TODO：内置 Roslyn 后实现 csharpcode 全工具组（独立 todo）
// 常用: CSharpCat 工具 Cat——工具路径验证（M2c 六+一域 CSharp 域）
// ═══════════════════════════════════════════════════
using System;

namespace Mau.Bricks
{
    /// <summary>
    /// C# 积木——csharp.compile 占位（TODO：Roslyn 接入后实现 csharpcode 工具组）
    /// </summary>
    public static class CSharpCompileBrick
    {
        /// <summary>
        /// C# 编译占位——返回待实现提示，走通工具调用路径
        /// </summary>
        /// <param name="source">C# 源码（占位忽略）</param>
        /// <param name="className">类名（占位忽略）</param>
        /// <param name="result">结果文本</param>
        /// <returns>true=调用成功（占位）</returns>
        public static bool Compile(string source, string className, out string result)
        {
            result = "PENDING|CSHARP_TOOLS_NOT_IMPLEMENTED|内置 Roslyn 接入为独立 todo（照搬 CH2 csharpcode 工具组体系）";
            return true;
        }
    }
}
// #MAU_CHECKSUM:SHA256:7F49F4D01D1A6CCBD6DB1C25D0B02FB54B15E028035A9BB239199DA670F2CE29
