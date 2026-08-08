// ═══════════════════════════════════════════════════
// 积木: csharp.init
// ID:   BRIK-CSHARP-002
// 类别: CSHARP
// 作用: 绑定 csproj 项目——激活 C# 工具组（多树隔离：每 csproj 一棵工作区树）
// 依赖: 无
// 包: 无
// 引用: Mau.Runtime（ICSharpBridge）· System
// 原理: DataBox.TryResolve<ICSharpBridge> → Invoke("init", argsJson)——PACK 协议
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
            string safe = csproj == null ? "" : csproj.Replace("\\", "\\\\").Replace("\"", "\\\"");
            string argsJson = "{\"csproj\":\"" + safe + "\"}";
            return bridge.Invoke("init", argsJson, out result);
        }
    }
}
// #MAU_CHECKSUM:SHA256:3D08AE7E6E93F894A1BF04E0C0AB7E1D7BECEE7CB0BDD3250BBC98364E9104AB
