// ═══════════════════════════════════════════════════
// 积木: csharp.init
// ID:   BRIK-CSHARP-002
// 类别: CSHARP
// 作用: 绑定 csproj 项目——激活 C# 工具组（多树隔离：每 csproj 一棵工作区树）
// 依赖: 无
// 包: Microsoft.CodeAnalysis.Workspaces.MSBuild@4.11.0
// 引用: Mau.Runtime（ICSharpBridge）· System
// 原理: DataBox 解析 ICSharpBridge → Init(csprojPath)（实现= MauRoslynBridge）
// 常用: CSharpCat 工具 Cat——C# 项目绑定（M2d.1 照搬 CH2 csharpcode 体系）
// ═══════════════════════════════════════════════════
using System;

namespace Mau.Bricks
{
    /// <summary>
    /// C# 积木——csharp.init 绑定项目（调度 ICSharpBridge）
    /// </summary>
    public static class CSharpInitBrick
    {
        /// <summary>
        /// 绑定 csproj 项目——返回项目名/文件数/初始编译诊断
        /// </summary>
        /// <param name="csproj">.csproj 路径</param>
        /// <param name="result">结果 JSON</param>
        /// <returns>true=调用成功</returns>
        public static bool Init(string csproj, out string result)
        {
            result = "";
            Mau.Runtime.ICSharpBridge bridge;
            Mau.Runtime.DataBox.TryResolve<Mau.Runtime.ICSharpBridge>(out bridge);
            if (bridge == null)
            {
                result = "ERR|CSHARP_NO_BRIDGE|宿主未注入 ICSharpBridge（Mau.Development.MauRoslynBridge）";
                return false;
            }
            result = bridge.Init(csproj);
            return true;
        }
    }
}
// #MAU_CHECKSUM:SHA256:EE3C591BA3579DB5F508E9E77F4F2D7AD2CE22E4CEC4CB91FCC5BC3E96CA6B08
