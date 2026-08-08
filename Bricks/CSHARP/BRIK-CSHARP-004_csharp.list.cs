// ═══════════════════════════════════════════════════
// 积木: csharp.list
// ID:   BRIK-CSHARP-004
// 类别: CSHARP
// 作用: 列出类/成员签名——class 空=全项目类名；指定类=成员列表+注释摘要
// 依赖: 无
// 包: Microsoft.CodeAnalysis.Workspaces.MSBuild@4.11.0
// 引用: Mau.Runtime（ICSharpBridge）· System
// 原理: DataBox 解析 ICSharpBridge → ListMembers(className)
// 常用: CSharpCat 工具 Cat——结构浏览（M2d.1）
// ═══════════════════════════════════════════════════
using System;

namespace Mau.Bricks
{
    /// <summary>
    /// C# 积木——csharp.list 类/成员清单（调度 ICSharpBridge）
    /// </summary>
    public static class CSharpListBrick
    {
        /// <summary>
        /// 列出类/成员——class 空=全项目类名
        /// </summary>
        /// <param name="className">类名（空=全项目）</param>
        /// <param name="result">清单文本</param>
        /// <returns>true=调用成功</returns>
        public static bool List(string className, out string result)
        {
            result = "";
            Mau.Runtime.ICSharpBridge bridge;
            Mau.Runtime.DataBox.TryResolve<Mau.Runtime.ICSharpBridge>(out bridge);
            if (bridge == null)
            {
                result = "ERR|CSHARP_NO_BRIDGE|宿主未注入 ICSharpBridge（Mau.Development.MauRoslynBridge）";
                return false;
            }
            result = bridge.ListMembers(className == null ? "" : className);
            return true;
        }
    }
}
// #MAU_CHECKSUM:SHA256:81647E300216842552C2DA1C64C3EE4A2E78F127704CE29FE26DBAFB7E9F3CB7
