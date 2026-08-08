// ═══════════════════════════════════════════════════
// 积木: csharp.body_replace
// ID:   BRIK-CSHARP-006
// 类别: CSHARP
// 作用: 替换整个方法体——签名+注释不动，只换 { } 内部（编译通过才写盘）
// 依赖: 无
// 包: 无
// 引用: Mau.Runtime（ICSharpBridge）· System
// 原理: DataBox.TryResolve<ICSharpBridge> → Invoke("body_replace", argsJson)——PACK 协议
// 常用: CSharpCat 工具 Cat——方法体重写（M2d.1）
// ═══════════════════════════════════════════════════
using System;

namespace Mau.Bricks
{
    /// <summary>
    /// C# 积木——csharp.body_replace 方法体替换（调度 ICSharpBridge）
    /// </summary>
    public static class CSharpBodyReplaceBrick
    {
        /// <summary>
        /// 替换整个方法体——签名+注释不动
        /// </summary>
        /// <param name="className">类名</param>
        /// <param name="methodName">方法名</param>
        /// <param name="body">新方法体（含大括号）</param>
        /// <param name="result">结果 JSON</param>
        /// <returns>true=调用成功</returns>
        public static bool BodyReplace(string className, string methodName, string body, out string result)
        {
            result = "";
            Mau.Runtime.ICSharpBridge bridge;
            Mau.Runtime.DataBox.TryResolve<Mau.Runtime.ICSharpBridge>(out bridge);
            if (bridge == null)
            {
                result = "ERR|CSHARP_NO_BRIDGE|宿主未注入 ICSharpBridge（Mau.Development.MauRoslynBridge）";
                return false;
            }
            string argsJson = "{\"class\":\"" + Safe(className) + "\",\"method\":\"" + Safe(methodName) + "\",\"body\":\"" + Safe(body) + "\"}";
            return bridge.Invoke("body_replace", argsJson, out result);
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
// #MAU_CHECKSUM:SHA256:B0CEEC3AEE6FF67047265E9DE5177CAF4360652260D2C3DC43D375AB15779208
