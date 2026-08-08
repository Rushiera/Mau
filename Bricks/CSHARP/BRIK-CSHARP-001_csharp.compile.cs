// ═══════════════════════════════════════════════════
// 积木: csharp.compile
// ID:   BRIK-CSHARP-001
// 类别: CSHARP
// 作用: 编译项目——默认仅返回错误/警告计数；full=true 返回完整诊断列表
// 依赖: 无
// 包: 无
// 引用: Mau.Runtime（ICSharpBridge）· System
// 原理: DataBox.TryResolve<ICSharpBridge> → Invoke("compile", argsJson)
//       经 csharp.bridge（BRIK-PACK-003）调度——PACK 协议
// 常用: CSharpCat 工具 Cat——编译验证（M2d.1 照搬 CH2 csharpcode_compile）
// ═══════════════════════════════════════════════════
using System;

namespace Mau.Bricks
{
    /// <summary>
    /// C# 积木——csharp.compile 项目编译（PACK 调度 ICSharpBridge）
    /// </summary>
    public static class CSharpCompileBrick
    {
        /// <summary>
        /// 编译项目——full=true 返回完整诊断
        /// </summary>
        /// <param name="full">true=完整诊断</param>
        /// <param name="result">结果 JSON</param>
        /// <returns>true=调用成功</returns>
        public static bool Compile(bool full, out string result)
        {
            result = "";
            Mau.Runtime.ICSharpBridge? bridge;
            Mau.Runtime.DataBox.TryResolve<Mau.Runtime.ICSharpBridge>(out bridge);
            if (bridge == null)
            {
                result = "ERR|CSHARP_NO_BRIDGE|宿主未注入 ICSharpBridge（Mau.Development.MauRoslynBridge）";
                return false;
            }
            string argsJson = "{\"full\":" + (full ? "true" : "false") + "}";
            return bridge.Invoke("compile", argsJson, out result);
        }
    }
}
// #MAU_CHECKSUM:SHA256:46D439626C18DB916207E0AFE42980A32364788E7AC961E64B372AD37246B3A4
