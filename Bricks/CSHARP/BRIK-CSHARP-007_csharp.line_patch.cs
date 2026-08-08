// ═══════════════════════════════════════════════════
// 积木: csharp.line_patch
// ID:   BRIK-CSHARP-007
// 类别: CSHARP
// 作用: 替换方法内指定行范围——行号 1-based 与 read 的 LN 对齐（编译通过才写盘）
// 依赖: 无
// 包: Microsoft.CodeAnalysis.Workspaces.MSBuild@4.11.0
// 引用: Mau.Runtime（ICSharpBridge）· System
// 原理: DataBox 解析 ICSharpBridge → LinePatch（行号越界保护）
// 常用: CSharpCat 工具 Cat——行级修复（M2d.1）
// ═══════════════════════════════════════════════════
using System;

namespace Mau.Bricks
{
    /// <summary>
    /// C# 积木——csharp.line_patch 行范围替换（调度 ICSharpBridge）
    /// </summary>
    public static class CSharpLinePatchBrick
    {
        /// <summary>
        /// 替换方法内指定行范围——1-based
        /// </summary>
        /// <param name="className">类名</param>
        /// <param name="methodName">方法名</param>
        /// <param name="startLine">起始行（方法内）</param>
        /// <param name="endLine">结束行（方法内）</param>
        /// <param name="newText">替换文本</param>
        /// <param name="result">结果 JSON</param>
        /// <returns>true=调用成功</returns>
        public static bool LinePatch(string className, string methodName, int startLine, int endLine, string newText, out string result)
        {
            result = "";
            Mau.Runtime.ICSharpBridge bridge;
            Mau.Runtime.DataBox.TryResolve<Mau.Runtime.ICSharpBridge>(out bridge);
            if (bridge == null)
            {
                result = "ERR|CSHARP_NO_BRIDGE|宿主未注入 ICSharpBridge（Mau.Development.MauRoslynBridge）";
                return false;
            }
            result = bridge.LinePatch(className, methodName, startLine, endLine, newText == null ? "" : newText);
            return true;
        }
    }
}
// #MAU_CHECKSUM:SHA256:E471CCCACAF209BD345D09CCCBBDD45F485DC7FBC1A57960ED25245D05380031
