// ═══════════════════════════════════════════════════
// 积木: csharp.find_ref
// ID:   BRIK-CSHARP-015
// 类别: CSHARP
// 作用: 查找成员的所有引用位置——返回文件+行号+引用行内容
// 依赖: 无
// 包: 无
// 引用: Mau.Runtime（ICSharpBridge）· System
// 原理: DataBox.TryResolve<ICSharpBridge> → Invoke("find_ref", argsJson)——PACK 协议
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
            string argsJson = "{\"class\":\"" + Safe(className) + "\",\"member\":\"" + Safe(memberName) + "\"}";
            return bridge.Invoke("find_ref", argsJson, out result);
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
// #MAU_CHECKSUM:SHA256:D5558C1C6B4AE0341C630249CB827DC1F023C880D0A1A840F58CA167F2CD6AA2
