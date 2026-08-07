// ═══════════════════════════════════════════════════
// 积木: file.move
// ID:   BRIK-FILE-009
// 类别: FILE
// 作用: 移动文件且拒绝覆盖目标
// 依赖: 无
// 引用: 无
// 原理: 经 FileBridge 受控文件系统 Move
// 常用: 整理路径 / 重命名
// ═══════════════════════════════════════════════════

using Mau.Runtime;
namespace Mau.Bricks
{
    /// <summary>
    /// 文件积木——file.move 移动文件（依赖 FileBridge）
    /// </summary>
    public static class FileMoveBrick
    {
        /// <summary>
        /// 移动文件且拒绝覆盖目标
        /// </summary>
        /// <param name="source">源路径</param>
        /// <param name="destination">目标路径</param>
        /// <returns>true=成功</returns>
        public static bool Move(string source, string destination)
        {
            try
            {
                FileBridge.CurrentFileSystem().Move(source, destination);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
// #MAU_CHECKSUM:SHA256:4FD17FFD1AD10EDFBF64B9F34D0D06812158E0ACE3DFC95CA04B23072FA3B790
