// ═══════════════════════════════════════════════════
// 积木: csharp.member_insert
// ID:   BRIK-CSHARP-009
// 类别: CSHARP
// 作用: 在类中插入新成员——position: after/before/end/after_fields（编译通过才写盘）
// 依赖: 无
// 包: 无
// 引用: Mau.Runtime（ICSharpBridge）· System
// 原理: DataBox.TryResolve<ICSharpBridge> → Invoke("member_insert", argsJson)——PACK 协议
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
            string argsJson = "{\"class\":\"" + Safe(className) + "\",\"position\":\"" + Safe(position) + "\",\"anchor\":\"" + Safe(anchor) + "\",\"code\":\"" + Safe(code) + "\"}";
            return bridge.Invoke("member_insert", argsJson, out result);
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
// #MAU_CHECKSUM:SHA256:86539C932190F54D97E2EBDDDBC18F0AD4323B48D5D8EFF80BBB8AF567F791E2
