// ═══════════════════════════════════════════════════
// 积木: csharp.read
// ID:   BRIK-CSHARP-005
// 类别: CSHARP
// 作用: 读取成员源码——含 XML 注释 + 方法内行号标注（// LN）；member 空=类概览
// 依赖: 无
// 包: 无
// 引用: Mau.Runtime（ICSharpBridge）· System
// 原理: DataBox.TryResolve<ICSharpBridge> → Invoke("read", argsJson)——PACK 协议
// 常用: CSharpCat 工具 Cat——源码阅读（M2d.1）
// ═══════════════════════════════════════════════════
using System;

namespace Mau.Bricks
{
    /// <summary>
    /// C# 积木——csharp.read 成员源码（调度 ICSharpBridge）
    /// </summary>
    public static class CSharpReadBrick
    {
        /// <summary>
        /// 读取成员源码——含 XML 注释 + 行号
        /// </summary>
        /// <param name="className">类名</param>
        /// <param name="memberName">成员名（空=类概览）</param>
        /// <param name="result">源码文本</param>
        /// <returns>true=调用成功</returns>
        public static bool Read(string className, string memberName, out string result)
        {
            result = "";
            Mau.Runtime.ICSharpBridge bridge;
            Mau.Runtime.DataBox.TryResolve<Mau.Runtime.ICSharpBridge>(out bridge);
            if (bridge == null)
            {
                result = "ERR|CSHARP_NO_BRIDGE|宿主未注入 ICSharpBridge（Mau.Development.MauRoslynBridge）";
                return false;
            }
            string safeClass = className == null ? "" : className.Replace("\\", "\\\\").Replace("\"", "\\\"");
            string safeMember = memberName == null ? "" : memberName.Replace("\\", "\\\\").Replace("\"", "\\\"");
            string argsJson = "{\"class\":\"" + safeClass + "\",\"member\":\"" + safeMember + "\"}";
            return bridge.Invoke("read", argsJson, out result);
        }
    }
}
// #MAU_CHECKSUM:SHA256:D30E04A11148DDBF5715E7AE6C86C1767F666AF0AA5560146636565D14690763
