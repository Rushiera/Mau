// ═══════════════════════════════════════════════════
// 积木: csharp.list
// ID:   BRIK-CSHARP-004
// 类别: CSHARP
// 作用: 列出类/成员签名——class 空=全项目类名；指定类=成员列表+注释摘要
// 依赖: 无
// 包: 无
// 引用: Mau.Runtime（ICSharpBridge）· System
// 原理: DataBox.TryResolve<ICSharpBridge> → Invoke("list", argsJson)——PACK 协议
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
            string safe = className == null ? "" : className.Replace("\\", "\\\\").Replace("\"", "\\\"");
            string argsJson = "{\"class\":\"" + safe + "\"}";
            return bridge.Invoke("list", argsJson, out result);
        }
    }
}
// #MAU_CHECKSUM:SHA256:AFAC140C915171AD5665C294456146617FC06778CCCD0FB85C07BB732DF17372
