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
// 方法: check → path,full
//        build → path
//        list → path,class
//        read → path,class,member
//        find_ref → path,class,member
//        patch → path,class,method,body
//        member → path,class,op,position,anchor,code,oldName,newName
//        comment → path,class,member,type,text,param
//        dead → path
//        comment_check → path
// 常用: cs.* 十工具的调度底座（P8 三期——15→9 域裁剪：砍 init/info；patch 合并三改法；member 合并三操作；comment 合并两操作；comment_check 后补）
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
        /// <param name="method">操作名（check/build/list/read/find_ref/patch/member/comment/dead/comment_check）</param>
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
// #MAU_CHECKSUM:SHA256:084DDD13F1E46CAA18A000A70E409ADC6477545AC393F15F1FFB72131F281CEA
