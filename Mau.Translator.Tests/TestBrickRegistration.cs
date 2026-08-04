using System;
using Mau.Contracts;

namespace Mau.Translator.Tests
{
    /// <summary>
    /// 测试积木注册——共享单例，线程安全，避免 xunit 并行下的重复注册
    /// </summary>
    internal static class TestBrickRegistration
    {
        private static readonly object Sync = new object();
        private static bool _done;

        /// <summary>
        /// 确保 file.convert 积木已注册——幂等，线程安全
        /// </summary>
        public static void Ensure()
        {
            lock (Sync)
            {
                if (_done)
                {
                    return;
                }
                BrickContract? existing;
                if (!BrickRegistry.TryGet("file.convert", out existing))
                {
                    BrickContract convert = new BrickContract("file.convert", "Mau.Bricks.FileBrick.Convert");
                    convert.Inputs.Add(new BrickPort("input", typeof(string), "输入文件路径"));
                    convert.Inputs.Add(new BrickPort("output", typeof(string), "输出文件路径"));
                    convert.Return = BrickReturnKind.Bool;
                    BrickRegistry.Register(convert);
                }
                _done = true;
            }
        }
    }
}
