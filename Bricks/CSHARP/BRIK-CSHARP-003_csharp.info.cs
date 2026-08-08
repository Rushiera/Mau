// ═══════════════════════════════════════════════════
// 积木: csharp.info
// ID:   BRIK-CSHARP-003
// 类别: CSHARP
// 作用: 查询当前 C# 工具组绑定状态——项目名/csproj/文档数/类数
// 依赖: 无
// 包: Microsoft.CodeAnalysis.Workspaces.MSBuild@4.11.0
// 引用: Mau.Runtime（ICSharpBridge）· System
// 原理: DataBox 解析 ICSharpBridge → GetInfo()
// 常用: CSharpCat 工具 Cat——绑定状态查询（M2d.1）
// ═══════════════════════════════════════════════════
using System;

namespace Mau.Bricks
{
    /// <summary>
    /// C# 积木——csharp.info 绑定状态（调度 ICSharpBridge）
    /// </summary>
    public static class CSharpInfoBrick
    {
        /// <summary>
        /// 查询绑定状态——项目名/csproj/文档数/类数
        /// </summary>
        /// <param name="result">结果 JSON</param>
        /// <returns>true=调用成功</returns>
        public static bool Info(out string result)
        {
            result = "";
            Mau.Runtime.ICSharpBridge bridge;
            Mau.Runtime.DataBox.TryResolve<Mau.Runtime.ICSharpBridge>(out bridge);
            if (bridge == null)
            {
                result = "ERR|CSHARP_NO_BRIDGE|宿主未注入 ICSharpBridge（Mau.Development.MauRoslynBridge）";
                return false;
            }
            result = bridge.GetInfo();
            return true;
        }
    }
}
// #MAU_CHECKSUM:SHA256:83C95DB25F20DA16AC87156CB05D065948F56D5BD0434B02447D833F1E10E54A
