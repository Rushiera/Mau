// ═══════════════════════════════════════════════════
// 积木: docx.write
// ID:   BRIK-OFFICE-004
// 类别: OFFICE
// 作用: 将纯文本写入 .docx 文件（PACK 调度——经 word.bridge）
// 依赖: 无
// 包: 无
// 引用: Mau.Runtime（IWordBridge）· System
// 原理: DataBox.TryResolve<IWordBridge> → Invoke("word.write", argsJson)
//       实现 = Mau.WorkApp.WorkAppBridge（OpenXml 封装——PACK 隔离）
// 常用: OfficeCat 工具 Cat——报告生成（PACK 协议）
// ═══════════════════════════════════════════════════
using System;

namespace Mau.Bricks
{
    /// <summary>
    /// Word 积木——docx.write 写入 .docx（PACK 调度 IWordBridge）
    /// </summary>
    public static class DocxWriteBrick
    {
        /// <summary>
        /// 将纯文本写入 .docx 文件（覆盖已有文件）
        /// </summary>
        /// <param name="path">文件路径</param>
        /// <param name="content">纯文本，换行符分割段落</param>
        /// <param name="result">结果描述</param>
        /// <returns>true=成功</returns>
        public static bool Write(string path, string content, out string result)
        {
            result = "";
            Mau.Runtime.IWordBridge? bridge;
            Mau.Runtime.DataBox.TryResolve<Mau.Runtime.IWordBridge>(out bridge);
            if (bridge == null)
            {
                result = "ERR|WORD_NO_BRIDGE|宿主未注入 IWordBridge（Mau.WorkApp.WorkAppBridge）";
                return false;
            }
            string argsJson = "{\"path\":\"" + Safe(path) + "\",\"content\":\"" + Safe(content) + "\"}";
            return bridge.Invoke("word.write", argsJson, out result);
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
// #MAU_CHECKSUM:SHA256:213341B942F292670172DF7280D38E922437A5BBFF35E71FB8A6AB4D802DAA70
