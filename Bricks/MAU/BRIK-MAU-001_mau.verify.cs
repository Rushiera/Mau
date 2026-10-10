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
            // [参数面] 声明面口径零容忍——未知 / 缺值一律 ERR|BAD_ARGS（catId 保留键放行）
            string badArgs = JsonArgs.Validate(argsJson, "file", "file", "", "");
            if (badArgs.Length > 0)
            {
                result = badArgs;
                return false;
            }
            string file = JsonArgs.Get(argsJson, "file");
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
                string abs = ResolveRepoPath(root, file, JsonArgs.Get(argsJson, "catId"));
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
                    // 结构化返回体（design-ch4-tools 附录）——首行 JSON 元数据头 + 正文定界
                    System.Collections.Generic.Dictionary<string, object> fields = new System.Collections.Generic.Dictionary<string, object>();
                    fields["file"] = file;
                    fields["flow"] = "FL_" + flowName;
                    fields["reports"] = r.Reports.Count;
                    result = MetaHead("mau-verify", true, fields) + "\n" + sb.ToString();
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
                // 结构化返回体（design-ch4-tools 附录）——首行 JSON 元数据头 + 正文定界
                System.Collections.Generic.Dictionary<string, object> errFields = new System.Collections.Generic.Dictionary<string, object>();
                errFields["file"] = file;
                errFields["flow"] = "FL_" + flowName;
                errFields["errors"] = r.Diagnostics.Count;
                result = MetaHead("mau-verify", false, errFields) + "\n" + err.ToString();
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
        /// 结构化元数据头——首行单行 JSON（ok/tool + 调用方字段；键序稳定 = 插入序）
        /// 约定（design-ch4-tools 附录）：返回体 = 首行 JSON 头 + 正文定界行（正文不塞进 JSON——避免转义膨胀）
        /// </summary>
        /// <param name="tool">工具名（mau-verify / mau-gen / mau-proj / mau-setup）</param>
        /// <param name="ok">成败（编译 / 验证是否通过）</param>
        /// <param name="fields">附加字段（按插入序输出）</param>
        /// <returns>单行 JSON</returns>
        private static string MetaHead(string tool, bool ok, System.Collections.Generic.Dictionary<string, object> fields)
        {
            System.Collections.Generic.Dictionary<string, object> head = new System.Collections.Generic.Dictionary<string, object>();
            head["ok"] = ok;
            head["tool"] = tool;
            foreach (System.Collections.Generic.KeyValuePair<string, object> kv in fields)
            {
                head[kv.Key] = kv.Value;
            }
            return JsonSerializer.Serialize(head);
        }
    }
}
// #MAU_CHECKSUM:SHA256:1DB06EEDEEA941BE629067148061022D31E68BA7B2381DE592EE8308D8F4B2F7
