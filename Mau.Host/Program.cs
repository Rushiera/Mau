using System;

namespace Mau.Host
{
    /// <summary>
    /// Mau 宿主入口——只做引导：注册积木 → 入口就绪 → 等待退出信号
    /// 纲领二：入口只是入口——业务逻辑、构筑逻辑、观测逻辑全部在基座与工具中
    /// </summary>
    public static class Program
    {
        /// <summary>
        /// 入口
        /// </summary>
        /// <param name="args">命令行参数</param>
        public static void Main(string[] args)
        {
            // [段1] 基座环境准备——注册标准积木
            Mau.Bricks.StandardBrickRegistration.RegisterAll();

            // [段2] 入口就绪——等待引导方驱动
            Console.WriteLine("Mau Host 入口就绪——等待引导");

            // [段3] 等待退出（引导方通过外部信号结束）
            Console.ReadLine();
        }
    }
}
