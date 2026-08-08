// ═══════════════════════════════════════════════
// 测试: Mau.Translator——行号映射（D1：生成物行 → 语料行/积木源码行）
// 引用: Mau.Translator.Tests → Mau.Translator + Mau.Contracts
// 原理: MauCompiler.Compile 生成 GeneratedMap——验证 transition/brick 段记录
// ═══════════════════════════════════════════════
using System;
using Mau.Translator;
using Xunit;

namespace Mau.Translator.Tests
{
    /// <summary>
    /// 行号映射测试——GeneratedMap 包含变迁块与积木段记录（D1 调试基建）
    /// </summary>
    public class SourceMapTests
    {
        /// <summary>
        /// 单文件编译——map 含 transition 段（语料行号）与 brick 段（积木源码）
        /// </summary>
        [Fact]
        public void BuildMap_SingleFile_ContainsTransitionAndBrick()
        {
            TestBrickRegistration.Ensure();
            string source = "Mau 0.1\n" +
                "命题:\n  P_S 信号\n  P_D 事实\n  P_F 事实\n" +
                "变迁 T_A:\n  前置: P_S\n  动作: probe.source\n  参数: ownerId\n  后置: P_D / P_F\n";
            CompileResult compile = MauCompiler.Compile(source, "MapFlow");

            Assert.True(compile.Success);
            Assert.False(string.IsNullOrEmpty(compile.GeneratedMap));
            // transition 段——T_A 的语料行号（变迁声明在第 6 行）
            Assert.Contains("transition T_A: generated ", compile.GeneratedMap);
            Assert.Contains(" source 6", compile.GeneratedMap);
            // brick 段——probe.source 内嵌
            Assert.Contains("brick BRIK-TEST-001: generated ", compile.GeneratedMap);
            Assert.Contains(" stripped ", compile.GeneratedMap);
        }

        /// <summary>
        /// map 与生成物行号对齐——transition 段起始行确实有 [T_A] 标记
        /// </summary>
        [Fact]
        public void BuildMap_GeneratedLine_MatchesMarkerLine()
        {
            TestBrickRegistration.Ensure();
            string source = "Mau 0.1\n" +
                "命题:\n  P_S 信号\n  P_D 事实\n  P_F 事实\n" +
                "变迁 T_A:\n  前置: P_S\n  动作: probe.source\n  参数: ownerId\n  后置: P_D / P_F\n";
            CompileResult compile = MauCompiler.Compile(source, "MapFlow");

            Assert.True(compile.Success);
            string[] codeLines = compile.GeneratedCode.Split('\n');
            string[] mapLines = compile.GeneratedMap.Split('\n');
            bool found = false;
            for (int i = 0; i < mapLines.Length; i++)
            {
                string line = mapLines[i].Trim();
                if (line.StartsWith("transition T_A:", StringComparison.Ordinal))
                {
                    string[] parts = line.Split(' ');
                    int genLine = int.Parse(parts[3]);
                    Assert.Contains("[T_A]", codeLines[genLine - 1]);
                    found = true;
                }
            }
            Assert.True(found);
        }

        /// <summary>
        /// 生成物含数据流追踪代码——SetTraceDataFlow + Signal/Consume 日志点（D2）
        /// </summary>
        [Fact]
        public void Generate_TraceDataFlow_CodePresent()
        {
            TestBrickRegistration.Ensure();
            string source = "Mau 0.1\n" +
                "命题:\n  P_S 信号\n  P_D 事实\n  P_F 事实\n" +
                "变迁 T_A:\n  前置: P_S\n  动作: probe.source\n  参数: ownerId\n  后置: P_D / P_F\n";
            CompileResult compile = MauCompiler.Compile(source, "TraceFlow");

            Assert.True(compile.Success);
            Assert.Contains("SetTraceDataFlow", compile.GeneratedCode);
            Assert.Contains("_traceDataFlow", compile.GeneratedCode);
            Assert.Contains("_logs.Add(new MauDebug(_frame, \"Fire\", \"Signal\", \"P_S\"))", compile.GeneratedCode);
            Assert.Contains("\"Consume\", \"P_S\"", compile.GeneratedCode);
        }
    }
}
