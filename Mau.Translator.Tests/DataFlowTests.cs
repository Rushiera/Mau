// ═══════════════════════════════════════════════
// 测试: Mau.Translator——数据流绑定 + 对外块（模块化 v0.1 核心）
// 引用: Mau.Translator.Tests → Mau.Translator + Mau.Contracts
// 原理: 探针积木（source 输出 text/texts，sink 输入 a/b）验证绑定解析/校验/生成
// 常用: 端口内绑定（B5 实现版）+ 对外契约（B9）回归
// ═══════════════════════════════════════════════
using System;
using System.Collections.Generic;
using Mau.Contracts;
using Mau.Translator;
using Xunit;

namespace Mau.Translator.Tests
{
    /// <summary>
    /// 数据流绑定测试——箭头绑定解析/校验/生成 + 对外块 + 空闲 Touch
    /// </summary>
    public class DataFlowTests
    {
        
        /// <summary>
        /// 箭头绑定解析——变量 → 端口 分离存储
        /// </summary>
        [Fact]
        public void ParseArrowBinding_SeparatesVariableAndPort()
        {
            string source = "Mau 0.1\n" +
                "命题:\n  P_S 信号\n  P_D 事实\n  P_F 事实\n" +
                "变迁 T_A:\n  前置: P_S\n  动作: probe.source\n  参数: ownerId\n  后置: P_D / P_F\n" +
                "变迁 T_B:\n  前置: P_D\n  动作: probe.sink\n  参数: text → a, b\n  后置: P_D / P_F\n";
            ParseResult result = MauParser.Parse(source);

            Assert.Empty(result.Diagnostics);
            IrTransition? tb = result.Document.FindTransition("T_B");
            Assert.NotNull(tb);
            Assert.Equal(2, tb!.Params.Count);
            Assert.Equal("text", tb.Params[0].Variable);
            Assert.Equal("a", tb.Params[0].PortName);
            Assert.Equal("b", tb.Params[1].Variable);
            Assert.Equal("b", tb.Params[1].PortName);
        }

        /// <summary>
        /// 完整绑定——验证通过
        /// </summary>
        [Fact]
        public void ValidateArrowBinding_CompleteBindings_Passes()
        {
            TestBrickRegistration.Ensure();
            string source = "Mau 0.1\n" +
                "命题:\n  P_S 信号\n  P_D 事实\n  P_F 事实\n" +
                "变迁 T_A:\n  前置: P_S\n  动作: probe.source\n  参数: ownerId\n  后置: P_D / P_F\n" +
                "变迁 T_B:\n  前置: P_D\n  动作: probe.sink\n  参数: text → a, b\n  后置: P_D / P_F\n";
            ParseResult result = MauParser.Parse(source);
            List<MauDiagnostic> diags = MauValidator.Validate(result.Document);

            Assert.Empty(diags);
        }

        /// <summary>
        /// 变量未定义——E015
        /// </summary>
        [Fact]
        public void ValidateArrowBinding_UnknownVariable_ReportsE015()
        {
            TestBrickRegistration.Ensure();
            string source = "Mau 0.1\n" +
                "命题:\n  P_S 信号\n  P_D 事实\n  P_F 事实\n" +
                "变迁 T_A:\n  前置: P_S\n  动作: probe.source\n  参数: ownerId\n  后置: P_D / P_F\n" +
                "变迁 T_B:\n  前置: P_D\n  动作: probe.sink\n  参数: nosuch → a, b\n  后置: P_D / P_F\n";
            ParseResult result = MauParser.Parse(source);
            List<MauDiagnostic> diags = MauValidator.Validate(result.Document);

            Assert.Contains(diags, d => d.Code == "E015");
        }

        /// <summary>
        /// 类型不匹配——E016（数组 → 标量）
        /// </summary>
        [Fact]
        public void ValidateArrowBinding_TypeMismatch_ReportsE016()
        {
            TestBrickRegistration.Ensure();
            string source = "Mau 0.1\n" +
                "命题:\n  P_S 信号\n  P_D 事实\n  P_F 事实\n" +
                "变迁 T_A:\n  前置: P_S\n  动作: probe.source\n  参数: ownerId\n  后置: P_D / P_F\n" +
                "变迁 T_B:\n  前置: P_D\n  动作: probe.sink\n  参数: texts → a, b\n  后置: P_D / P_F\n";
            ParseResult result = MauParser.Parse(source);
            List<MauDiagnostic> diags = MauValidator.Validate(result.Document);

            Assert.Contains(diags, d => d.Code == "E016");
        }

        /// <summary>
        /// 输入端口未全覆盖——E017
        /// </summary>
        [Fact]
        public void ValidateArrowBinding_MissingInput_ReportsE017()
        {
            TestBrickRegistration.Ensure();
            string source = "Mau 0.1\n" +
                "命题:\n  P_S 信号\n  P_D 事实\n  P_F 事实\n" +
                "变迁 T_A:\n  前置: P_S\n  动作: probe.source\n  参数: ownerId\n  后置: P_D / P_F\n" +
                "变迁 T_B:\n  前置: P_D\n  动作: probe.sink\n  参数: text → a\n  后置: P_D / P_F\n";
            ParseResult result = MauParser.Parse(source);
            List<MauDiagnostic> diags = MauValidator.Validate(result.Document);

            Assert.Contains(diags, d => d.Code == "E017");
        }

        /// <summary>
        /// 对外块解析——接收/发送条目方向与 Key/Port 正确
        /// </summary>
        [Fact]
        public void ParseExternalBlock_EntriesAndDirections()
        {
            string source = "Mau 0.1\n" +
                "命题:\n  P_User 信号\n  P_D 事实\n  P_F 事实\n" +
                "变迁 T_X:\n  前置: P_User\n  动作: probe.source\n  参数: ownerId\n  后置: P_D / P_F\n" +
                "对外:\n  接收: msg → P_User\n  发送: reply → reply_key\n";
            ParseResult result = MauParser.Parse(source);

            Assert.Empty(result.Diagnostics);
            Assert.Equal(2, result.Document.Externals.Count);
            Assert.Equal("接收", result.Document.Externals[0].Direction);
            Assert.Equal("msg", result.Document.Externals[0].Key);
            Assert.Equal("P_User", result.Document.Externals[0].Port);
            Assert.Equal("发送", result.Document.Externals[1].Direction);
            Assert.Equal("reply_key", result.Document.Externals[1].Key);
            Assert.Equal("reply", result.Document.Externals[1].Port);
        }

        /// <summary>
        /// 对外条目缺箭头——E151
        /// </summary>
        [Fact]
        public void ParseExternalBlock_MissingArrow_ReportsE151()
        {
            string source = "Mau 0.1\n" +
                "命题:\n  P_User 信号\n" +
                "对外:\n  接收: msg P_User\n";
            ParseResult result = MauParser.Parse(source);

            Assert.Contains(result.Diagnostics, d => d.Code == "E151");
        }

        /// <summary>
        /// 生成——箭头绑定传源字段（_text），Fire 只收简写参数（ownerId）
        /// </summary>
        [Fact]
        public void GenerateArrowBinding_SourceFieldAndFireShorthand()
        {
            TestBrickRegistration.Ensure();
            string source = "Mau 0.1\n" +
                "命题:\n  P_S 信号\n  P_D 事实\n  P_F 事实\n" +
                "变迁 T_A:\n  前置: P_S\n  动作: probe.source\n  参数: ownerId\n  后置: P_D / P_F\n" +
                "变迁 T_B:\n  前置: P_D\n  动作: probe.sink\n  参数: text → a, b\n  后置: P_D / P_F\n";
            ParseResult result = MauParser.Parse(source);
            List<MauDiagnostic> diags = MauValidator.Validate(result.Document);
            Assert.Empty(diags);
            CompileResult compile = MauCompiler.Compile(source, "ProbeFlow");

            Assert.True(compile.Success);
            Assert.Contains("Mau.Bricks.BRIK_TEST_002.Sink(_text, _b, out _result)", compile.GeneratedCode);
            Assert.Contains("public void FireS(long ownerId)", compile.GeneratedCode);
            Assert.DoesNotContain("FireS(long ownerId, string a", compile.GeneratedCode);
        }

        /// <summary>
        /// 生成——空闲时限生成 Touch 调用
        /// </summary>
        [Fact]
        public void GenerateIdleTimeout_TouchOnSuccess()
        {
            TestBrickRegistration.Ensure();
            string source = "Mau 0.1\n" +
                "命题:\n  P_S 信号\n  P_D 事实\n  P_F 事实\n" +
                "变迁 T_A:\n  前置: P_S\n  动作: probe.source\n  参数: ownerId\n  时限: 空闲60帧\n  后置: P_D / P_F\n";
            ParseResult result = MauParser.Parse(source);
            List<MauDiagnostic> diags = MauValidator.Validate(result.Document);
            Assert.Empty(diags);
            CompileResult compile = MauCompiler.Compile(source, "ProbeFlow");

            Assert.True(compile.Success);
            Assert.Contains("CubeMode.Idle, 60", compile.GeneratedCode);
            Assert.Contains(".Touch();", compile.GeneratedCode);
        }
        /// <summary>
        /// 常量绑定解析——字符串字面量与数字字面量
        /// </summary>
        [Fact]
        public void ParseConstantBinding_DetectsLiterals()
        {
            string source = "Mau 0.1\n" +
                "命题:\n  P_S 信号\n  P_D 事实\n  P_F 事实\n" +
                "变迁 T_X:\n  前置: P_S\n  动作: probe.sink\n  参数: \"file.read\" → a, 5 → b\n  后置: P_D / P_F\n";
            ParseResult result = MauParser.Parse(source);

            Assert.Empty(result.Diagnostics);
            IrTransition? tx = result.Document.FindTransition("T_X");
            Assert.NotNull(tx);
            Assert.True(tx!.Params[0].IsConstant);
            Assert.Equal("file.read", tx.Params[0].ConstantValue);
            Assert.Equal("a", tx.Params[0].PortName);
            Assert.True(tx.Params[1].IsConstant);
            Assert.Equal("5", tx.Params[1].ConstantValue);
            Assert.Equal("b", tx.Params[1].PortName);
        }

        /// <summary>
        /// 常量绑定生成——字面量直传，Fire 不含常量参数
        /// </summary>
        [Fact]
        public void GenerateConstantBinding_LiteralInCall()
        {
            TestBrickRegistration.Ensure();
            string source = "Mau 0.1\n" +
                "命题:\n  P_S 信号\n  P_D 事实\n  P_F 事实\n" +
                "变迁 T_X:\n  前置: P_S\n  动作: probe.sink\n  参数: \"file.read\" → a, 5 → b\n  后置: P_D / P_F\n";
            ParseResult result = MauParser.Parse(source);
            List<MauDiagnostic> diags = MauValidator.Validate(result.Document);
            Assert.Empty(diags);
            CompileResult compile = MauCompiler.Compile(source, "ProbeFlow");

            Assert.True(compile.Success);
            Assert.Contains("Mau.Bricks.BRIK_TEST_002.Sink(\"file.read\", 5, out _result)", compile.GeneratedCode);
            Assert.Contains("public void FireS()", compile.GeneratedCode);
        }

        /// <summary>
        /// 常量类型不匹配——字符串常量绑 int 端口 → E018
        /// </summary>
        [Fact]
        public void ValidateConstantBinding_TypeMismatch_ReportsE018()
        {
            TestBrickRegistration.Ensure();
            string source = "Mau 0.1\n" +
                "命题:\n  P_S 信号\n  P_D 事实\n  P_F 事实\n" +
                "变迁 T_X:\n  前置: P_S\n  动作: probe.sink\n  参数: \"abc\" → b, \"file.read\" → a\n  后置: P_D / P_F\n";
            ParseResult result = MauParser.Parse(source);
            List<MauDiagnostic> diags = MauValidator.Validate(result.Document);

            Assert.Contains(diags, d => d.Code == "E018");
        }
    }
}
