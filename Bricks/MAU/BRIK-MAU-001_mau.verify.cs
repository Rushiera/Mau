// ═══════════════════════════════════════════════════
// 积木: mau.verify
// ID:   BRIK-MAU-001
// 类别: MAU
// 作用: Mau 语料全链检查（词法→解析→验证→分析）——LLM 工具 mau-verify 语料执行面（P8 二期 dev_cat）
// 依赖: 无
// 引用: Mau.Runtime · Mau.Development（MauProjFile/MauGroupBuilder）· Mau.Translator（MauCompilerV3）
// 原理: 仓库根内路径 → MauCompilerV3.Compile 进程内直调 → 诊断格式化（文件:行:错误码:消息）；零产出
// 常用: dev_cat.mau 认领线——'mau.verify'[@args] > @result
// ═══════════════════════════════════════════════════
using System;
using System.Text;
using System.Text.Json;
using Mau.Development;
using Mau.Runtime;
using Mau.Translator;

namespace Mau.Bricks
{
    /// <summary>
    /// Mau 自查积木——mau-verify 全链检查（LLM 工具执行面：参数整包 argsJson）
    /// </summary>
    public static class MauVerifyBrick
    {
        /// <summary>
        /// Mau 语料全链检查——零产出
        /// </summary>
        /// <param name="argsJson">工具参数 JSON（file）</param>
        /// <param name="result">诊断文本或 ERR| 错误文本</param>
        /// <returns>true=验证通过</returns>
        public static bool Verify(string argsJson, out string result)
        {
            result = "";
            string file = ExtractArg(argsJson, "file");
            if (file.Length == 0)
            {
                result = "ERR|BAD_ARGS|缺少参数 file";
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
                string abs = ResolveRepoPath(root, file);
                if (abs.Length == 0)
                {
                    result = "ERR|PATH_ESCAPE|路径越界（仅允许仓库根内）: " + file;
                    return false;
                }
                if (!System.IO.File.Exists(abs))
                {
                    result = "ERR|NOT_FOUND|文件不存在: " + abs;
                    return false;
                }
                string source = System.IO.File.ReadAllText(abs);
                string flowName = MauGroupBuilder.FlowNameFromPath(abs);
                CompileResultV3 r = MauCompilerV3.Compile(source, flowName);
                if (r.Success)
                {
                    StringBuilder sb = new StringBuilder();
                    sb.Append("OK 验证通过: " + file + " → FL_" + flowName);
                    for (int i = 0; i < r.Reports.Count; i++)
                    {
                        sb.Append(Environment.NewLine);
                        sb.Append("报告: " + r.Reports[i]);
                    }
                    result = sb.ToString();
                    return true;
                }
                StringBuilder err = new StringBuilder();
                err.Append("FAIL|VALIDATE|" + file);
                for (int i = 0; i < r.Diagnostics.Count; i++)
                {
                    MauDiagnostic d = r.Diagnostics[i];
                    err.Append(Environment.NewLine);
                    err.Append(file + ":" + d.Line + ": " + d.Code + ": " + d.Message);
                }
                result = err.ToString();
                return false;
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
        /// 路径解析——受控根前缀（id:relative）走通用接口解码（对齐 text-*/cs-* 寻址约定），无前缀按仓库根拼接；统一验证在仓库根内
        /// </summary>
        /// <param name="root">仓库根</param>
        /// <param name="relPath">路径参数（支持 mau: 前缀）</param>
        /// <returns>绝对路径（越界返回空串）</returns>
        private static string ResolveRepoPath(string root, string relPath)
        {
            string full;
            int nsSep = relPath.IndexOf(':');
            if (nsSep > 0)
            {
                // 受控根前缀——通用解码接口（FileSystemService.Resolve：id 映射 + 越界 + 只读根；盘符 C:\ 无匹配 id 原样回落）
                FileSystemService fs;
                if (!DataBox.TryResolve<FileSystemService>(out fs))
                {
                    return "";
                }
                try
                {
                    full = fs.Resolve(relPath, false);
                }
                catch (Exception)
                {
                    return "";
                }
            }
            else
            {
                full = System.IO.Path.GetFullPath(System.IO.Path.Combine(root, relPath));
            }
            if (!full.StartsWith(root + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(full, root, StringComparison.OrdinalIgnoreCase))
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
// #MAU_CHECKSUM:SHA256:6492D6B78A2F672DBC78FDD7D5A26679043B3887B526D6C2F6160E2698166F8B
