// ═══════════════════════════════════════════════════
// 积木: mau.gen
// ID:   BRIK-MAU-002
// 类别: MAU
// 作用: 组翻译中间产物——.mauproj 组声明 → 验证全组 + BRIKGROUP.cs + FL_*.cs（不编译）——LLM 工具 mau-gen 语料执行面
// 依赖: 无
// 引用: Mau.Runtime · Mau.Development（MauProjFile/MauGroupBuilder）
// 原理: 仓库根内路径 → MauProjFile.Load → MauGroupBuilder.Build(doBuild=false) → 步骤/诊断文本
// 常用: dev_cat.mau 认领线——'mau.gen'[@args] > @result
// ═══════════════════════════════════════════════════
using System;
using System.Text;
using System.Text.Json;
using Mau.Development;

namespace Mau.Bricks
{
    /// <summary>
    /// Mau 自查积木——mau-gen 组翻译中间产物（LLM 工具执行面：参数整包 argsJson）
    /// </summary>
    public static class MauGenBrick
    {
        /// <summary>
        /// 组翻译——中间产物落 public/src/&lt;组&gt;/（不编译）
        /// </summary>
        /// <param name="argsJson">工具参数 JSON（proj）</param>
        /// <param name="result">步骤/诊断文本或 ERR| 错误文本</param>
        /// <returns>true=翻译成功</returns>
        public static bool Gen(string argsJson, out string result)
        {
            return RunGroupBuild(argsJson, false, out result);
        }

        /// <summary>
        /// 组翻译共享执行——路径解析 → MauProjFile.Load → MauGroupBuilder.Build
        /// </summary>
        /// <param name="argsJson">工具参数 JSON（proj/build）</param>
        /// <param name="doBuild">true=翻译后 dotnet build</param>
        /// <param name="result">结果文本（步骤日志 + 失败诊断）</param>
        /// <returns>true=成功</returns>
        private static bool RunGroupBuild(string argsJson, bool doBuild, out string result)
        {
            result = "";
            string projParam = ExtractArg(argsJson, "proj");
            if (projParam.Length == 0)
            {
                result = "ERR|BAD_ARGS|缺少参数 proj";
                return false;
            }
            try
            {
                string root = RepoRoot();
                if (root.Length == 0)
                {
                    result = "ERR|NO_REPO|未找到仓库根（Mau.sln 向上探测）";
                    return false;
                }
                string abs = ResolveRepoPath(root, projParam);
                if (abs.Length == 0)
                {
                    result = "ERR|PATH_ESCAPE|路径越界（仅允许仓库根内）: " + projParam;
                    return false;
                }
                if (!System.IO.File.Exists(abs))
                {
                    result = "ERR|NOT_FOUND|文件不存在: " + abs;
                    return false;
                }
                MauProjParseResult parsed = MauProjFile.Load(abs);
                if (parsed.Error.Length > 0)
                {
                    result = "FAIL|MAUPROJ|" + parsed.Error;
                    return false;
                }
                MauProjFile proj = parsed.File!;
                string srcDir = System.IO.Path.Combine(root, "public", "src", proj.Name);
                string dllDir = System.IO.Path.Combine(root, "public", "app", "Flows");
                MauGroupBuildResult r = MauGroupBuilder.Build(abs, srcDir, dllDir, doBuild);
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < r.Steps.Count; i++)
                {
                    if (i > 0)
                    {
                        sb.Append(Environment.NewLine);
                    }
                    sb.Append(r.Steps[i]);
                }
                if (!r.Success)
                {
                    for (int i = 0; i < r.FailDiagnostics.Count; i++)
                    {
                        sb.Append(Environment.NewLine);
                        sb.Append(r.FailDiagnostics[i]);
                    }
                    if (r.Error.Length > 0)
                    {
                        sb.Append(Environment.NewLine);
                        sb.Append("FAIL|GEN|" + r.Error);
                    }
                    result = sb.ToString();
                    return false;
                }
                result = sb.ToString();
                return true;
            }
            catch (Exception ex)
            {
                result = "ERR|" + ex.GetType().Name + "|" + ex.Message;
                return false;
            }
        }

        /// <summary>
        /// 仓库根——向上找 Mau.sln（MauProjFile.FindWorkspaceRoot 同源）
        /// </summary>
        /// <returns>仓库根绝对路径或空串</returns>
        private static string RepoRoot()
        {
            string? root = MauGroupBuilder.FindWorkspaceRoot();
            if (root == null)
            {
                return "";
            }
            return root;
        }

        /// <summary>
        /// 相对仓库根路径解析——拼接后防越界（GetFullPath 后必须仍在仓库根内）
        /// </summary>
        /// <param name="root">仓库根</param>
        /// <param name="relPath">相对仓库根路径</param>
        /// <returns>绝对路径（越界返回空串）</returns>
        private static string ResolveRepoPath(string root, string relPath)
        {
            string full = System.IO.Path.GetFullPath(System.IO.Path.Combine(root, relPath));
            if (!full.StartsWith(root + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                return "";
            }
            return full;
        }

        /// <summary>
        /// 展平参数提取——argsJson 中取字符串值（不存在返回空串）
        /// </summary>
        /// <param name="argumentsJson">参数 JSON</param>
        /// <param name="key">参数名</param>
        /// <returns>参数值</returns>
        private static string ExtractArg(string argumentsJson, string key)
        {
            try
            {
                JsonDocument doc = JsonDocument.Parse(argumentsJson);
                try
                {
                    if (doc.RootElement.TryGetProperty(key, out JsonElement el))
                    {
                        if (el.ValueKind == JsonValueKind.String)
                        {
                            return el.GetString() ?? "";
                        }
                        return el.GetRawText();
                    }
                }
                finally
                {
                    doc.Dispose();
                }
            }
            catch (Exception)
            {
            }
            return "";
        }
    }
}
// #MAU_CHECKSUM:SHA256:A897B518CE774570577D27CE8982FF827C0E4BC1CB8D7D20D317669571FE6CC9
