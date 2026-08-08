// ═══════════════════════════════════════════════════
// 积木: csharp.dead
// ID:   BRIK-CSHARP-014
// 类别: CSHARP
// 作用: 扫描全项目零引用 private/internal 成员——跳过 public 和构造函数
// 依赖: 无
// 包: Microsoft.CodeAnalysis.Workspaces.MSBuild@4.11.0
// 引用: Mau.Runtime（ICSharpBridge）· System
// 原理: DataBox 解析 ICSharpBridge → DeadCode（SymbolFinder 语义引用）
// 常用: CSharpCat 工具 Cat——死代码清理（M2d.1）
// ═══════════════════════════════════════════════════
using System;

namespace Mau.Bricks
{
    /// <summary>
    /// C# 积木——csharp.dead 死代码扫描（调度 ICSharpBridge）
    /// </summary>
    public static class CSharpDeadBrick
    {
        /// <summary>
        /// 扫描零引用 private/internal 成员
        /// </summary>
        /// <param name="result">清单文本</param>
        /// <returns>true=调用成功</returns>
        public static bool Dead(out string result)
        {
            result = "";
            Mau.Runtime.ICSharpBridge bridge;
            Mau.Runtime.DataBox.TryResolve<Mau.Runtime.ICSharpBridge>(out bridge);
            if (bridge == null)
            {
                result = "ERR|CSHARP_NO_BRIDGE|宿主未注入 ICSharpBridge（Mau.Development.MauRoslynBridge）";
                return false;
            }
            result = bridge.DeadCode();
            return true;
        }
    }
}
// #MAU_CHECKSUM:SHA256:0C15620A5416166163CAEF876452EC0A55B9EBB736B93616427173EB98E7CCE7
