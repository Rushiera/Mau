// ═══════════════════════════════════════════════
// 测试: Mau.Development——行号映射诊断反查（D1：生成物行 → 语料行/积木源码行）
// 引用: Mau.Development.Tests → Mau.Development
// 原理: Compile 带 mapText——FormatDiagnostics 反查错误行号到语料/积木源码行
// ═══════════════════════════════════════════════
using System;
using System.IO;
using Mau.Development;
using Xunit;

namespace Mau.Development.Tests
{
    /// <summary>
    /// 行号映射反查测试——编译错误诊断携带语料/积木源码定位（D1 调试基建）
    /// </summary>
    public sealed class PocketMapTests : IDisposable
    {
        /// <summary>
        /// 测试输出根
        /// </summary>
        private readonly string _root;

        /// <summary>
        /// 创建测试夹具
        /// </summary>
        public PocketMapTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "mau_map_test_" + Guid.NewGuid().ToString("N"));
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
        /// transition 段反查——错误行落在变迁块 → 映射为语料行号 + 说明
        /// </summary>
        [Fact]
        public void Compile_ErrorInTransition_ResolvesSourceLine()
        {
            MauPocketCompiler compiler = new MauPocketCompiler(_root);
            // 第 5 行有编译错误（int = string）
            string source = "class X\n{\n    void M()\n    {\n        int x = \"s\";\n    }\n}\n";
            string map = "# Mau 行号映射 v1\n"
                + "transition T_A: generated 5 source 6\n"
                + "brick BRIK-TEST-001: generated 10-20 stripped 4\n";
            MauPocketCompileResult result = compiler.Compile(source, "MapFlow", map);

            Assert.False(result.Success);
            Assert.Contains(result.Diagnostics, d => d.Contains("→ 语料 T_A 行6"));
        }

        /// <summary>
        /// brick 段反查——错误行落在内嵌积木段 → 映射为积木源码行 + 说明
        /// </summary>
        [Fact]
        public void Compile_ErrorInBrick_ResolvesBrickSourceLine()
        {
            MauPocketCompiler compiler = new MauPocketCompiler(_root);
            // 第 12 行有编译错误（int = string 字段初始化）
            string source = "class X\n{\n    void M()\n    {\n        int a = 1;\n    }\n}\n\nnamespace Bad\n{\n    int z = \"oops\";\n}\n";
            string map = "# Mau 行号映射 v1\n"
                + "transition T_A: generated 5 source 6\n"
                + "brick BRIK-TEST-001: generated 10-20 stripped 4\n";
            MauPocketCompileResult result = compiler.Compile(source, "MapFlow", map);

            Assert.False(result.Success);
            Assert.Contains(result.Diagnostics, d => d.Contains("→ 积木 BRIK-TEST-001 源码行5"));
        }

        /// <summary>
        /// 无 map——诊断原样输出，无映射说明
        /// </summary>
        [Fact]
        public void Compile_WithoutMap_NoMappingNote()
        {
            MauPocketCompiler compiler = new MauPocketCompiler(_root);
            string source = "class X\n{\n    void M()\n    {\n        int x = \"s\";\n    }\n}\n";
            MauPocketCompileResult result = compiler.Compile(source, "MapFlow");

            Assert.False(result.Success);
            Assert.DoesNotContain(result.Diagnostics, d => d.Contains("→ 语料"));
            Assert.DoesNotContain(result.Diagnostics, d => d.Contains("→ 积木"));
        }
    }
}
