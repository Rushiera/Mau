// ═══════════════════════════════════════════════════
// 积木: csharp.line_insert
// ID:   BRIK-CSHARP-008
// 类别: CSHARP
// 作用: 在方法内指定行后插入——afterLine=0=body 头（编译通过才写盘）
// 依赖: 无
// 包: Microsoft.CodeAnalysis.Workspaces.MSBuild@4.11.0
// 引用: Mau.Runtime（ICSharpBridge）· System
// 原理: DataBox 解析 ICSharpBridge → LineInsert（行号越界保护）
// 常用: CSharpCat 工具 Cat——行级插入（M2d.1）
// ═══════════════════════════════════════════════════
using System;

namespace Mau.Bricks
{
    /// <summary>
    /// C# 积木——csharp.line_insert 行插入（调度 ICSharpBridge）
    /// </summary>
    public static class CSharpLineInsertBrick
    {
        /// <summary>
        /// 在方法内指定行后插入——0=body 头
        /// </summary>
        /// <param name="className">类名</param>
        /// <param name="methodName">方法名</param>
        /// <param name="afterLine">插入位置（方法内行号，0=body头）</param>
        /// <param name="newText">插入文本</param>
        /// <param name="result">结果 JSON</param>
        /// <returns>true=调用成功</returns>
        public static bool LineInsert(string className, string methodName, int afterLine, string newText, out string result)
        {
            result = "";
            Mau.Runtime.ICSharpBridge bridge;
            Mau.Runtime.DataBox.TryResolve<Mau.Runtime.ICSharpBridge>(out bridge);
            if (bridge == null)
            {
                result = "ERR|CSHARP_NO_BRIDGE|宿主未注入 ICSharpBridge（Mau.Development.MauRoslynBridge）";
                return false;
            }
            result = bridge.LineInsert(className, methodName, afterLine, newText == null ? "" : newText);
            return true;
        }
    }
}
// #MAU_CHECKSUM:SHA256:30505146D5FE1F09F055AB2B927D65FD7CC173744375EAEA659529CE06EEF39E
