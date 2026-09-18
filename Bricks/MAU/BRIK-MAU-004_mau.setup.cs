// ═══════════════════════════════════════════════════
// 积木: mau.setup
// ID:   BRIK-MAU-004
// 类别: MAU
// 作用: 一键部署链执行（SetUp.exe）——起进程并阻塞等待至结束，读 JSON 报告返回步明细/产物/成败；编译验证闭环
// 依赖: 无
// 引用: System.Diagnostics（进程）+ System.Text.Json + Mau.Development（仓库根探测）
// 原理: 仓库根 → SetUp.exe（prepare/deploy）+ --report → WaitForExit（内建看门狗）→ 读 <报告>.json 提炼摘要
// 常用: mau_cat.mau 认领线——'mau.setup'[@args] > @result（载荷 mode/target/report）
// ═══════════════════════════════════════════════════
#nullable disable warnings
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using Mau.Development;

namespace Mau.Bricks
{
    /// <summary>
    /// 一键部署积木——SetUp.exe 进程级执行面（阻塞等待 + 报告解析）。
    /// 设计意图：宿主（Flow）内不绕过任何写保护语义——部署动作全部由 SetUp.exe 自身完成，本积木只负责起进程、等待、读报告。
    /// </summary>
    public static class MauSetupBrick
    {
        /// <summary>
        /// 内建看门狗——最长等待毫秒（宿主侧 OA 超时映射须不小于本值）
        /// </summary>
        private const int WatchdogMs = 900000;

        /// <summary>
        /// 一键部署——mode=prepare（默认，就地自举 public/ + Mau-public/）| deploy（复制到目标目录）
        /// </summary>
        /// <param name="argsJson">工具参数 JSON（mode/target/report）</param>
        /// <param name="result">步骤摘要/诊断文本或 ERR| 错误文本</param>
        /// <returns>true=SetUp 退出码 0（报告 Ok=true）</returns>
        public static bool Setup(string argsJson, out string result)
        {
            result = "";
            // [参数面] 声明面口径零容忍——未知 / 非法 mode 一律 ERR|BAD_ARGS（catId 保留键放行）
            string badArgs = ValidateArgs(argsJson, "mode target report", "", "mode", "prepare|deploy");
            if (badArgs.Length > 0)
            {
                result = badArgs;
                return false;
            }
            string mode = ExtractArg(argsJson, "mode");
            if (mode.Length == 0)
            {
                mode = "prepare";
            }
            if (mode != "prepare" && mode != "deploy")
            {
                result = "ERR|BAD_ARGS|mode 仅支持 prepare/deploy（当前: " + mode + "）";
                return false;
            }
            string target = ExtractArg(argsJson, "target");
            if (mode == "deploy" && target.Length == 0)
            {
                result = "ERR|BAD_ARGS|deploy 模式需 target（目标目录）";
                return false;
            }
            try
            {
                string root = RepoRoot();
                if (root.Length == 0)
                {
                    result = "ERR|NO_REPO|未找到仓库根（Mau.sln 向上探测/MAU_ROOT）——一键部署需仓库根内 SetUp.exe";
                    return false;
                }
                string exe = Path.Combine(root, "SetUp.exe");
                if (!File.Exists(exe))
                {
                    result = "ERR|NOT_FOUND|SetUp.exe 不存在: " + exe;
                    return false;
                }
                string report = ExtractArg(argsJson, "report");
                if (report.Length == 0)
                {
                    report = Path.Combine(root, "CatTemp", "setup_report.json");
                }
                string reportDir = Path.GetDirectoryName(report);
                if (reportDir != null && reportDir.Length > 0)
                {
                    Directory.CreateDirectory(reportDir);
                }
                string logPath = report + ".log";
                string args = mode;
                if (mode == "deploy")
                {
                    args = args + " \"" + target + "\"";
                }
                args = args + " --report \"" + report + "\"";
                StringBuilder sb = new StringBuilder();
                sb.Append("SetUp " + mode + " 执行中（仓库根 " + root + "）");
                sb.Append(Environment.NewLine);
                sb.Append("报告: " + report);
                sb.Append(Environment.NewLine);
                int exitCode = RunProcess(exe, args, root, sb);
                if (exitCode == int.MinValue)
                {
                    result = sb.ToString();
                    return false;
                }
                int stepCount = 0;
                int stepOk = 0;
                int artifactCount = 0;
                string summary = ReadReport(report, exitCode, logPath, out stepCount, out stepOk, out artifactCount);
                sb.Append(summary);
                // 结构化返回体（design-ch4-tools 附录）——首行 JSON 元数据头 + 正文定界
                System.Collections.Generic.Dictionary<string, object> fields = new System.Collections.Generic.Dictionary<string, object>();
                fields["mode"] = mode;
                fields["target"] = (mode == "deploy") ? target : "";
                fields["exit"] = exitCode;
                fields["steps"] = stepCount;
                fields["stepsOk"] = stepOk;
                fields["artifacts"] = artifactCount;
                result = MetaHead("mau-setup", exitCode == 0, fields) + "\n" + sb.ToString();
                return exitCode == 0;
            }
            catch (Exception ex)
            {
                result = "ERR|" + ex.GetType().Name + "|" + ex.Message;
                return false;
            }
        }

        /// <summary>
        /// 起进程并阻塞等待——内建看门狗；超时强杀（进程树）后按失败返回
        /// </summary>
        /// <param name="exe">可执行文件路径</param>
        /// <param name="args">命令行参数</param>
        /// <param name="workDir">工作目录（SetUp 以当前目录找 Mau.sln）</param>
        /// <param name="sb">输出累积</param>
        /// <returns>退出码；看门狗超时返回 int.MinValue</returns>
        private static int RunProcess(string exe, string args, string workDir, StringBuilder sb)
        {
            ProcessStartInfo psi = new ProcessStartInfo(exe, args);
            psi.WorkingDirectory = workDir;
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            Process proc = Process.Start(psi);
            if (proc == null)
            {
                sb.Append("FAIL|START|进程启动失败（Process.Start 返回空）");
                sb.Append(Environment.NewLine);
                return int.MinValue;
            }
            bool exited = proc.WaitForExit(WatchdogMs);
            if (!exited)
            {
                try
                {
                    proc.Kill(true);
                }
                catch (Exception)
                {
                }
                sb.Append("FAIL|TIMEOUT|SetUp 超时（" + WatchdogMs.ToString() + " ms）已强杀");
                sb.Append(Environment.NewLine);
                return int.MinValue;
            }
            return proc.ExitCode;
        }

        /// <summary>
        /// 读报告 JSON——提炼 Ok/退出码/步明细/产物时间戳（读失败回落退出码结论）
        /// </summary>
        /// <param name="reportPath">报告文件路径</param>
        /// <param name="exitCode">进程退出码</param>
        /// <param name="logPath">同路径 .log 全量日志</param>
        /// <param name="stepCount">步总数（out）</param>
        /// <param name="stepOk">通过步数（out）</param>
        /// <param name="artifactCount">产物件数（out）</param>
        /// <returns>摘要文本</returns>
        private static string ReadReport(string reportPath, int exitCode, string logPath, out int stepCount, out int stepOk, out int artifactCount)
        {
            stepCount = 0;
            stepOk = 0;
            artifactCount = 0;
            StringBuilder sb = new StringBuilder();
            sb.Append("exit=" + exitCode.ToString());
            sb.Append(Environment.NewLine);
            if (!File.Exists(reportPath))
            {
                sb.Append("FAIL|NO_REPORT|报告未生成（进程异常退出？）——日志: " + logPath);
                sb.Append(Environment.NewLine);
                return sb.ToString();
            }
            try
            {
                string text = File.ReadAllText(reportPath);
                JsonDocument doc = JsonDocument.Parse(text);
                try
                {
                    JsonElement rootEl = doc.RootElement;
                    string ok = "?";
                    string ts = "";
                    JsonElement v;
                    if (rootEl.TryGetProperty("Ok", out v))
                    {
                        ok = v.ValueKind == JsonValueKind.True ? "true" : "false";
                    }
                    if (rootEl.TryGetProperty("Ts", out v) && v.ValueKind == JsonValueKind.String)
                    {
                        ts = v.GetString();
                    }
                    sb.Append("Ok=" + ok + (ts.Length > 0 ? " | 报告时间 " + ts : ""));
                    sb.Append(Environment.NewLine);
                    JsonElement steps;
                    if (rootEl.TryGetProperty("Steps", out steps) && steps.ValueKind == JsonValueKind.Array)
                    {
                        int i = 0;
                        foreach (JsonElement step in steps.EnumerateArray())
                        {
                            i = i + 1;
                            string line = StepText(step);
                            if (line.Length > 0)
                            {
                                sb.Append("  " + line);
                                sb.Append(Environment.NewLine);
                            }
                            JsonElement okFlag;
                            if (step.ValueKind == JsonValueKind.Object && step.TryGetProperty("Ok", out okFlag) && okFlag.ValueKind == JsonValueKind.True)
                            {
                                stepOk = stepOk + 1;
                            }
                        }
                        stepCount = i;
                        sb.Append("步数: " + i.ToString());
                        sb.Append(Environment.NewLine);
                    }
                    JsonElement arts;
                    if (rootEl.TryGetProperty("Artifacts", out arts) && arts.ValueKind == JsonValueKind.Array)
                    {
                        int n = 0;
                        foreach (JsonElement a in arts.EnumerateArray())
                        {
                            n = n + 1;
                        }
                        artifactCount = n;
                        sb.Append("产物: " + n.ToString() + " 件（时间戳见报告 JSON）");
                        sb.Append(Environment.NewLine);
                    }
                }
                finally
                {
                    doc.Dispose();
                }
            }
            catch (Exception ex)
            {
                sb.Append("WARN|REPORT_PARSE|" + ex.GetType().Name + "|" + ex.Message);
                sb.Append(Environment.NewLine);
            }
            sb.Append("日志: " + logPath);
            return sb.ToString();
        }

        /// <summary>
        /// 步明细文本——字符串直接取；对象取 name/step/detail/ok 拼接
        /// </summary>
        /// <param name="step">Steps 数组元素</param>
        /// <returns>行文本</returns>
        private static string StepText(JsonElement step)
        {
            if (step.ValueKind == JsonValueKind.String)
            {
                string s = step.GetString();
                return s == null ? "" : s;
            }
            if (step.ValueKind != JsonValueKind.Object)
            {
                return step.GetRawText();
            }
            StringBuilder sb = new StringBuilder();
            JsonElement v;
            if (step.TryGetProperty("Name", out v) && v.ValueKind == JsonValueKind.String)
            {
                sb.Append(v.GetString());
            }
            else if (step.TryGetProperty("name", out v) && v.ValueKind == JsonValueKind.String)
            {
                sb.Append(v.GetString());
            }
            if (step.TryGetProperty("Ok", out v))
            {
                sb.Append(v.ValueKind == JsonValueKind.True ? " [OK]" : " [FAIL]");
            }
            if (step.TryGetProperty("Detail", out v) && v.ValueKind == JsonValueKind.String)
            {
                sb.Append(" " + v.GetString());
            }
            else if (step.TryGetProperty("detail", out v) && v.ValueKind == JsonValueKind.String)
            {
                sb.Append(" " + v.GetString());
            }
            if (sb.Length == 0)
            {
                return step.GetRawText();
            }
            return sb.ToString();
        }

        /// <summary>
        /// 仓库根——注入根 → MAU_ROOT → 当前目录向上 → 程序集位置兜底（MauGroupBuilder 同源）
        /// </summary>
        /// <returns>仓库根绝对路径或空串</returns>
        private static string RepoRoot()
        {
            string root = MauGroupBuilder.FindWorkspaceRoot();
            if (root == null)
            {
                return "";
            }
            return root;
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
        /// <param name="required">必填键（空格分隔；空=无必填）</param>
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
                    if (required.Length > 0)
                    {
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

        /// <summary>
        /// 结构化元数据头——首行单行 JSON（ok/tool + 调用方字段；键序稳定 = 插入序）
        /// 约定（design-ch4-tools 附录）：返回体 = 首行 JSON 头 + 正文定界行（正文不塞进 JSON——避免转义膨胀）
        /// </summary>
        /// <param name="tool">工具名（mau-verify / mau-gen / mau-proj / mau-setup）</param>
        /// <param name="ok">成败（部署链成败）</param>
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

        private static string ExtractArg(string argumentsJson, string key)
        {
            try
            {
                JsonDocument doc = JsonDocument.Parse(argumentsJson);
                try
                {
                    JsonElement el;
                    if (doc.RootElement.TryGetProperty(key, out el))
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
// #MAU_CHECKSUM:SHA256:861EA6FD4AAE5A56F3E243C7825682B41E6C96C665E8EFFE1E2FEDD606E230A0
