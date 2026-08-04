using System.Collections.Generic;

namespace Mau.Translator
{
    /// <summary>
    /// 编译结果——诊断 + 生成代码
    /// </summary>
    public sealed class CompileResult
    {
        /// <summary>
        /// 是否成功——无任何错误诊断
        /// </summary>
        public bool Success;

        /// <summary>
        /// 诊断列表——语法 + 验证
        /// </summary>
        public List<MauDiagnostic> Diagnostics;

        /// <summary>
        /// 解析文档——成功时可用
        /// </summary>
        public MauDocument Document;

        /// <summary>
        /// 生成代码——成功时可用
        /// </summary>
        public string GeneratedCode;

        /// <summary>
        /// 构造编译结果
        /// </summary>
        public CompileResult()
        {
            Success = false;
            Diagnostics = new List<MauDiagnostic>();
            Document = new MauDocument();
            GeneratedCode = "";
        }
    }

    /// <summary>
    /// Mau 编译器门面——解析 → IR → 静态验证 → 生成
    /// </summary>
    public static class MauCompiler
    {
        /// <summary>
        /// 编译 Mau 源文本
        /// </summary>
        /// <param name="sourceText">Mau 源文本</param>
        /// <param name="flowName">流程名——PascalCase，用于生成类名</param>
        /// <returns>编译结果</returns>
        public static CompileResult Compile(string sourceText, string flowName)
        {
            CompileResult result = new CompileResult();

            // [段1] 解析
            ParseResult parsed = MauParser.Parse(sourceText);
            result.Document = parsed.Document;
            result.Diagnostics.AddRange(parsed.Diagnostics);
            if (HasErrors(parsed.Diagnostics))
            {
                return result;
            }

            // [段2] 静态验证
            List<MauDiagnostic> validateDiags = MauValidator.Validate(parsed.Document);
            result.Diagnostics.AddRange(validateDiags);
            if (HasErrors(validateDiags))
            {
                return result;
            }

            // [段3] 代码生成
            result.GeneratedCode = CodeGenerator.Generate(parsed.Document, flowName);
            result.Success = true;
            return result;
        }

        /// <summary>
        /// 是否存在错误诊断
        /// </summary>
        /// <param name="diags">诊断列表</param>
        /// <returns>存在为真</returns>
        private static bool HasErrors(List<MauDiagnostic> diags)
        {
            return diags.Count > 0;
        }
    }
}
