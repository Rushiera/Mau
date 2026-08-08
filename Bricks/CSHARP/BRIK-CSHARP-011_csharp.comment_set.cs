// ═══════════════════════════════════════════════════
// 积木: csharp.comment_set
// ID:   BRIK-CSHARP-011
// 类别: CSHARP
// 作用: 设置 XML 注释——type: summary/param/returns；member 空=设类
// 依赖: 无
// 包: 无
// 引用: Mau.Runtime（ICSharpBridge）· System
// 原理: DataBox.TryResolve<ICSharpBridge> → Invoke("comment_set", argsJson)——PACK 协议
// 常用: CSharpCat 工具 Cat——注释维护（M2d.1）
// ═══════════════════════════════════════════════════
using System;

namespace Mau.Bricks
{
    /// <summary>
    /// C# 积木——csharp.comment_set XML 注释（调度 ICSharpBridge）
    /// </summary>
    public static class CSharpCommentSetBrick
    {
        /// <summary>
        /// 设置 XML 注释——summary/param/returns
        /// </summary>
        /// <param name="className">类名</param>
        /// <param name="memberName">成员名（空=类）</param>
        /// <param name="commentType">summary/param/returns</param>
        /// <param name="text">注释文本</param>
        /// <param name="paramName">type=param 时必填</param>
        /// <param name="result">结果 JSON</param>
        /// <returns>true=调用成功</returns>
        public static bool CommentSet(string className, string memberName, string commentType, string text, string paramName, out string result)
        {
            result = "";
            Mau.Runtime.ICSharpBridge bridge;
            Mau.Runtime.DataBox.TryResolve<Mau.Runtime.ICSharpBridge>(out bridge);
            if (bridge == null)
            {
                result = "ERR|CSHARP_NO_BRIDGE|宿主未注入 ICSharpBridge（Mau.Development.MauRoslynBridge）";
                return false;
            }
            string argsJson = "{\"class\":\"" + Safe(className) + "\",\"member\":\"" + Safe(memberName) + "\",\"type\":\"" + Safe(commentType) + "\",\"text\":\"" + Safe(text) + "\",\"param\":\"" + Safe(paramName) + "\"}";
            return bridge.Invoke("comment_set", argsJson, out result);
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
// #MAU_CHECKSUM:SHA256:EED12C06354FA6E4DF911D32CAEBD8B26F39AE40C6BE8134FFAE0294324B3FA8
