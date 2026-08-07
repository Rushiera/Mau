// ═══════════════════════════════════════════════════
// 积木: file.tree
// ID:   BRIK-FILE-007
// 类别: FILE
// 作用: 按深度和数量上限列出稳定排序目录树
// 依赖: 无
// 引用: System
// 原理: 经 FileBridge 受控文件系统 Tree → 换行拼接
// 常用: 看结构 / 查文件
// ═══════════════════════════════════════════════════
using System;

using Mau.Runtime;
namespace Mau.Bricks
{
    /// <summary>
    /// 文件积木——file.tree 列出目录树（依赖 FileBridge）
    /// </summary>
    public static class FileTreeBrick
    {
        /// <summary>
        /// 按深度和数量上限列出稳定排序目录树
        /// </summary>
        /// <param name="path">受控目录</param>
        /// <param name="depth">零到十层</param>
        /// <param name="limit">最大条数</param>
        /// <param name="tree">相对路径行——成功时填充</param>
        /// <returns>true=成功</returns>
        public static bool Tree(string path, int depth, int limit, out string tree)
        {
            try
            {
                string[] entries = FileBridge.CurrentFileSystem().Tree(path, depth, limit);
                tree = string.Join("\n", entries);
                return true;
            }
            catch (Exception ex)
            {
                tree = "ERR|" + ex.GetType().Name + "|" + ex.Message;
                return false;
            }
        }
    }
}
// #MAU_CHECKSUM:SHA256:B8ADC9D79C617989738CE12A839059A18838B4FBF21F9EBE14D972F93EC165C9
