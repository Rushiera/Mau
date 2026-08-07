using System;
using Mau.Translator;

namespace Mau.Translator.Tests
{
    /// <summary>
    /// 测试积木索引——共享单例，线程安全（积木注册表已退役——翻译器构筑期经 BrickIndex 加载 Bricks/index.json）
    /// </summary>
    internal static class TestBrickRegistration
    {
        private static readonly object Sync = new object();
        private static bool _done;

        /// <summary>
        /// 确保积木索引已加载——幂等，线程安全（探测：MAU_BRICKS_ROOT → 当前目录向上找 Bricks/）
        /// </summary>
        public static void Ensure()
        {
            lock (Sync)
            {
                if (_done)
                {
                    return;
                }
                if (BrickIndex.Count == 0)
                {
                    string? probe = Environment.GetEnvironmentVariable("MAU_BRICKS_ROOT");
                    if (!string.IsNullOrWhiteSpace(probe))
                    {
                        BrickIndex.Load(probe);
                    }
                    if (BrickIndex.Count == 0)
                    {
                        string? dir = System.IO.Directory.GetCurrentDirectory();
                        while (dir != null)
                        {
                            if (BrickIndex.Load(System.IO.Path.Combine(dir, "Bricks")))
                            {
                                break;
                            }
                            dir = System.IO.Directory.GetParent(dir)?.FullName;
                        }
                    }
                }
                _done = true;
            }
        }
    }
}
