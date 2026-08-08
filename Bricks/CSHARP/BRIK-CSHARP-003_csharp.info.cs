// ═══════════════════════════════════════════════════
// 积木: csharp.info
// ID:   BRIK-CSHARP-003
// 类别: CSHARP
// 作用: 查询当前 C# 工具组绑定状态——项目名/csproj/文档数/类数
// 依赖: 无
// 包: 无
// 引用: Mau.Runtime（ICSharpBridge）· System
// 原理: DataBox.TryResolve<ICSharpBridge> → Invoke("info", "{}")——PACK 协议
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
            return bridge.Invoke("info", "{}", out result);
        }
    }
}
// #MAU_CHECKSUM:SHA256:374531AC903DCEE6F0F1894C9CE57A0F0567BFA533A3A65209D8EEC80EA065CA
