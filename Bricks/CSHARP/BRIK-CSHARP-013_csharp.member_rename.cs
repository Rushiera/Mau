// ═══════════════════════════════════════════════════
// 积木: csharp.member_rename
// ID:   BRIK-CSHARP-013
// 类别: CSHARP
// 作用: 重命名成员——全项目引用同步更新（含 using）；编译检查通过才写盘
// 依赖: 无
// 包: 无
// 引用: Mau.Runtime（ICSharpBridge）· System
// 原理: DataBox.TryResolve<ICSharpBridge> → Invoke("member_rename", argsJson)——PACK 协议
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
            string argsJson = "{\"class\":\"" + Safe(className) + "\",\"oldName\":\"" + Safe(oldName) + "\",\"newName\":\"" + Safe(newName) + "\"}";
            return bridge.Invoke("member_rename", argsJson, out result);
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
// #MAU_CHECKSUM:SHA256:117608DCFC2485CB0D42634317DF1ABB798E39C8FEE924B9E9D485060B91BD42
