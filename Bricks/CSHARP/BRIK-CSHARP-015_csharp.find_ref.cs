// ═══════════════════════════════════════════════════
// 积木: csharp.find_ref
// ID:   BRIK-CSHARP-015
// 类别: CSHARP
// 作用: 查找成员的所有引用位置——返回文件+行号+引用行内容
// 依赖: 无
// 包: Microsoft.CodeAnalysis.Workspaces.MSBuild@4.11.0
// 引用: Mau.Runtime（ICSharpBridge）· System
// 原理: DataBox 解析 ICSharpBridge → FindReferences（SymbolFinder 语义定位）
// 常用: CSharpCat 工具 Cat——引用定位（M2d.1）
// ═══════════════════════════════════════════════════
using System;

namespace Mau.Bricks
{
    /// <summary>
    /// C# 积木——csharp.find_ref 引用查找（调度 ICSharpBridge）
    /// </summary>
    public static class CSharpFindRefBrick
    {
        /// <summary>
        /// 查找成员引用——文件+行号+文本
        /// </summary>
        /// <param name="className">类名</param>
        /// <param name="memberName">成员名</param>
        /// <param name="result">结果 JSON</param>
        /// <returns>true=调用成功</returns>
        public static bool FindRef(string className, string memberName, out string result)
        {
            result = "";
            Mau.Runtime.ICSharpBridge bridge;
            Mau.Runtime.DataBox.TryResolve<Mau.Runtime.ICSharpBridge>(out bridge);
            if (bridge == null)
            {
                result = "ERR|CSHARP_NO_BRIDGE|宿主未注入 ICSharpBridge（Mau.Development.MauRoslynBridge）";
                return false;
            }
            result = bridge.FindReferences(className, memberName == null ? "" : memberName);
            return true;
        }
    }
}
// #MAU_CHECKSUM:SHA256:E8815058AB066D20C43BDDF965AC6033218DF566970F712F963DC4C6D5C776B1
