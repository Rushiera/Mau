// ═══════════════════════════════════════════════════
// 积木: file.convert
// ID:   BRIK-FILE-001
// 类别: FILE
// 作用: 转换文件——读取→转码→写入（第一期转码=原样写出）
// 依赖: 无
// 引用: System
// 原理: ReadAllBytes → WriteAllBytes——编码转换算法留积木内部后续实现
// 常用: 文件格式转换
// ═══════════════════════════════════════════════════
using System;

using Mau.Runtime;
namespace Mau.Bricks
{
    /// <summary>
    /// 文件积木——file.convert 转换文件（独立实现，不走受控根）
    /// </summary>
    public static class FileConvertBrick
    {
        /// <summary>
        /// 转换文件——真实实现（读取→转码→写入）
        /// </summary>
        /// <param name="input">输入文件路径</param>
        /// <param name="output">输出文件路径</param>
        /// <returns>转换是否成功</returns>
        public static bool Convert(string input, string output)
        {
            try
            {
                byte[] bytes = System.IO.File.ReadAllBytes(input);
                // 第一期：转码 = 读取后原样写出——编码转换算法留积木内部后续实现
                System.IO.File.WriteAllBytes(output, bytes);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
// #MAU_CHECKSUM:SHA256:DC76C91967A32FAA66253BDB3D8BFE300D6770E69341F528E135B50A30DAD4A5
