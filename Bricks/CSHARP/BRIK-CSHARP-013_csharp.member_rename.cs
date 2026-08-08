// ═══════════════════════════════════════════════════
// 积木: csharp.member_rename
// ID:   BRIK-CSHARP-013
// 类别: CSHARP
// 作用: 重命名成员——全项目引用同步更新（含 using）；编译检查通过才写盘
// 依赖: 无
// 包: Microsoft.CodeAnalysis.Workspaces.MSBuild@4.11.0
// 引用: Mau.Runtime（ICSharpBridge）· System
// 原理: DataBox 解析 ICSharpBridge → RenameMember（Renamer + 编译回滚）
// 常用: CSharpCat 工具 Cat——重构重命名（M2d.1）
// ═══════════════════════════════════════════════════
using System;

namespace Mau.Bricks
{
    /// <summary>
    /// C# 积木——csharp.member_rename 重命名（调度 ICSharpBridge）
    /// </summary>
    public static class CSharpMemberRenameBrick
    {
        /// <summary>
        /// 重命名成员——全项目引用同步
        /// </summary>
        /// <param name="className">类名</param>
        /// <param name="oldName">旧名</param>
        /// <param name="newName">新名</param>
        /// <param name="result">结果 JSON</param>
        /// <returns>true=调用成功</returns>
        public static bool MemberRename(string className, string oldName, string newName, out string result)
        {
            result = "";
            Mau.Runtime.ICSharpBridge bridge;
            Mau.Runtime.DataBox.TryResolve<Mau.Runtime.ICSharpBridge>(out bridge);
            if (bridge == null)
            {
                result = "ERR|CSHARP_NO_BRIDGE|宿主未注入 ICSharpBridge（Mau.Development.MauRoslynBridge）";
                return false;
            }
            result = bridge.RenameMember(className, oldName, newName);
            return true;
        }
    }
}
// #MAU_CHECKSUM:SHA256:A1025ECCBA1B6121D335BA7A2D9B2A261DC8E0DFB848A8C79A6C900AA77469F1
