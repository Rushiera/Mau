using System;
using System.IO;
using Mau.Development;
using Xunit;

namespace Mau.Development.Tests
{
    /// <summary>
    /// 口袋编译器测试——编译/导出/调用/卸载闭环
    /// </summary>
    public sealed class PocketCompilerTests : IDisposable
    {
        /// <summary>
        /// 测试输出根
        /// </summary>
        private readonly string _root;

        /// <summary>
        /// 创建测试夹具
        /// </summary>
        public PocketCompilerTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "mau_pocket_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        /// <summary>
        /// 释放夹具——尽力删除测试输出根
        /// </summary>
        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_root))
                {
                    Directory.Delete(_root, true);
                }
            }
            catch
            {
                // 忽略
            }
        }

        /// <summary>
        /// 样例源码——两个导出方法 + 一个隐藏方法
        /// </summary>
        private const string SampleSource = @"
using Mau.Contracts;

namespace PocketSample
{
    /// <summary>
    /// 口袋样例
    /// </summary>
    public static class Sample
    {
        /// <summary>
        /// 无参导出
        /// </summary>
        /// <returns>固定文本</returns>
        [MauExport]
        public static string Hello()
        {
            return ""hello-mau"";
        }

        /// <summary>
        /// 单参数导出
        /// </summary>
        /// <param name=""text"">输入</param>
        /// <returns>回显文本</returns>
        [MauExport]
        public static string Echo(string text)
        {
            return ""echo:"" + text;
        }

        /// <summary>
        /// 未标记导出——不可被发现
        /// </summary>
        /// <returns>隐藏文本</returns>
        public static string Hidden()
        {
            return ""hidden"";
        }
    }
}
";

        /// <summary>
        /// 编译成功——DLL 落盘且成功标志正确
        /// </summary>
        [Fact]
        public void Compile_Success_WritesDll()
        {
            MauPocketCompiler compiler = new MauPocketCompiler(_root);
            MauPocketCompileResult result = compiler.Compile(SampleSource, "Sample");

            Assert.True(result.Success);
            Assert.True(File.Exists(result.AssemblyPath));
            Assert.EndsWith("Sample.dll", result.AssemblyPath);
            Assert.Empty(result.Diagnostics);
        }

        /// <summary>
        /// 编译失败——诊断含错误码
        /// </summary>
        [Fact]
        public void Compile_SyntaxError_ReportsDiagnostics()
        {
            MauPocketCompiler compiler = new MauPocketCompiler(_root);
            MauPocketCompileResult result = compiler.Compile("class Broken { void M( }", "Broken");

            Assert.False(result.Success);
            Assert.NotEmpty(result.Diagnostics);
        }

        /// <summary>
        /// 列出导出——仅 MauExport 标记方法
        /// </summary>
        [Fact]
        public void ListExports_OnlyMarkedMethods()
        {
            MauPocketCompiler compiler = new MauPocketCompiler(_root);
            MauPocketCompileResult result = compiler.Compile(SampleSource, "Sample");
            Assert.True(result.Success);

            string[] exports = compiler.ListExports(result.AssemblyPath);

            Assert.Equal(2, exports.Length);
            Assert.Contains("PocketSample.Sample.Echo", exports);
            Assert.Contains("PocketSample.Sample.Hello", exports);
            Assert.DoesNotContain("Hidden", exports);
        }

        /// <summary>
        /// 调用无参导出——返回值正确且 ALC 已回收
        /// </summary>
        [Fact]
        public void Invoke_NoArgument_ReturnsValueAndUnloads()
        {
            MauPocketCompiler compiler = new MauPocketCompiler(_root);
            MauPocketCompileResult result = compiler.Compile(SampleSource, "Sample");
            Assert.True(result.Success);

            MauPocketInvocationResult invocation = compiler.Invoke(
                result.AssemblyPath, "PocketSample.Sample", "Hello", "");

            Assert.Equal("hello-mau", invocation.Result);
            Assert.True(invocation.Unloaded);
        }

        /// <summary>
        /// 调用单参数导出——参数透传
        /// </summary>
        [Fact]
        public void Invoke_SingleStringArgument_PassesThrough()
        {
            MauPocketCompiler compiler = new MauPocketCompiler(_root);
            MauPocketCompileResult result = compiler.Compile(SampleSource, "Sample");
            Assert.True(result.Success);

            MauPocketInvocationResult invocation = compiler.Invoke(
                result.AssemblyPath, "PocketSample.Sample", "Echo", "abc");

            Assert.Equal("echo:abc", invocation.Result);
            Assert.True(invocation.Unloaded);
        }

        /// <summary>
        /// 调用未标记方法——拒绝访问
        /// </summary>
        [Fact]
        public void Invoke_NotExported_Throws()
        {
            MauPocketCompiler compiler = new MauPocketCompiler(_root);
            MauPocketCompileResult result = compiler.Compile(SampleSource, "Sample");
            Assert.True(result.Success);

            Assert.Throws<UnauthorizedAccessException>(() => compiler.Invoke(
                result.AssemblyPath, "PocketSample.Sample", "Hidden", ""));
        }
    }
}
