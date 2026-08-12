using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

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
/// 确保积木索引已加载——未加载时按序探测：环境变量 MAU_BRICKS_ROOT → 当前目录向上 → 程序集目录向上
/// </summary>
/// <returns>索引可用</returns>
private static bool EnsureIndexLoaded()
{
    if (BrickIndex.Count > 0)
    {
        return true;
    }

    string? probe = Environment.GetEnvironmentVariable("MAU_BRICKS_ROOT");
    if (!string.IsNullOrWhiteSpace(probe) && BrickIndex.Load(probe))
    {
        return true;
    }

    string? dir = Directory.GetCurrentDirectory();
    while (dir != null)
    {
        if (BrickIndex.Load(Path.Combine(dir, "Bricks")))
        {
            return true;
        }

        dir = Directory.GetParent(dir)?.FullName;
    }

    dir = AppContext.BaseDirectory;
    while (dir != null)
    {
        if (BrickIndex.Load(Path.Combine(dir, "Bricks")))
        {
            return true;
        }

        dir = Directory.GetParent(dir)?.FullName;
    }

    return false;
}        /// <summary>
        /// 编译入口
        /// </summary>
        /// <param name="sourceText">.mau 源文本</param>
        /// <param name="flowName">流程名——PascalCase，生成类名</param>
        /// <returns>编译结果</returns>
        public static CompileResultV2 Compile(string sourceText, string flowName)
{
            CompileResultV2 result = new CompileResultV2();
            // [阶段0] 积木索引——构筑期契约数据源（Bricks/index.json；加载失败 = 积木契约校验跳过）
            EnsureIndexLoaded();

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

            // [阶段4] 验证——引用完整性 + 积木契约（E2xx）
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

            // [阶段6] 生成——骨架 + 内嵌积木段（BRIKGROUP，黄金剥离后对比）
            string skeleton = MauGeneratorV2.Generate(result.Doc, flowName);
            if (BrickIndex.Count > 0)
            {
                List<BrickIndexEntry> closure = BrickEmbedder.CollectClosureV2(result.Doc);
                string embedded = BrickEmbedder.BuildEmbeddedSection(closure, out string embedError);
                if (closure.Count > 0 && embedError.Length > 0)
                {
                    result.Diagnostics.Add(new MauDiagnostic("E901", 0, embedError));
                    result.Success = false;
                    return result;
                }
                if (embedded.Length > 0)
                {
                    skeleton = MergeEmbedded(skeleton, embedded);
                }
            }
            result.GeneratedCode = skeleton;
            result.Success = true;
            return result;
        }/// <summary>
/// 剥离内嵌积木段（BRIKGROUP）——黄金文件只存纯生成内容（E1：积木段由积木谱独立验证，不进黄金）
/// </summary>
/// <param name = "generatedCode">完整生成物（含内嵌段）</param>
/// <returns>剥离后源码——骨架纯生成内容</returns>
public static string StripBrickSectionsV2(string generatedCode)
{
    generatedCode = generatedCode.Replace("\r\n", "\n");
    string[] lines = generatedCode.Split('\n');
    StringBuilder sb = new StringBuilder();
    bool inBrick = false;
    for (int i = 0; i < lines.Length; i++)
    {
        string line = lines[i];
        string trimmed = line.Trim();
        if (!inBrick && trimmed.StartsWith("// #BRICK:", StringComparison.Ordinal) && trimmed.Contains("BEGIN", StringComparison.Ordinal))
        {
            inBrick = true;
            continue;
        }

        if (inBrick)
        {
            if (trimmed.StartsWith("// #BRICK:", StringComparison.Ordinal) && trimmed.Contains("END", StringComparison.Ordinal))
            {
                inBrick = false;
            }

            continue;
        }

        sb.Append(line);
        sb.Append('\n');
    }

    return sb.ToString().TrimEnd('\n');
}/// <summary>
/// 合并内嵌段——BRIKGROUP 的 using 提取到骨架 using 区（C# using 必须位于所有 namespace 之前）
/// </summary>
/// <param name = "skeleton">骨架源码</param>
/// <param name = "embedded">内嵌段（using + namespace 块）</param>
/// <returns>合并后源码</returns>
private static string MergeEmbedded(string skeleton, string embedded)
{
    // [段1] 提取内嵌段 using 行
    List<string> usings = new List<string>();
    string[] embeddedLines = embedded.Split('\n');
    StringBuilder embeddedBody = new StringBuilder();
    bool headerSkipped = false;
    for (int i = 0; i < embeddedLines.Length; i++)
    {
        string line = embeddedLines[i];
        string trimmed = line.Trim();
        if (!headerSkipped)
        {
            if (trimmed.StartsWith("using ", StringComparison.Ordinal))
            {
                usings.Add(line);
                continue;
            }

            if (trimmed.StartsWith("// ═══", StringComparison.Ordinal))
            {
                continue;
            }

            if (trimmed.Length == 0)
            {
                continue;
            }

            headerSkipped = true;
        }

        embeddedBody.Append(line);
        if (i < embeddedLines.Length - 1)
        {
            embeddedBody.Append('\n');
        }
    }

    // [段2] 提取骨架 using 区结束位置——头部注释行跳过，首个非注释非空行判定
    string[] skeletonLines = skeleton.Split('\n');
    int insertAt = 0;
    for (int i = 0; i < skeletonLines.Length; i++)
    {
        string trimmed = skeletonLines[i].Trim();
        if (trimmed.StartsWith("using ", StringComparison.Ordinal))
        {
            insertAt = i + 1;
            continue;
        }

        if (trimmed.StartsWith("//", StringComparison.Ordinal))
        {
            continue;
        }

        if (trimmed.Length == 0)
        {
            continue;
        }

        break;
    }

    // [段3] 拼接——骨架 using 区 + 内嵌 using（去重）+ 骨架其余 + 内嵌 body
    StringBuilder sb = new StringBuilder();
    for (int i = 0; i < insertAt; i++)
    {
        sb.Append(skeletonLines[i]);
        sb.Append('\n');
    }

    for (int u = 0; u < usings.Count; u++)
    {
        string candidate = usings[u].Trim();
        bool exists = false;
        for (int i = 0; i < insertAt; i++)
        {
            if (skeletonLines[i].Trim() == candidate)
            {
                exists = true;
                break;
            }
        }

        if (!exists)
        {
            sb.Append(usings[u]);
            sb.Append('\n');
        }
    }

    for (int i = insertAt; i < skeletonLines.Length; i++)
    {
        sb.Append(skeletonLines[i]);
        sb.Append('\n');
    }

    sb.Append(embeddedBody.ToString());
    return sb.ToString();
}    }
}
