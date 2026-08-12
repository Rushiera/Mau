using System;
using System.Collections.Generic;

namespace Mau.Translator
{
    /// <summary>
    /// 编译结果 v2——诊断 + 生成代码 + 分析报告
    /// </summary>
    public sealed class CompileResultV2
    {
        /// <summary>是否成功——无任何错误诊断</summary>
        public bool Success;

        /// <summary>诊断列表——词法 E0xx / 语法 E1xx / 验证 E2xx / 分析 E3xx</summary>
        public List<MauDiagnostic> Diagnostics = new List<MauDiagnostic>();

        /// <summary>解析文档——成功时可用</summary>
        public MauDocV2 Doc = new MauDocV2();

        /// <summary>生成代码——成功时可用</summary>
        public string GeneratedCode = "";

        /// <summary>关键路径报告（分析层）</summary>
        public string KeyPathReport = "";
    }

    /// <summary>
    /// 编译门面 v2——五阶段流水线：词法 → 解析 → 糖展开 → 验证 → 分析 → 生成
    /// </summary>
    public static class MauCompilerV2
    {
        /// <summary>
        /// 编译入口
        /// </summary>
        /// <param name="sourceText">.mau 源文本</param>
        /// <param name="flowName">流程名——PascalCase，生成类名</param>
        /// <returns>编译结果</returns>
        public static CompileResultV2 Compile(string sourceText, string flowName)
        {
            CompileResultV2 result = new CompileResultV2();

            // [阶段1] 词法——七步流水线（E0xx）
            LexResultV2 lex = MauLexerV2.Lex(sourceText);
            if (!lex.Success)
            {
                result.Diagnostics.AddRange(lex.Diagnostics);
                result.Success = false;
                return result;
            }

            // [阶段2] 解析——段 → IR + 类型推导（E1xx）
            ParseResultV2 parsed = MauParserV2.Parse(lex);
            if (!parsed.Success)
            {
                result.Diagnostics.AddRange(parsed.Diagnostics);
                result.Success = false;
                return result;
            }
            result.Doc = parsed.Doc;

            // [阶段3] 糖展开——IR 层变换（SEQ/PAR/FBK）
            ExpandResultV2 expanded = MauSugarExpanderV2.Expand(result.Doc);
            if (!expanded.Success)
            {
                result.Diagnostics.AddRange(expanded.Diagnostics);
                result.Success = false;
                return result;
            }

            // [阶段4] 验证——引用完整性（E2xx）
            ValidateResultV2 valid = MauValidatorV2.Validate(result.Doc);
            if (!valid.Success)
            {
                result.Diagnostics.AddRange(valid.Diagnostics);
                result.Success = false;
                return result;
            }

            // [阶段5] 分析——稳定性/可达性/关键路径/扰动（E3xx）
            AnalyzeResultV2 analyze = MauAnalyzerV2.Analyze(result.Doc);
            result.KeyPathReport = analyze.KeyPathReport;
            if (!analyze.Success)
            {
                result.Diagnostics.AddRange(analyze.Diagnostics);
                result.Success = false;
                return result;
            }

            // [阶段6] 生成——确定性输出
            result.GeneratedCode = MauGeneratorV2.Generate(result.Doc, flowName);
            result.Success = true;
            return result;
        }
    }
}
