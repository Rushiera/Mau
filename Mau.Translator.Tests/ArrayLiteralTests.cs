// ═══════════════════════════════════════════════════
// 测试: Mau.Translator——数组字面量（B2：语料可构造数组端口）
// 引用: Mau.Translator.Tests → Mau.Translator + Mau.Contracts
// 原理: [a,b,c] → 数组端口——解析/验证（E019）/生成（new T[] { ... }）
// 注意: 参数声明顺序必须与积木签名一致（翻译器按声明顺序生成调用）
// ═══════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using Mau.Translator;
using Xunit;

namespace Mau.Translator.Tests
{
    /// <summary>
    /// 数组字面量测试——[a,b,c] 绑定数组端口（B2）
    /// </summary>
    public class ArrayLiteralTests
    {
        /// <summary>
        /// 语料模板——cmd.register 参数顺序：ownerId 在前、cmdKeys 在后（与签名一致）
        /// </summary>
        private const string SourceTemplate =
            "Mau 0.1\n" +
            "命题:\n  P_S 信号\n  P_D 事实\n  P_F 事实\n" +
            "变迁 T_C:\n  前置: P_S\n  动作: cmd.register\n" +
            "  参数: 5 → ownerId, [\"life.set\",\"life.disable\"] → cmdKeys\n" +
            "  后置: P_D / P_F\n";

        /// <summary>
        /// 解析——数组字面量识别 + 元素提取 + 与常量绑定共存
        /// </summary>
        [Fact]
        public void ParseArrayLiteral_DetectsItems()
        {
            TestBrickRegistration.Ensure();
            ParseResult result = MauParser.Parse(SourceTemplate);

            Assert.Empty(result.Diagnostics);
            IrTransition? tc = result.Document.FindTransition("T_C");
            Assert.NotNull(tc);
            Assert.Equal(2, tc!.Params.Count);
            Assert.True(tc.Params[0].IsConstant);
            Assert.Equal("5", tc.Params[0].ConstantValue);
            Assert.True(tc.Params[1].IsArray);
            Assert.Equal(2, tc.Params[1].ArrayItems.Count);
            // 元素保留原文（含引号）——生成器负责剥离
            Assert.Equal("\"life.set\"", tc.Params[1].ArrayItems[0]);
            Assert.Equal("\"life.disable\"", tc.Params[1].ArrayItems[1]);
        }

        /// <summary>
        /// 验证——字符串数组字面量绑 string[] 端口通过
        /// </summary>
        [Fact]
        public void ValidateArrayLiteral_StringArray_Passes()
        {
            TestBrickRegistration.Ensure();
            ParseResult parsed = MauParser.Parse(SourceTemplate);
            List<MauDiagnostic> diags = MauValidator.Validate(parsed.Document);

            Assert.Empty(diags);
        }

        /// <summary>
        /// 验证——数组字面量绑标量端口 → E019
        /// </summary>
        [Fact]
        public void ValidateArrayLiteral_OnScalarPort_ReportsE019()
        {
            TestBrickRegistration.Ensure();
            string source = "Mau 0.1\n" +
                "命题:\n  P_S 信号\n  P_D 事实\n  P_F 事实\n" +
                "变迁 T_C:\n  前置: P_S\n  动作: cmd.register\n" +
                "  参数: [1,2,3] → ownerId, [\"a\"] → cmdKeys\n" +
                "  后置: P_D / P_F\n";
            ParseResult parsed = MauParser.Parse(source);
            List<MauDiagnostic> diags = MauValidator.Validate(parsed.Document);

            Assert.Contains(diags, d => d.Code == "E019");
        }

        /// <summary>
        /// 验证——数字元素绑 string[] 端口 → E019（元素类型严格）
        /// </summary>
        [Fact]
        public void ValidateArrayLiteral_NumberOnStringArray_ReportsE019()
        {
            TestBrickRegistration.Ensure();
            string source = "Mau 0.1\n" +
                "命题:\n  P_S 信号\n  P_D 事实\n  P_F 事实\n" +
                "变迁 T_C:\n  前置: P_S\n  动作: cmd.register\n" +
                "  参数: 5 → ownerId, [1,2] → cmdKeys\n" +
                "  后置: P_D / P_F\n";
            ParseResult parsed = MauParser.Parse(source);
            List<MauDiagnostic> diags = MauValidator.Validate(parsed.Document);

            Assert.Contains(diags, d => d.Code == "E019");
        }

        /// <summary>
        /// 生成——数组字面量输出 new string[] { ... }，调用顺序与签名一致
        /// </summary>
        [Fact]
        public void GenerateArrayLiteral_NewStringArray()
        {
            TestBrickRegistration.Ensure();
            CompileResult compile = MauCompiler.Compile(SourceTemplate, "ArrayFlow");

            Assert.True(compile.Success);
            Assert.Contains("new string[] { \"life.set\", \"life.disable\" }", compile.GeneratedCode);
            Assert.Contains("Register(5, new string[] { \"life.set\", \"life.disable\" })", compile.GeneratedCode);
        }
    }
}
