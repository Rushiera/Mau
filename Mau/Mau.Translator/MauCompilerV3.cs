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
        /// 全链编译——.mau 源 → C# 生成物（兼容入口——默认外观表 + 内嵌积木）
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

        /// <summary>
        /// 全链编译——指定外观表 + 积木内嵌开关（组模式 embedBricks=false——积木提取到 BRIKGROUP.cs）。
        /// </summary>
        /// <param name="source">.mau 源文本</param>
        /// <param name="flowName">流程名</param>
        /// <param name="appearance">符号外观表</param>
        /// <param name="embedBricks">true=内嵌积木源（单文件路径）；false=不内嵌（组模式）</param>
        /// <param name="doc">解析出的 IR（组模式收集用）</param>
        /// <returns>编译结果</returns>
        public static CompileResultV3 Compile(string source, string flowName, ISymbolAppearance appearance, bool embedBricks, out MauDocV3 doc)
        {
            doc = null!;
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
            doc = MauParserV3.Parse(lex.Tokens);
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
            GenerateResultV3 gen = MauGeneratorV3.Generate(doc, flowName, embedBricks);
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

        /// <summary>
        /// 组模式全链编译——多 .mau 源 → 各 FL_*.cs（embedBricks=false）+ BRIKGROUP.cs（组共享积木闭包）。
        /// 对应 design-ch4-deploy.md §3.2：一组翻译产出 = csproj + BRIKGROUP.cs + 各 FL_*.cs。
        /// </summary>
        /// <param name="sources">.mau 源文本数组</param>
        /// <param name="flowNames">流程名数组（与 sources 对齐）</param>
        /// <returns>组编译结果（任一失败 = 整组拒绝）</returns>
        public static GroupCompileResultV3 CompileGroup(string[] sources, string[] flowNames)
        {
            GroupCompileResultV3 group = new GroupCompileResultV3();
            List<MauDocV3> docs = new List<MauDocV3>();
            for (int i = 0; i < sources.Length; i++)
            {
                CompileResultV3 result = Compile(sources[i], flowNames[i], new DefaultAppearance(), false, out MauDocV3 doc);
                group.Results.Add(result);
                if (!result.Success)
                {
                    group.Success = false;
                    return group;
                }
                docs.Add(doc);
            }
            group.BrickGroupSource = MauGeneratorV3.GenerateBrickGroup(docs, flowNames);
            group.Success = true;
            return group;
        }
    }

    /// <summary>
    /// v3 组编译结果——各 FL 生成物 + BRIKGROUP.cs 共享闭包
    /// </summary>
    public sealed class GroupCompileResultV3
    {
        /// <summary>
        /// 组编译是否成功（任一失败 = 整组拒绝）
        /// </summary>
        public bool Success;

        /// <summary>
        /// 各语料编译结果（与输入对齐）
        /// </summary>
        public List<CompileResultV3> Results = new List<CompileResultV3>();

        /// <summary>
        /// BRIKGROUP.cs 全文（组内积木共享闭包；空 = 组无积木引用）
        /// </summary>
        public string BrickGroupSource = "";
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
