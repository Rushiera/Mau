// ═══════════════════════════════════════════════════
// 积木: file.delete
// ID:   BRIK-FILE-010
// 类别: FILE
// 作用: 软删除到受控回收站
// 依赖: 无
// 引用: System
// 原理: 经 FileBridge 受控文件系统 Recycle
// 常用: 可恢复删除
// ═══════════════════════════════════════════════════
using System;

using Mau.Runtime;
namespace Mau.Bricks
{
    /// <summary>
    /// 文件积木——file.delete 软删除（依赖 FileBridge）
    /// </summary>
    public static class FileDeleteBrick
    {
        /// <summary>
        /// 软删除到受控回收站
        /// </summary>
        /// <param name="path">目标路径</param>
        /// <param name="recycled">回收站内新路径——成功时填充</param>
        /// <returns>true=成功</returns>
        public static bool Delete(string path, out string recycled)
        {
            try
            {
                recycled = FileBridge.CurrentFileSystem().Recycle(path);
                return true;
            }
            catch (Exception ex)
            {
                recycled = "ERR|" + ex.GetType().Name + "|" + ex.Message;
                return false;
            }
        }
    }
}
// #MAU_CHECKSUM:SHA256:E56013E53E37CB6F92B9C24F0DEF4D4421FBAC6D1EB2FF0099C905867EBF3E30
