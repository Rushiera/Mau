// ═══════════════════════════════════════════════════
// 积木: docx.read
// ID:   BRIK-OFFICE-003
// 类别: OFFICE
// 作用: 读取 .docx 文件，提取纯文本内容（PACK 调度——经 word.bridge）
// 依赖: 无
// 包: 无
// 引用: Mau.Runtime（IWordBridge）· System
// 原理: DataBox.TryResolve<IWordBridge> → Invoke("word.read", argsJson)
//       实现 = Mau.WorkApp.WorkAppBridge（OpenXml 封装——PACK 隔离）
// 常用: OfficeCat 工具 Cat——文档读取（PACK 协议）
// ═══════════════════════════════════════════════════
using System;

namespace Mau.Bricks
{
    /// <summary>
    /// Word 积木——docx.read 读取 .docx（PACK 调度 IWordBridge）
    /// </summary>
    public static class DocxReadBrick
    {
        /// <summary>
        /// 读取 .docx 文件，提取纯文本内容
        /// </summary>
        /// <param name="path">文件路径</param>
        /// <param name="content">纯文本内容</param>
        /// <returns>true=成功</returns>
        public static bool Read(string path, out string content)
        {
            content = "";
            Mau.Runtime.IWordBridge? bridge;
            Mau.Runtime.DataBox.TryResolve<Mau.Runtime.IWordBridge>(out bridge);
            if (bridge == null)
            {
                content = "ERR|WORD_NO_BRIDGE|宿主未注入 IWordBridge（Mau.WorkApp.WorkAppBridge）";
                return false;
            }
            string argsJson = "{\"path\":\"" + Safe(path) + "\"}";
            return bridge.Invoke("word.read", argsJson, out content);
        }

        /// <summary>
        /// JSON 字符串安全转义
        /// </summary>
        /// <param name="value">原始值</param>
        /// <returns>转义后</returns>
        private static string Safe(string value)
        {
            if (value == null) { return ""; }
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "");
        }
    }
}
// #MAU_CHECKSUM:SHA256:1F78F8F905C95471876EDF3B466E80A42649DC3452A5E9817915E6AAECF3990A
