using System;
using System.Collections.Generic;
using Mau.Contracts;

namespace Mau.Translator
{
    /// <summary>
    /// v3 编译器门面——词法 → 解析 → 验证 → 分析 → 生成 全链（design-mau-v3 §八）。
    /// 任何一环失败即拒绝生成（白名单词法 + 语义校验 + 图论分析 = LLM 表达，代码验真）。
    /// </summary>
    public static class MauCompilerV3
    {
        /// <summary>
        /// 全链编译——.mau 源 → C# 生成物
        /// </summary>
        /// <param name="source">.mau 源文本</param>
        /// <param name="flowName">流程名（PascalCase——类名 FL_ 前缀）</param>
        /// <returns>编译结果（Success=false 时 Diagnostics 有内容且 GeneratedCode 为空）</returns>
        public static CompileResultV3 Compile(string source, string flowName)
        {
            return Compile(source, flowName, new DefaultAppearance());
        }

        /// <summary>
        /// 全链编译——指定外观表
        /// </summary>
        /// <param name="source">.mau 源文本</param>
        /// <param name="flowName">流程名</param>
        /// <param name="appearance">符号外观表</param>
        /// <returns>编译结果</returns>
        public static CompileResultV3 Compile(string source, string flowName, ISymbolAppearance appearance)
        {
            CompileResultV3 result = new CompileResultV3();
            // [段1] 词法——白名单前置
            LexResultV3 lex = MauLexerV3.Lex(source, appearance);
            if (!lex.Success)
            {
                result.Diagnostics.AddRange(lex.Diagnostics);
                result.Success = false;
                return result;
            }
            // [段2] 解析——TokenId → FSM 网络 IR
            MauDocV3 doc = MauParserV3.Parse(lex.Tokens);
            if (!doc.Success)
            {
                result.Diagnostics.AddRange(doc.Diagnostics);
                result.Success = false;
                return result;
            }
            // [段3] 验证——名称唯一 + 引用完整性
            if (!MauValidatorV3.Validate(doc))
            {
                result.Diagnostics.AddRange(doc.Diagnostics);
                result.Success = false;
                return result;
            }
            // [段4] 分析——自检仪（图论验证，失败拒绝生成）
            AnalysisResultV3 analysis = MauAnalyzerV3.Analyze(doc);
            if (!analysis.Success)
            {
                result.Diagnostics.AddRange(analysis.Errors);
                result.Success = false;
                return result;
            }
            result.Reports.AddRange(analysis.Reports);
            // [段5] 生成——IR → C#
            GenerateResultV3 gen = MauGeneratorV3.Generate(doc, flowName);
            if (!gen.Success)
            {
                result.Diagnostics.AddRange(gen.Diagnostics);
                result.Success = false;
                return result;
            }
            result.GeneratedCode = gen.Code;
            result.Success = true;
            return result;
        }
    }

    /// <summary>
    /// v3 编译结果——生成物 + 诊断 + 报告
    /// </summary>
    public sealed class CompileResultV3
    {
        /// <summary>
        /// 编译是否成功
        /// </summary>
        public bool Success;

        /// <summary>
        /// C# 生成物全文（失败时为空）
        /// </summary>
        public string GeneratedCode = "";

        /// <summary>
        /// 编译诊断（E0xx-E3xx）
        /// </summary>
        public List<MauDiagnostic> Diagnostics = new List<MauDiagnostic>();

        /// <summary>
        /// 分析报告（关键路径等）
        /// </summary>
        public List<string> Reports = new List<string>();
    }
}
