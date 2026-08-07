// ═══════════════════════════════════════════════════
// 积木: file.read_lines
// ID:   BRIK-FILE-006
// 类别: FILE
// 作用: 按一基闭区间读取文本行（带行号）
// 依赖: 无
// 引用: System
// 原理: 经 FileBridge 受控文件系统 ReadLines
// 常用: 代码定位 / 区间读取
// ═══════════════════════════════════════════════════
using System;

using Mau.Runtime;
namespace Mau.Bricks
{
    /// <summary>
    /// 文件积木——file.read_lines 按区间读行（依赖 FileBridge）
    /// </summary>
    public static class FileReadLinesBrick
    {
        /// <summary>
        /// 按一基闭区间读取文本行（带行号）
        /// </summary>
        /// <param name="path">受控路径</param>
        /// <param name="startLine">起始行（1-based）</param>
        /// <param name="endLine">结束行；0=文件尾</param>
        /// <param name="lines">带行号文本——成功时填充</param>
        /// <returns>true=成功</returns>
        public static bool ReadLines(string path, int startLine, int endLine, out string lines)
        {
            try
            {
                lines = FileBridge.CurrentFileSystem().ReadLines(path, startLine, endLine);
                return true;
            }
            catch (Exception ex)
            {
                lines = "ERR|" + ex.GetType().Name + "|" + ex.Message;
                return false;
            }
        }
    }
}
// #MAU_CHECKSUM:SHA256:8C0F19239689D18F4B3B75489A56ECD2E280A8DDDF062648350ECC2CEB81917F
