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
            string badArgs = JsonArgs.Validate(argsJson, "proj", "proj", "", "");
            if (badArgs.Length > 0)
            {
                result = badArgs;
                return false;
            }
            string projParam = JsonArgs.Get(argsJson, "proj");
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
                string abs = ResolveRepoPath(root, projParam, JsonArgs.Get(argsJson, "catId"));
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
                sb.Append(proj.Name + " | " + r.Steps.Count.ToString() + " 步");
                for (int i = 0; i < r.Steps.Count; i++)
                {
                    sb.Append(Environment.NewLine);
                    sb.Append(r.Steps[i]);
                }
                // 结构化返回体（design-ch4-tools 附录 · A214）——首行 JSON 头（target/items）+ 正文摘要行
                System.Collections.Generic.Dictionary<string, object> fields = new System.Collections.Generic.Dictionary<string, object>();
                fields["errors"] = r.FailDiagnostics.Count;
                fields["build"] = doBuild;
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
                    result = MetaHead("mau-gen", false, proj.Name, r.Steps.Count, fields) + "\n" + sb.ToString();
                    return false;
                }
                result = MetaHead("mau-gen", true, proj.Name, r.Steps.Count, fields) + "\n" + sb.ToString();
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
        /// 路径解析——受控根前缀（id:relative）走统一寻址（猫级 fs 优先，对齐 text-*/file-* 约定），无前缀按仓库根拼接；统一验证在仓库根内
        /// </summary>
        /// <param name="root">仓库根</param>
        /// <param name="relPath">路径参数（支持 mau: 前缀）</param>
        /// <param name="catId">会话猫 key（catId 保留键；空=当前猫）</param>
        /// <returns>绝对路径（越界返回空串）</returns>
        private static string ResolveRepoPath(string root, string relPath, string catId)
        {
            string full;
            int nsSep = relPath.IndexOf(':');
            if (nsSep > 0)
            {
                // 受控根前缀——统一寻址（对齐 text-*/file-*）：猫级 fs（ResolveScoped：显式 catId → 当前猫）优先，回落全局 DataBox
                FileSystemService? fs = FileSystemRegistry.ResolveScoped(catId);
                if (fs == null)
                {
                    DataBox.TryResolve<FileSystemService>(out fs);
                }
                if (fs == null)
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
        /// <summary>
        /// 结构化元数据头——首行单行 JSON（ok/tool + 调用方字段；键序稳定 = 插入序）
        /// 约定（design-ch4-tools 附录）：返回体 = 首行 JSON 头 + 正文定界行（正文不塞进 JSON——避免转义膨胀）
        /// </summary>
        /// <param name="tool">工具名（mau-verify / mau-gen / mau-proj / mau-setup）</param>
        /// <param name="ok">成败（编译 / 验证是否通过）</param>
        /// <param name="target">主来源（文件 / 项目名；空串 = 省略）</param>
        /// <param name="items">主计数（负值 = 省略）</param>
        /// <param name="fields">附加字段（按插入序输出）</param>
        /// <returns>单行 JSON</returns>
        private static string MetaHead(string tool, bool ok, string target, int items, System.Collections.Generic.Dictionary<string, object> fields)
        {
            System.Collections.Generic.Dictionary<string, object> head = new System.Collections.Generic.Dictionary<string, object>();
            head["ok"] = ok;
            head["tool"] = tool;
            if (target.Length > 0)
            {
                head["target"] = target;
            }
            if (items >= 0)
            {
                head["items"] = items;
            }
            foreach (System.Collections.Generic.KeyValuePair<string, object> kv in fields)
            {
                head[kv.Key] = kv.Value;
            }
            return JsonSerializer.Serialize(head);
        }
    }
}
// #MAU_CHECKSUM:SHA256:B125E3668CE86C6F158608050A1801AFD41DAE172AAED5A89905A1976746FA74
