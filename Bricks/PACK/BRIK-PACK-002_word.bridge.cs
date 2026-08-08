// ═══════════════════════════════════════════════════
// 积木: word.bridge
// ID:   BRIK-PACK-002
// 类别: PACK
// 作用: Word 桥接口积木——外部包能力声明（PACK 类，普通积木只经本桥访问 Word）
// 依赖: 无
// 包: 无
// 引用: Mau.Runtime（IWordBridge）· System
// 原理: DataBox.TryResolve<IWordBridge> → Invoke(method, argsJson, out result)
//       实现 = Mau.WorkApp.WorkAppBridge（DocumentFormat.OpenXml 封装，依赖者宿主选装 Bind）
// 方法: word.read → path
//        word.write → path,content
// 常用: docx.read/write 积木的调度底座（PACK 协议——外部包与积木解耦）
// ═══════════════════════════════════════════════════
using System;

namespace Mau.Bricks
{
    /// <summary>
    /// PACK 接口积木——word.bridge（调度 IWordBridge）
    /// </summary>
    public static class WordBridgeBrick
    {
        /// <summary>
        /// Word 桥单方法调度——method 白名单 + argsJson 展平参数
        /// </summary>
        /// <param name="method">操作名（word.read/word.write）</param>
        /// <param name="argsJson">展平参数 JSON</param>
        /// <param name="result">结果文本</param>
        /// <returns>true=调用成功</returns>
        public static bool Invoke(string method, string argsJson, out string result)
        {
            result = "";
            Mau.Runtime.IWordBridge? bridge;
            Mau.Runtime.DataBox.TryResolve<Mau.Runtime.IWordBridge>(out bridge);
            if (bridge == null)
            {
                result = "ERR|WORD_NO_BRIDGE|宿主未注入 IWordBridge（Mau.WorkApp.WorkAppBridge）";
                return false;
            }
            return bridge.Invoke(method, argsJson, out result);
        }
    }
}
// #MAU_CHECKSUM:SHA256:E67FF36D016D75973F946AB03E5EC6B83CD8DD3D80D8D5A3DD5E96A6BE47B15E
