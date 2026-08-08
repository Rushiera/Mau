// ═══════════════════════════════════════════════════
// 积木: csharp.comment_check
// ID:   BRIK-CSHARP-012
// 类别: CSHARP
// 作用: 扫描全项目缺 summary 的类/方法/字段/属性——返回清单+统计
// 依赖: 无
// 包: 无
// 引用: Mau.Runtime（ICSharpBridge）· System
// 原理: DataBox.TryResolve<ICSharpBridge> → Invoke("comment_check", "{}")——PACK 协议
// 常用: CSharpCat 工具 Cat——注释门禁（M2d.1）
// ═══════════════════════════════════════════════════
using System;

namespace Mau.Bricks
{
    /// <summary>
    /// C# 积木——csharp.comment_check 注释扫描（调度 ICSharpBridge）
    /// </summary>
    public static class CSharpCommentCheckBrick
    {
        /// <summary>
        /// 扫描缺 summary 成员——清单+统计
        /// </summary>
        /// <param name="result">清单文本</param>
        /// <returns>true=调用成功</returns>
        public static bool CommentCheck(out string result)
        {
            result = "";
            Mau.Runtime.ICSharpBridge bridge;
            Mau.Runtime.DataBox.TryResolve<Mau.Runtime.ICSharpBridge>(out bridge);
            if (bridge == null)
            {
                result = "ERR|CSHARP_NO_BRIDGE|宿主未注入 ICSharpBridge（Mau.Development.MauRoslynBridge）";
                return false;
            }
            return bridge.Invoke("comment_check", "{}", out result);
        }
    }
}
// #MAU_CHECKSUM:SHA256:7081B80E004AB09E4E8B70D523DCA3EEA3A86DFAD68FCA3F96A71D6AE10499BD
