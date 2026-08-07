// ═══════════════════════════════════════════════════
// 积木: file.replace
// ID:   BRIK-FILE-005
// 类别: FILE
// 作用: 替换全部精确文本并原子写回（受控路径）
// 依赖: 无
// 引用: 无
// 原理: 经 FileBridge 受控文件系统 ReplaceText——0 次替换也算成功
// 常用: 文本修订 / 批量替换
// ═══════════════════════════════════════════════════

using Mau.Runtime;
namespace Mau.Bricks
{
    /// <summary>
    /// 文件积木——file.replace 替换文本（依赖 FileBridge）
    /// </summary>
    public static class FileReplaceBrick
    {
        /// <summary>
        /// 替换全部精确文本并原子写回
        /// </summary>
        /// <param name="path">受控路径</param>
        /// <param name="oldText">非空目标</param>
        /// <param name="newText">新文本</param>
        /// <returns>true=成功（0 次替换也算成功）</returns>
        public static bool Replace(string path, string oldText, string newText)
        {
            try
            {
                FileBridge.CurrentFileSystem().ReplaceText(path, oldText, newText);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
// #MAU_CHECKSUM:SHA256:E5EBE3B2EE06DB33BC6DF1022957FD12B27301B72A6722A6C37B11826FE7E457
