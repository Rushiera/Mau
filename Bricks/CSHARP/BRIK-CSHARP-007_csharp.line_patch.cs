// ═══════════════════════════════════════════════════
// 积木: csharp.line_patch
// ID:   BRIK-CSHARP-007
// 类别: CSHARP
// 作用: 替换方法内指定行范围——行号 1-based 与 read 的 LN 对齐（编译通过才写盘）
// 依赖: 无
// 包: 无
// 引用: Mau.Runtime（ICSharpBridge）· System
// 原理: DataBox.TryResolve<ICSharpBridge> → Invoke("line_patch", argsJson)——PACK 协议
// 常用: CSharpCat 工具 Cat——行级修复（M2d.1）
// ═══════════════════════════════════════════════════
using System;

namespace Mau.Bricks
{
    /// <summary>
    /// C# 积木——csharp.line_patch 行范围替换（调度 ICSharpBridge）
    /// </summary>
    public static class CSharpLinePatchBrick
    {
        /// <summary>
        /// 替换方法内指定行范围——1-based
        /// </summary>
        /// <param name="className">类名</param>
        /// <param name="methodName">方法名</param>
        /// <param name="startLine">起始行（方法内）</param>
        /// <param name="endLine">结束行（方法内）</param>
        /// <param name="newText">替换文本</param>
        /// <param name="result">结果 JSON</param>
        /// <returns>true=调用成功</returns>
        public static bool LinePatch(string className, string methodName, int startLine, int endLine, string newText, out string result)
        {
            result = "";
            Mau.Runtime.ICSharpBridge bridge;
            Mau.Runtime.DataBox.TryResolve<Mau.Runtime.ICSharpBridge>(out bridge);
            if (bridge == null)
            {
                result = "ERR|CSHARP_NO_BRIDGE|宿主未注入 ICSharpBridge（Mau.Development.MauRoslynBridge）";
                return false;
            }
            string argsJson = "{\"class\":\"" + Safe(className) + "\",\"method\":\"" + Safe(methodName) + "\",\"startLine\":" + startLine + ",\"endLine\":" + endLine + ",\"newText\":\"" + Safe(newText) + "\"}";
            return bridge.Invoke("line_patch", argsJson, out result);
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
// #MAU_CHECKSUM:SHA256:898BA7EBEBA68AE3225F00334BA877B4E3FB0F3233B9AB178CE86497006AB0E1
