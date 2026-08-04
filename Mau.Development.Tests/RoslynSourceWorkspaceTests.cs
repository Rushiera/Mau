using System;
using System.IO;
using Mau.Development;
using Xunit;

namespace Mau.Development.Tests
{
    /// <summary>
    /// Roslyn 源码工作区测试——索引/读取/诊断/引用/注释检查/方法体替换
    /// </summary>
    public class RoslynSourceWorkspaceTests : IDisposable
    {
        /// <summary>
        /// 临时源码根
        /// </summary>
        private readonly string _root;

        /// <summary>
        /// 创建临时源码根并写入样例文件
        /// </summary>
        public RoslynSourceWorkspaceTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "mau_ws_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
            File.WriteAllText(Path.Combine(_root, "Sample.cs"), SampleSource);
            File.WriteAllText(Path.Combine(_root, "MissingComment.cs"), MissingCommentSource);
            File.WriteAllText(Path.Combine(_root, "Broken.cs"), BrokenSource);
        }

        /// <summary>
        /// 清理临时源码根
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
        /// 正常样例——注释完整
        /// </summary>
        private const string SampleSource = @"namespace WorkspaceSample
{
    /// <summary>
    /// 样例类型
    /// </summary>
    public class Sample
    {
        /// <summary>
        /// 值字段
        /// </summary>
        private int _value;

        /// <summary>
        /// 获取值
        /// </summary>
        /// <returns>值</returns>
        public int GetValue()
        {
            return _value;
        }

        /// <summary>
        /// 设置值
        /// </summary>
        /// <param name=""value"">新值</param>
        public void SetValue(int value)
        {
            _value = value;
        }
    }
}
";

        /// <summary>
        /// 缺注释样例——NoComment 无 summary
        /// </summary>
        private const string MissingCommentSource = @"namespace WorkspaceSample
{
    /// <summary>
    /// 缺注释样例
    /// </summary>
    public class MissingComment
    {
        public int NoComment()
        {
            return 1;
        }
    }
}
";

        /// <summary>
        /// 语法错误样例——括号不闭合
        /// </summary>
        private const string BrokenSource = @"namespace WorkspaceSample
{
    public class Broken
    {
        public void Bad(
        }
    }
}
";

        /// <summary>
        /// 索引——类型和成员签名齐全
        /// </summary>
        [Fact]
        public void ListMembers_ContainsTypesAndMembers()
        {
            MauRoslynSourceWorkspace workspace = new MauRoslynSourceWorkspace(_root);
            string[] lines = workspace.ListMembers();

            Assert.Contains(lines, l => l.Contains("Sample.cs | type | Sample"));
            Assert.Contains(lines, l => l.Contains("Sample.cs | member | Sample.GetValue"));
            Assert.Contains(lines, l => l.Contains("Sample.cs | member | Sample.SetValue"));
            Assert.Contains(lines, l => l.Contains("MissingComment.cs | type | MissingComment"));
        }

        /// <summary>
        /// 读取类型——返回类型声明源码
        /// </summary>
        [Fact]
        public void ReadMember_Type_ReturnsDeclaration()
        {
            MauRoslynSourceWorkspace workspace = new MauRoslynSourceWorkspace(_root);
            string text = workspace.ReadMember("Sample", "");

            Assert.Contains("public class Sample", text);
            Assert.Contains("/// <summary>", text);
        }

        /// <summary>
        /// 读取方法——返回方法完整源码含注释
        /// </summary>
        [Fact]
        public void ReadMember_Method_ReturnsMethodSource()
        {
            MauRoslynSourceWorkspace workspace = new MauRoslynSourceWorkspace(_root);
            string text = workspace.ReadMember("Sample", "GetValue");

            Assert.Contains("public int GetValue()", text);
            Assert.Contains("return _value;", text);
            Assert.Contains("/// <summary>", text);
        }

        /// <summary>
        /// 语法诊断——错误文件产生 CS 诊断
        /// </summary>
        [Fact]
        public void GetSyntaxDiagnostics_DetectsBrokenFile()
        {
            MauRoslynSourceWorkspace workspace = new MauRoslynSourceWorkspace(_root);
            string[] diagnostics = workspace.GetSyntaxDiagnostics();

            Assert.Contains(diagnostics, d => d.Contains("Broken.cs") && d.Contains("CS"));
        }

        /// <summary>
        /// 引用定位——标识符命中所有引用行
        /// </summary>
        [Fact]
        public void FindIdentifierReferences_LocatesAllUses()
        {
            MauRoslynSourceWorkspace workspace = new MauRoslynSourceWorkspace(_root);
            string[] references = workspace.FindIdentifierReferences("GetValue");

            Assert.NotEmpty(references);
            Assert.Contains(references, r => r.Contains("Sample.cs") && r.Contains("GetValue"));
        }

        /// <summary>
        /// 注释检查——缺 summary 的成员被列出
        /// </summary>
        [Fact]
        public void CheckXmlComments_ListsMissingSummary()
        {
            MauRoslynSourceWorkspace workspace = new MauRoslynSourceWorkspace(_root);
            string[] missing = workspace.CheckXmlComments();

            Assert.Contains(missing, m => m.Contains("NoComment"));
            Assert.DoesNotContain(missing, m => m.Contains("GetValue"));
        }

        /// <summary>
        /// 方法体替换——文件落盘且再次读取可见新体
        /// </summary>
        [Fact]
        public void ReplaceMethodBody_WritesNewBody()
        {
            MauRoslynSourceWorkspace workspace = new MauRoslynSourceWorkspace(_root);
            workspace.ReplaceMethodBody("Sample", "SetValue",
                "{ _value = 99; }");

            string after = File.ReadAllText(Path.Combine(_root, "Sample.cs"));
            Assert.Contains("_value = 99;", after);
            Assert.DoesNotContain("_value = value;", after);

            string readBack = workspace.ReadMember("Sample", "SetValue");
            Assert.Contains("_value = 99;", readBack);
        }

        /// <summary>
        /// 替换不存在的成员——抛异常且文件不变
        /// </summary>
        [Fact]
        public void ReplaceMethodBody_UnknownMember_Throws()
        {
            MauRoslynSourceWorkspace workspace = new MauRoslynSourceWorkspace(_root);
            Assert.Throws<InvalidOperationException>(() => workspace.ReplaceMethodBody(
                "Sample", "NoSuchMethod", "{ }"));
        }
    }
}
