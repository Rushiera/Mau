// ═══════════════════════════════════════════════════
// 积木: csharp.bridge
// ID:   BRIK-PACK-003
// 类别: PACK
// 作用: C# 工具桥接口积木——Roslyn 能力声明（PACK 类，csharp.* 积木统一经本桥访问）
// 依赖: 无
// 包: Microsoft.CodeAnalysis.CSharp@4.11.0
// 引用: Mau.Runtime（ICSharpBridge）· System
// 原理: DataBox.TryResolve<ICSharpBridge> → Invoke(method, argsJson, out result)
//       实现 = Mau.Development.MauRoslynBridge（Mau 编译链必要内部支持——唯一常驻 PACK）
// 方法: init → csproj
//        info →
//        list → class
//        read → class,member
//        compile → full
//        body_replace → class,method,body
//        line_patch → class,method,startLine,endLine,newText
//        line_insert → class,method,afterLine,newText
//        member_insert → class,position,anchor,code
//        member_delete → class,member
//        comment_set → class,member,type,text,param
//        comment_check →
//        member_rename → class,oldName,newName
//        dead →
//        find_ref → class,member
// 常用: csharp.* 十五积木的调度底座（PACK 协议）
// ═══════════════════════════════════════════════════
using System;

namespace Mau.Bricks
{
    /// <summary>
    /// PACK 接口积木——csharp.bridge（调度 ICSharpBridge）
    /// </summary>
    public static class CSharpBridgeBrick
    {
        /// <summary>
        /// C# 工具桥单方法调度——method 白名单 + argsJson 展平参数
        /// </summary>
        /// <param name="method">操作名（init/info/list/read/compile/...）</param>
        /// <param name="argsJson">展平参数 JSON</param>
        /// <param name="result">结果文本</param>
        /// <returns>true=调用成功</returns>
        public static bool Invoke(string method, string argsJson, out string result)
        {
            result = "";
            Mau.Runtime.ICSharpBridge? bridge;
            Mau.Runtime.DataBox.TryResolve<Mau.Runtime.ICSharpBridge>(out bridge);
            if (bridge == null)
            {
                result = "ERR|CSHARP_NO_BRIDGE|宿主未注入 ICSharpBridge（Mau.Development.MauRoslynBridge）";
                return false;
            }
            return bridge.Invoke(method, argsJson, out result);
        }
    }
}
// #MAU_CHECKSUM:SHA256:EF6A5EAC2DBE7377AF03647F2391E9850411C67B6E0F556B64CC3787695F5D91
