using System;
using System.IO;
using Mau.Development;
using Mau.Runtime;
using Xunit;

namespace Mau.Development.Tests
{
    /// <summary>
    /// 引用集构造回归测试（A46）——共享框架注入按目标 TFM 判定。
    /// 非 -windows 项目若注入 WindowsDesktop.App，会引入 Accessibility.dll（命名空间 Accessibility），
    /// 与 Roslyn 的 Accessibility 枚举抢名 → cs-check 假阳性 CS0118/CS0234（实机 cs.build 无误）。
    /// </summary>
    public sealed class ReferenceProbeTests
    {
        /// <summary>
        /// 仓库根（受控根）
        /// </summary>
        private readonly string _root;

        /// <summary>
        /// 编码桥实例（受控根 = 仓库根）
        /// </summary>
        private readonly MauRoslynBridge _bridge;

        /// <summary>
        /// 创建夹具——定位仓库根 + 建桥
        /// </summary>
        public ReferenceProbeTests()
        {
            _root = RepoRoot();
            WorkspaceConfig.RootEntry entry = new WorkspaceConfig.RootEntry();
            entry.Id = "mau";
            entry.Path = _root;
            entry.Writable = true;
            _bridge = new MauRoslynBridge(new WorkspaceConfig.RootEntry[] { entry });
        }

        /// <summary>
        /// 仓库根探测——测试输出目录向上找含 Bricks 的层级
        /// </summary>
        /// <returns>含 Bricks 目录的层级路径</returns>
        private static string RepoRoot()
        {
            string dir = AppContext.BaseDirectory;
            while (dir.Length > 3 && !Directory.Exists(Path.Combine(dir, "Bricks")))
            {
                dir = Path.GetDirectoryName(dir) ?? "";
            }
            return dir;
        }

        /// <summary>
        /// 单项目 check——桥调用输出文本（cs-check 同源）
        /// </summary>
        /// <param name="rel">相对仓库根的 csproj 路径</param>
        /// <returns>结果文本</returns>
        private string Check(string rel)
        {
            string csproj = Path.Combine(_root, rel);
            Assert.True(File.Exists(csproj), "目标工程缺失: " + csproj);
            string result;
            _bridge.Invoke("check", "{\"path\":\"" + csproj.Replace("\\", "\\\\") + "\"}", out result);
            return result;
        }

        /// <summary>
        /// 非 -windows 项目（net8.0）——不得因共享框架注入报 CS0118/CS0234 抢名假阳性
        /// </summary>
        [Fact]
        public void NonWindowsProjectHasNoDesktopFrameworkNameClash()
        {
            string result = Check(Path.Combine("Mau", "Mau.Development", "Mau.Development.csproj"));
            Assert.False(result.Contains("CS0118"), "CS0118 假阳性复现: " + result);
            Assert.False(result.Contains("CS0234"), "CS0234 假阳性复现: " + result);
        }

        /// <summary>
        /// -windows 项目（net8.0-windows）——WindowsDesktop 框架仍须注入（WinForms 类型可解析）
        /// </summary>
        [Fact]
        public void WindowsProjectKeepsDesktopFramework()
        {
            string result = Check(Path.Combine("CatHome4", "CatHome4.csproj"));
            Assert.False(result.Contains("CS0246"), "WindowsDesktop 引用丢失（WinForms 类型解析失败）: " + result);
        }
    }
}
