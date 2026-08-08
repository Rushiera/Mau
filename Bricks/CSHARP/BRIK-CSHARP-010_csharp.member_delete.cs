// ═══════════════════════════════════════════════════
// 积木: csharp.member_delete
// ID:   BRIK-CSHARP-010
// 类别: CSHARP
// 作用: 删除指定成员（含注释）——编译通过才写盘
// 依赖: 无
// 包: Microsoft.CodeAnalysis.Workspaces.MSBuild@4.11.0
// 引用: Mau.Runtime（ICSharpBridge）· System
// 原理: DataBox 解析 ICSharpBridge → DeleteMember
// 常用: CSharpCat 工具 Cat——成员删除（M2d.1）
// ═══════════════════════════════════════════════════
using System;

namespace Mau.Bricks
{
    /// <summary>
    /// C# 积木——csharp.member_delete 成员删除（调度 ICSharpBridge）
    /// </summary>
    public static class CSharpMemberDeleteBrick
    {
        /// <summary>
        /// 删除指定成员（含注释）
        /// </summary>
        /// <param name="className">类名</param>
        /// <param name="memberName">成员名</param>
        /// <param name="result">结果 JSON</param>
        /// <returns>true=调用成功</returns>
        public static bool MemberDelete(string className, string memberName, out string result)
        {
            result = "";
            Mau.Runtime.ICSharpBridge bridge;
            Mau.Runtime.DataBox.TryResolve<Mau.Runtime.ICSharpBridge>(out bridge);
            if (bridge == null)
            {
                result = "ERR|CSHARP_NO_BRIDGE|宿主未注入 ICSharpBridge（Mau.Development.MauRoslynBridge）";
                return false;
            }
            result = bridge.DeleteMember(className, memberName == null ? "" : memberName);
            return true;
        }
    }
}
// #MAU_CHECKSUM:SHA256:E5D8A6A33FBE1D8B768C2D5CBD11E3E9E32B385B081C4E81B73F08530FEE02A0
