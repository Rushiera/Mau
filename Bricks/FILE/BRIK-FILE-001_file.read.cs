// ═══════════════════════════════════════════════════
// 积木: file.read
// ID:   BRIK-FILE-001
// 类别: FILE
// 作用: 读取 UTF-8 文本（受控路径）
// 依赖: 无
// 引用: Mau.Runtime（FileSystemService/DataBox）
// 原理: DataBox.TryResolve<FileSystemService> → ReadText(path)——白名单根边界内建
// 常用: CH4 第一轮 io_test_cat 语料——ReadText 执行体
// ═══════════════════════════════════════════════════
using System;

using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// 文件积木——file.read 读取文本（依赖 FileSystemService）
    /// </summary>
    public static class FileReadBrick
    {
        /// <summary>
        /// 读取 UTF-8 文本
        /// </summary>
        /// <param name="path">受控路径</param>
        /// <param name="content">完整文本——失败时携带 ERR| 错误文本</param>
        /// <returns>true=成功</returns>
        public static bool Read(string path, out string content)
        {
            try
            {
                // P2 猫级解析——当前猫上下文优先（直执面 AsyncLocal），回退全局
                FileSystemService? fs = FileSystemRegistry.ResolveCurrent();
                if (fs == null)
                {
                    DataBox.TryResolve<FileSystemService>(out fs);
                }
                if (fs == null)
                {
                    content = "ERR|FS_NO_SERVICE|宿主未注入 FileSystemService";
                    return false;
                }
                content = fs.ReadText(path);
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
// #MAU_CHECKSUM:SHA256:EDCEDBD52E284324889964E20DA88E8880C348CFBBD68F8A5839D74503CB70E2
