// ═══════════════════════════════════════════════════
// 积木: file.write
// ID:   BRIK-FILE-003
// 类别: FILE
// 作用: 原子覆写 UTF-8 文本（受控路径）
// 依赖: 无
// 引用: 无
// 原理: 经 FileBridge 受控文件系统 WriteText——原子写内置
// 常用: CH4 IO 工具组 / 文件写入
// ═══════════════════════════════════════════════════

using Mau.Runtime;
namespace Mau.Bricks
{
    /// <summary>
    /// 文件积木——file.write 原子覆写（依赖 FileBridge）
    /// </summary>
    public static class FileWriteBrick
    {
        /// <summary>
        /// 原子覆写 UTF-8 文本
        /// </summary>
        /// <param name="path">受控路径</param>
        /// <param name="content">完整正文</param>
        /// <returns>true=成功</returns>
        public static bool Write(string path, string content)
        {
            try
            {
                FileBridge.CurrentFileSystem().WriteText(path, content);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
// #MAU_CHECKSUM:SHA256:6E404A18CE9290E84FDEC0069CD08E545BECF94A5C4B599F5A3E48AB8E1F36EB
