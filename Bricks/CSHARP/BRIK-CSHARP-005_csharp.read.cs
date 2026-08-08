// ═══════════════════════════════════════════════════
// 积木: csharp.read
// ID:   BRIK-CSHARP-005
// 类别: CSHARP
// 作用: 读取成员源码——含 XML 注释 + 方法内行号标注（// LN）；member 空=类概览
// 依赖: 无
// 包: Microsoft.CodeAnalysis.Workspaces.MSBuild@4.11.0
// 引用: Mau.Runtime（ICSharpBridge）· System
// 原理: DataBox 解析 ICSharpBridge → ReadMember(className, memberName)
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
            result = bridge.ReadMember(className, memberName == null ? "" : memberName);
            return true;
        }
    }
}
// #MAU_CHECKSUM:SHA256:16B70B586DBDAD05DEDF928FACEEA2AF9F7940B9F0746D44E6069FBF7F3ACB00
