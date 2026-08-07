// ═══════════════════════════════════════════════════
// 积木: file.append
// ID:   BRIK-FILE-004
// 类别: FILE
// 作用: 追加 UTF-8 文本并创建父目录（受控路径）
// 依赖: 无
// 引用: 无
// 原理: 经 FileBridge 受控文件系统 AppendText
// 常用: 日志追加 / 增量记录
// ═══════════════════════════════════════════════════

using Mau.Runtime;
namespace Mau.Bricks
{
    /// <summary>
    /// 文件积木——file.append 追加文本（依赖 FileBridge）
    /// </summary>
    public static class FileAppendBrick
    {
        /// <summary>
        /// 追加 UTF-8 文本并创建父目录
        /// </summary>
        /// <param name="path">受控路径</param>
        /// <param name="content">追加正文</param>
        /// <returns>true=成功</returns>
        public static bool Append(string path, string content)
        {
            try
            {
                FileBridge.CurrentFileSystem().AppendText(path, content);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
// #MAU_CHECKSUM:SHA256:2849D2EC873ED658AF0B1CD3E85A53932F8DC17636B4608657994FB1FC5B7482
