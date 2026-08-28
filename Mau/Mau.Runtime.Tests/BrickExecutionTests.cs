using System;
using System.IO;
using Xunit;

namespace Mau.Runtime.Tests
{
    /// <summary>
    /// 积木执行跑测——薄封装：逻辑唯一态在 Mau.Development.BrickSpecRunner（CLI 门禁段与测试共用）。
    /// </summary>
    public sealed class BrickExecutionTests
    {
        /// <summary>
        /// 跑测主链——brickflow 语料 → 强类型直调 → 状态转移断言
        /// </summary>
        [Fact]
        public void BrickFlow_ProbeSink_ExecutesAndTransitions()
        {
            Mau.Development.BrickSpecRunResult result = Mau.Development.BrickSpecRunner.Run(FindRepoRoot());
            Assert.True(result.Success, result.Summary + "\n" + result.Details);
        }

        /// <summary>
        /// 仓库根探测——Mau.sln 锚点向上找
        /// </summary>
        /// <returns>仓库根</returns>
        private static string FindRepoRoot()
        {
            DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Mau.sln")))
            {
                dir = dir.Parent;
            }
            if (dir == null)
            {
                throw new DirectoryNotFoundException("未找到 Mau.sln——跑测语料定位失败");
            }
            return dir.FullName;
        }
    }
}
