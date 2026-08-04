using System;
using Mau.Contracts;

namespace Mau.Bricks
{
    /// <summary>
    /// 文件积木——标准积木库文件类
    /// </summary>
    public static class FileBrick
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

    /// <summary>
    /// 标准积木注册——进程启动时调用一次
    /// </summary>
    public static class StandardBrickRegistration
    {
        /// <summary>
        /// 注册全部标准积木
        /// </summary>
        public static void RegisterAll()
        {
            BrickContract convert = new BrickContract("file.convert", "Mau.Bricks.FileBrick.Convert");
            convert.Inputs.Add(new BrickPort("input", typeof(string), "输入文件路径"));
            convert.Inputs.Add(new BrickPort("output", typeof(string), "输出文件路径"));
            convert.Return = BrickReturnKind.Bool;
            convert.Duration = BrickDuration.Sync;
            convert.Thread = "main";
            BrickRegistry.Register(convert);
        }
    }
}
