// ═══════════════════════════════════════════════════
// 积木: file.find
// ID:   BRIK-FILE-008
// 类别: FILE
// 作用: 按文件名通配符搜索并稳定排序
// 依赖: 无
// 引用: System
// 原理: 经 FileBridge 受控文件系统 Find → 换行拼接
// 常用: 找文件 / 按模式搜索
// ═══════════════════════════════════════════════════
using System;

using Mau.Runtime;
namespace Mau.Bricks
{
    /// <summary>
    /// 文件积木——file.find 搜索文件（依赖 FileBridge）
    /// </summary>
    public static class FileFindBrick
    {
        /// <summary>
        /// 按文件名通配符搜索并稳定排序
        /// </summary>
        /// <param name="directory">受控目录</param>
        /// <param name="pattern">文件名模式</param>
        /// <param name="recursive">是否递归</param>
        /// <param name="limit">最大结果</param>
        /// <param name="found">相对路径行——成功时填充</param>
        /// <returns>true=成功</returns>
        public static bool Find(string directory, string pattern, bool recursive, int limit, out string found)
        {
            try
            {
                string[] entries = FileBridge.CurrentFileSystem().Find(directory, pattern, recursive, limit);
                found = string.Join("\n", entries);
                return true;
            }
            catch (Exception ex)
            {
                found = "ERR|" + ex.GetType().Name + "|" + ex.Message;
                return false;
            }
        }
    }
}
// #MAU_CHECKSUM:SHA256:40D1230A37D05B9634F65B2F88211957FE351313DA9547BBAB00D605668EFB79
