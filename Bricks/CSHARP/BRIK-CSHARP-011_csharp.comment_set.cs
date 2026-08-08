// ═══════════════════════════════════════════════════
// 积木: csharp.comment_set
// ID:   BRIK-CSHARP-011
// 类别: CSHARP
// 作用: 设置 XML 注释——type: summary/param/returns；member 空=设类
// 依赖: 无
// 包: Microsoft.CodeAnalysis.Workspaces.MSBuild@4.11.0
// 引用: Mau.Runtime（ICSharpBridge）· System
// 原理: DataBox 解析 ICSharpBridge → SetComment
// 常用: CSharpCat 工具 Cat——注释维护（M2d.1）
// ═══════════════════════════════════════════════════
using System;

namespace Mau.Bricks
{
    /// <summary>
    /// C# 积木——csharp.comment_set XML 注释（调度 ICSharpBridge）
    /// </summary>
    public static class CSharpCommentSetBrick
    {
        /// <summary>
        /// 设置 XML 注释——summary/param/returns
        /// </summary>
        /// <param name="className">类名</param>
        /// <param name="memberName">成员名（空=类）</param>
        /// <param name="commentType">summary/param/returns</param>
        /// <param name="text">注释文本</param>
        /// <param name="paramName">type=param 时必填</param>
        /// <param name="result">结果 JSON</param>
        /// <returns>true=调用成功</returns>
        public static bool CommentSet(string className, string memberName, string commentType, string text, string paramName, out string result)
        {
            result = "";
            Mau.Runtime.ICSharpBridge bridge;
            Mau.Runtime.DataBox.TryResolve<Mau.Runtime.ICSharpBridge>(out bridge);
            if (bridge == null)
            {
                result = "ERR|CSHARP_NO_BRIDGE|宿主未注入 ICSharpBridge（Mau.Development.MauRoslynBridge）";
                return false;
            }
            result = bridge.SetComment(className, memberName == null ? "" : memberName, commentType, text == null ? "" : text, paramName == null ? "" : paramName);
            return true;
        }
    }
}
// #MAU_CHECKSUM:SHA256:0D7D63322F3A4B94D8C093EC0D96E7DEA076EA704A2CD68E7D3D48B24904A010
