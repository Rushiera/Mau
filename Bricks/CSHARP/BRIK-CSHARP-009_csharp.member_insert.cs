// ═══════════════════════════════════════════════════
// 积木: csharp.member_insert
// ID:   BRIK-CSHARP-009
// 类别: CSHARP
// 作用: 在类中插入新成员——position: after/before/end/after_fields（编译通过才写盘）
// 依赖: 无
// 包: Microsoft.CodeAnalysis.Workspaces.MSBuild@4.11.0
// 引用: Mau.Runtime（ICSharpBridge）· System
// 原理: DataBox 解析 ICSharpBridge → InsertMember（锚点定位 + 编译回滚）
// 常用: CSharpCat 工具 Cat——成员新增（M2d.1）
// ═══════════════════════════════════════════════════
using System;

namespace Mau.Bricks
{
    /// <summary>
    /// C# 积木——csharp.member_insert 成员插入（调度 ICSharpBridge）
    /// </summary>
    public static class CSharpMemberInsertBrick
    {
        /// <summary>
        /// 在类中插入新成员
        /// </summary>
        /// <param name="className">类名</param>
        /// <param name="position">after/before/end/after_fields</param>
        /// <param name="anchor">锚点成员名（after/before 必填）</param>
        /// <param name="code">新成员完整源码</param>
        /// <param name="result">结果 JSON</param>
        /// <returns>true=调用成功</returns>
        public static bool MemberInsert(string className, string position, string anchor, string code, out string result)
        {
            result = "";
            Mau.Runtime.ICSharpBridge bridge;
            Mau.Runtime.DataBox.TryResolve<Mau.Runtime.ICSharpBridge>(out bridge);
            if (bridge == null)
            {
                result = "ERR|CSHARP_NO_BRIDGE|宿主未注入 ICSharpBridge（Mau.Development.MauRoslynBridge）";
                return false;
            }
            result = bridge.InsertMember(className, position, anchor == null ? "" : anchor, code == null ? "" : code);
            return true;
        }
    }
}
// #MAU_CHECKSUM:SHA256:C448A2322183013BA262BE9F4E2F82AE7E50E173529D64C2932BC0A2BCA0B45C
