// ═══════════════════════════════════════════════════
// 积木: file.read
// ID:   BRIK-FILE-002
// 类别: FILE
// 作用: 读取 UTF-8 文本（受控路径）
// 依赖: 无
// 引用: System
// 原理: 经 FileBridge 受控文件系统 ReadText——白名单边界内置
// 常用: CH4 IO 工具组 / 任意文件读取场景
// ═══════════════════════════════════════════════════
using System;

using Mau.Runtime;
namespace Mau.Bricks
{
    /// <summary>
    /// 文件积木——file.read 读取文本（依赖 FileBridge）
    /// </summary>
    public static class FileReadBrick
    {
        /// <summary>
        /// 读取 UTF-8 文本
        /// </summary>
        /// <param name="path">受控路径</param>
        /// <param name="content">完整文本——成功时填充</param>
        /// <returns>true=成功</returns>
        public static bool Read(string path, out string content)
        {
            try
            {
                content = FileBridge.CurrentFileSystem().ReadText(path);
                return true;
            }
            catch (Exception ex)
            {
                content = "ERR|" + ex.GetType().Name + "|" + ex.Message;
                return false;
            }
        }
    }
}
// #MAU_CHECKSUM:SHA256:7D1FB216430A6EFD579485DC3B033A2E4C1BD0DF99E8AAB692B16BA6DF501146
