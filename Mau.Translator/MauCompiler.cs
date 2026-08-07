using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

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
        /// 生成代码——成功时可用（单文件编译含内嵌；组模式为骨架）
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
    /// 组编译结果——多语料骨架数组 + 共享 BRIKGROUP 段（组模式：多 .mau → 一 dll）
    /// </summary>
    public sealed class GroupCompileResult
    {
        /// <summary>
        /// 各语料编译结果（GeneratedCode = 骨架，不含内嵌）
        /// </summary>
        public CompileResult[] Results;

        /// <summary>
        /// 共享 BRIKGROUP 段——全组闭包并集（调用方作为额外 source 传入 CompileMany）
        /// </summary>
        public string BrickGroupSource;

        /// <summary>
        /// 构造空结果
        /// </summary>
        public GroupCompileResult()
        {
            Results = Array.Empty<CompileResult>();
            BrickGroupSource = "";
        }
    }

    /// <summary>
    /// Mau 编译器门面——解析 → IR → 静态验证 → 生成（单文件/组模式双入口）
    /// </summary>
    public static class MauCompiler
    {
        /// <summary>
        /// 编译 Mau 源文本（单文件——骨架 + 内嵌 BRIKGROUP）
        /// </summary>
        /// <param name="sourceText">Mau 源文本</param>
        /// <param name="flowName">流程名——PascalCase，用于生成类名</param>
        /// <returns>编译结果</returns>
        public static CompileResult Compile(string sourceText, string flowName)
        {
            return CompileCore(sourceText, flowName, true);
        }

        /// <summary>
        /// 组编译——多语料骨架 + 共享 BRIKGROUP（组模式：多 .mau → 一 dll，闭包去重）
        /// </summary>
        /// <param name="sourceTexts">语料数组</param>
        /// <param name="flowNames">流程名数组（与语料一一对应）</param>
        /// <returns>组编译结果</returns>
        public static GroupCompileResult CompileGroup(string[] sourceTexts, string[] flowNames)
        {
            GroupCompileResult group = new GroupCompileResult();
            if (!EnsureIndexLoaded())
            {
                CompileResult fail = new CompileResult();
                fail.Diagnostics.Add(new MauDiagnostic("E900", 0, "积木索引不可用——未找到 Bricks/index.json（环境变量 MAU_BRICKS_ROOT 或仓库根）"));
                group.Results = new CompileResult[] { fail };
                return group;
            }
            if (sourceTexts == null || sourceTexts.Length == 0)
            {
                return group;
            }
            CompileResult[] results = new CompileResult[sourceTexts.Length];
            // [段1] 逐语料编译（骨架，不含内嵌）
            for (int i = 0; i < sourceTexts.Length; i++)
            {
                string flowName = i < flowNames.Length ? flowNames[i] : ("Flow" + (i + 1).ToString());
                results[i] = CompileCore(sourceTexts[i], flowName, false);
                if (!results[i].Success)
                {
                    group.Results = results;
                    return group;
                }
            }
            // [段2] 闭包并集——全组动作积木去重
            Dictionary<string, BrickIndexEntry> closure =
                new Dictionary<string, BrickIndexEntry>(StringComparer.Ordinal);
            for (int i = 0; i < results.Length; i++)
            {
                List<BrickIndexEntry> docClosure = BrickEmbedder.CollectClosure(results[i].Document);
                for (int c = 0; c < docClosure.Count; c++)
                {
                    if (!closure.ContainsKey(docClosure[c].Id))
                    {
                        closure[docClosure[c].Id] = docClosure[c];
                    }
                }
            }
            List<BrickIndexEntry> sorted = new List<BrickIndexEntry>(closure.Values);
            sorted.Sort(delegate (BrickIndexEntry a, BrickIndexEntry b)
            {
                return string.CompareOrdinal(a.Id, b.Id);
            });
            // [段3] 共享 BRIKGROUP 段
            group.BrickGroupSource = BrickEmbedder.BuildEmbeddedSection(sorted, out string embedError);
            if (sorted.Count > 0 && embedError.Length > 0)
            {
                CompileResult fail = new CompileResult();
                fail.Diagnostics.Add(new MauDiagnostic("E901", 0, embedError));
                group.Results = new CompileResult[] { fail };
                return group;
            }
            group.Results = results;
            return group;
        }

        /// <summary>
        /// 编译核心——解析 → 验证 → 生成骨架；includeEmbedded 时拼接内嵌 BRIKGROUP
        /// </summary>
        /// <param name="sourceText">Mau 源文本</param>
        /// <param name="flowName">流程名</param>
        /// <param name="includeEmbedded">是否内嵌积木源码（单文件 true；组模式 false——共享段）</param>
        /// <returns>编译结果</returns>
        private static CompileResult CompileCore(string sourceText, string flowName, bool includeEmbedded)
        {
            CompileResult result = new CompileResult();

            // [段0] 积木索引——编译前置：翻译器构筑期契约/闭包唯一数据源（注册表退役）
            if (!EnsureIndexLoaded())
            {
                result.Diagnostics.Add(new MauDiagnostic("E900", 0, "积木索引不可用——未找到 Bricks/index.json（环境变量 MAU_BRICKS_ROOT 或仓库根）"));
                return result;
            }

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

            // [段3b] 内嵌积木源码（BRIKGROUP）——翻译器产物完备：积木是文本语料，复制即单包
            if (includeEmbedded)
            {
                List<BrickIndexEntry> closure = BrickEmbedder.CollectClosure(parsed.Document);
                string embedded = BrickEmbedder.BuildEmbeddedSection(closure, out string embedError);
                if (closure.Count > 0 && embedError.Length > 0)
                {
                    result.Diagnostics.Add(new MauDiagnostic("E901", 0, embedError));
                    return result;
                }
                if (embedded.Length > 0)
                {
                    result.GeneratedCode = MergeEmbedded(result.GeneratedCode, embedded);
                }
            }
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

        /// <summary>
        /// 合并内嵌段——BRIKGROUP 的 using 提取到骨架 using 区（C# using 必须位于所有 namespace 之前）
        /// </summary>
        /// <param name="skeleton">骨架源码</param>
        /// <param name="embedded">内嵌段（using + namespace 块）</param>
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
            // [段2] 提取骨架 using 区结束位置（首个非 using 非空行）
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
        }

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
        }
    }
}
