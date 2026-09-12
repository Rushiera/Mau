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
using Mau.Runtime;

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
            // [参数面] 声明面口径零容忍——未知 / 缺值一律 ERR|BAD_ARGS（catId 保留键放行）
            string badArgs = ValidateArgs(argsJson, "proj", "proj", "", "");
            if (badArgs.Length > 0)
            {
                result = badArgs;
                return false;
            }
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
        /// <summary>
        /// 参数面校验——声明面口径零容忍：未知参数 / 必填缺值 / 非法枚举值一律 ERR|BAD_ARGS（宿主注入保留键 catId 放行）。
        /// </summary>
        /// <param name="argsJson">工具参数 JSON</param>
        /// <param name="allowed">允许键（空格分隔）</param>
        /// <param name="required">必填键（空格分隔）</param>
        /// <param name="enumName">枚举参数名（空=无）</param>
        /// <param name="enumValues">枚举合法值（| 分隔）</param>
        /// <returns>错误文本（空=通过）</returns>
        private static string ValidateArgs(string argsJson, string allowed, string required, string enumName, string enumValues)
        {
            if (argsJson == null || argsJson.Length == 0)
            {
                return "ERR|BAD_ARGS|工具参数 JSON 解析失败（LLM 生成参数可能被截断——超长内容请分段写入）";
            }
            try
            {
                JsonDocument doc = JsonDocument.Parse(argsJson);
                try
                {
                    JsonElement root = doc.RootElement;
                    if (root.ValueKind != JsonValueKind.Object)
                    {
                        return "ERR|BAD_ARGS|参数必须是 JSON 对象";
                    }
                    foreach (JsonProperty property in root.EnumerateObject())
                    {
                        if (property.Name == "catId")
                        {
                            continue;
                        }
                        if ((" " + allowed + " ").IndexOf(" " + property.Name + " ", StringComparison.Ordinal) < 0)
                        {
                            return "ERR|BAD_ARGS|未知参数: " + property.Name + "（支持 " + allowed + "）";
                        }
                    }
                    string[] must = required.Split(' ');
                    for (int i = 0; i < must.Length; i = i + 1)
                    {
                        JsonElement mustValue;
                        if (!root.TryGetProperty(must[i], out mustValue) ||
                            (mustValue.ValueKind == JsonValueKind.String && (mustValue.GetString() ?? "").Length == 0))
                        {
                            return "ERR|BAD_ARGS|缺参数 " + must[i] + "（必填：" + required + "）";
                        }
                    }
                    if (enumName.Length > 0)
                    {
                        JsonElement enumValue;
                        if (root.TryGetProperty(enumName, out enumValue) && enumValue.ValueKind == JsonValueKind.String)
                        {
                            string value = enumValue.GetString() ?? "";
                            if (value.Length > 0 && ("|" + enumValues + "|").IndexOf("|" + value + "|", StringComparison.Ordinal) < 0)
                            {
                                return "ERR|BAD_ARGS|" + enumName + " 非法值: " + value + "（" + enumValues + "）";
                            }
                        }
                    }
                    return "";
                }
                finally
                {
                    doc.Dispose();
                }
            }
            catch (Exception ex)
            {
                return "ERR|BAD_ARGS|参数 JSON 解析失败: " + ex.Message;
            }
        }

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
// #MAU_CHECKSUM:SHA256:B09EB6CD54753190F37CFA5F726E67507EABC5BD1C4F14442011A3DF29275A7A
