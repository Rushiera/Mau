// ═══════════════════════════════════════════════════
// 积木: csharp.member_delete
// ID:   BRIK-CSHARP-010
// 类别: CSHARP
// 作用: 删除指定成员（含注释）——编译通过才写盘
// 依赖: 无
// 包: 无
// 引用: Mau.Runtime（ICSharpBridge）· System
// 原理: DataBox.TryResolve<ICSharpBridge> → Invoke("member_delete", argsJson)——PACK 协议
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
            string argsJson = "{\"class\":\"" + Safe(className) + "\",\"member\":\"" + Safe(memberName) + "\"}";
            return bridge.Invoke("member_delete", argsJson, out result);
        }

        /// <summary>
        /// JSON 字符串安全转义
        /// </summary>
        /// <param name="value">原始值</param>
        /// <returns>转义后</returns>
        private static string Safe(string value)
        {
            if (value == null) { return ""; }
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "");
        }
    }
}
// #MAU_CHECKSUM:SHA256:F41FD3917E1F9338A0B6D9BA3B0F12F3CF7FBD5703FE81A79FBAFE6A4DC6F09F
