using System;
using System.IO;
using System.Collections.Generic;
using Mau.Translator;

namespace Mau.Cli
{
    /// <summary>
    /// mau check（v3 最小版）——语法谱全编译 + 负例谱正确拒绝 + 分析报告聚合。
    /// 门禁原则：LLM 表达，代码验真——负例谱验证分析门禁（E300/E302 等）真实工作。
    /// 目录即清单：cases/*.mau 全编译；checks/v3/n*.mau 负例（文件头 // 预期: E0xx 声明期望错误码）。
    /// 完整全谱（积木谱/黄金结构断言）随 P5 积木重生 + P6 工具链收拢扩展。
    /// </summary>
    public static class CommandCheckV3
    {
/// <summary>
/// 执行 check——语法谱 + 负例谱 + 关键路径报告 + 聚合汇总
/// </summary>
///

        ///
public static int Run()
{
    string? root = CliSupport.FindWorkspaceRoot();
    if (root == null)
    {
        Console.WriteLine("FAIL: 未找到 Mau.sln——请在 Mau workspace 下运行 mau check");
        return 1;
    }
    string casesDir = Path.Combine(root, "Mau.Snapshots", "cases");
    string negDir = Path.Combine(root, "Mau.Snapshots", "checks", "v3");
    int casePass = 0;
    int caseFail = 0;
    Dictionary<string, List<string>> reports = new Dictionary<string, List<string>>();
    // [段1] 语法谱——目录即清单全编译（报告级分析产出随谱收集）
    if (Directory.Exists(casesDir))
    {
        string[] files = Directory.GetFiles(casesDir, "*.mau");
        Array.Sort(files, StringComparer.Ordinal);
        for (int i = 0; i < files.Length; i++)
        {
            string name = Path.GetFileName(files[i]);
            string flowName = Program.FlowNameFromPath(files[i]);
            string source = File.ReadAllText(files[i]);
            CompileResultV3 result = MauCompilerV3.Compile(source, flowName);
            if (result.Success)
            {
                casePass = casePass + 1;
                CliSupport.Detail("OK " + name);
                if (result.Reports.Count > 0)
                {
                    reports[name] = result.Reports;
                }
            }
            else
            {
                caseFail = caseFail + 1;
                Console.WriteLine("FAIL: " + name + " 编译失败");
                for (int d = 0; d < result.Diagnostics.Count; d++)
                {
                    Console.WriteLine("  " + result.Diagnostics[d].Code + ": " + result.Diagnostics[d].Message);
                }
            }
        }
    }
    else
    {
        Console.WriteLine("WARN: 语法谱目录不存在——" + casesDir);
    }
    // [段2] 负例谱——正确拒绝验证
    int negPass = 0;
    int negFail = 0;
    if (Directory.Exists(negDir))
    {
        string[] files = Directory.GetFiles(negDir, "*.mau");
        Array.Sort(files, StringComparer.Ordinal);
        for (int i = 0; i < files.Length; i++)
        {
            string name = Path.GetFileName(files[i]);
            string flowName = Program.FlowNameFromPath(files[i]);
            string source = File.ReadAllText(files[i]);
            string expected = ExpectedErrorCode(source);
            CompileResultV3 result = MauCompilerV3.Compile(source, flowName);
            if (result.Success)
            {
                negFail = negFail + 1;
                Console.WriteLine("FAIL: " + name + " 应被拒绝（预期 " + expected + "）但编译通过");
                continue;
            }
            bool hit = false;
            for (int d = 0; d < result.Diagnostics.Count; d++)
            {
                if (result.Diagnostics[d].Code == expected)
                {
                    hit = true;
                    break;
                }
            }
            if (hit)
            {
                negPass = negPass + 1;
                CliSupport.Detail("OK " + name + " → " + expected);
            }
            else
            {
                negFail = negFail + 1;
                Console.WriteLine("FAIL: " + name + " 期望 " + expected + " 但诊断为 " + FirstCode(result));
            }
        }
    }
    else
    {
        Console.WriteLine("WARN: 负例谱目录不存在——" + negDir);
    }
    // [段3] 聚合报告
    Console.WriteLine("=== MAU_CHECK 汇总 ===");
    Console.WriteLine("[A·L1] 语法谱 " + casePass + "/" + (casePass + caseFail) + " 通过（" + caseFail + " 失败）");
    Console.WriteLine("[D·L2] 负例谱 " + negPass + "/" + (negPass + negFail) + " 正确拒绝（" + negFail + " 未达预期）");
    // [段4] 关键路径报告——报告级分析产出（排错定位）
    if (reports.Count > 0)
    {
        Console.WriteLine("[R] 关键路径报告:");
        string[] reportNames = new string[reports.Count];
        reports.Keys.CopyTo(reportNames, 0);
        Array.Sort(reportNames, StringComparer.Ordinal);
        for (int i = 0; i < reportNames.Length; i++)
        {
            List<string> list = reports[reportNames[i]];
            for (int r = 0; r < list.Count; r++)
            {
                Console.WriteLine("  " + reportNames[i] + " → " + list[r]);
            }
        }
    }
    int totalFail = caseFail + negFail;
    if (totalFail == 0)
    {
        Console.WriteLine("MAU_CHECK_OK");
        return 0;
    }
    Console.WriteLine("MAU_CHECK_FAIL");
    return 1;
}
        /// <summary>
        /// 负例预期错误码——扫描文件头 // 预期: E0xx
        /// </summary>
        /// <param name="source">负例源文本</param>
        /// <returns>错误码（无则空）</returns>
        private static string ExpectedErrorCode(string source)
        {
            string[] lines = source.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.StartsWith("// 预期:", StringComparison.Ordinal))
                {
                    return line.Substring("// 预期:".Length).Trim();
                }
            }
            return "";
        }

        /// <summary>
        /// 首条诊断错误码
        /// </summary>
        /// <param name="result">编译结果</param>
        /// <returns>错误码文本</returns>
        private static string FirstCode(CompileResultV3 result)
        {
            if (result.Diagnostics.Count > 0)
            {
                return result.Diagnostics[0].Code;
            }
            return "未知";
        }
    }
}
