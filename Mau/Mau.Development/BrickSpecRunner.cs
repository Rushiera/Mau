#nullable disable warnings
using System;
using System.IO;
using Mau.Runtime;
using Mau.Translator;

namespace Mau.Development
{
    /// <summary>
    /// 积木谱跑测结果——成功标志 + 汇总 + 明细
    /// </summary>
    public sealed class BrickSpecRunResult
    {
        /// <summary>
        /// 跑测是否全过
        /// </summary>
        public bool Success;

        /// <summary>
        /// 汇总文本
        /// </summary>
        public string Summary = "";

        /// <summary>
        /// 明细（失败诊断等）
        /// </summary>
        public string Details = "";
    }

    /// <summary>
    /// 积木谱跑测——P5 跑测闭环的共享实现（CLI 门禁段 + Runtime.Tests 回归共用一份逻辑）。
    /// brickflow.mau → Compile → 内嵌积木 → PocketCompiler Emit → FlowHandle ALC → DataBox 驱动 → 状态断言。
    /// Mau 用自己的积木测自己的生成物。
    /// </summary>
    public static class BrickSpecRunner
    {
        /// <summary>
        /// 跑测主链
        /// </summary>
        /// <param name="repoRoot">仓库根（Mau.sln 所在）</param>
        /// <returns>跑测结果</returns>
        public static BrickSpecRunResult Run(string repoRoot)
        {
            BrickSpecRunResult result = new BrickSpecRunResult();
            string mauPath = Path.Combine(repoRoot, "Mau", "Mau.Runtime.Tests", "fixtures", "brickflow", "brickflow.mau");
            if (!File.Exists(mauPath))
            {
                result.Summary = "跑测语料缺失: " + mauPath;
                return result;
            }
            string mau = File.ReadAllText(mauPath);
            // [段1] 编译——E4xx 积木门禁 + 强类型内嵌
            CompileResultV3 cr = MauCompilerV3.Compile(mau, "BrickFlow");
            if (!cr.Success)
            {
                result.Summary = "跑测语料编译失败";
                result.Details = FirstDiag(cr);
                return result;
            }
            if (!cr.GeneratedCode.Contains("ProbeSinkBrick.Sink(", StringComparison.Ordinal))
            {
                result.Summary = "生成物缺强类型积木调用（内嵌失败）";
                return result;
            }
            // [段2] Emit——PocketCompiler Roslyn 编译生成物（含内嵌积木）
            string pocketRoot = Path.Combine(Path.GetTempPath(), "mau_spec_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            try
            {
                MauPocketCompiler compiler = new MauPocketCompiler(pocketRoot);
                MauPocketCompileResult pr = compiler.Compile(cr.GeneratedCode, "FL_BrickFlow");
                if (!pr.Success)
                {
                    result.Summary = "生成物 Emit 失败";
                    result.Details = string.Join("\n", pr.Diagnostics);
                    return result;
                }
                // [段3] 运行——ALC 加载 + 事件驱动 + 状态断言
                DataBox.ResetSignals();
                using (FlowHandle handle = FlowHandle.Load(pr.AssemblyPath))
                {
                    IObservableFlow flow = handle.Flow;
                    DataBox.Signal("P_Go");
                    flow.Tick(1);
                    FlowStatusV3 status1 = flow.GetStatus();
                    if (Array.IndexOf(status1.StateLines, "S_Flow=Working") < 0)
                    {
                        result.Summary = "首转移断言失败——期望 S_Flow=Working，实际 " + string.Join(";", status1.StateLines);
                        return result;
                    }
                    DataBox.Signal("P_Go");
                    flow.Tick(2);
                    FlowStatusV3 status2 = flow.GetStatus();
                    if (Array.IndexOf(status2.StateLines, "S_Flow=Done") < 0)
                    {
                        result.Summary = "次转移断言失败——期望 S_Flow=Done，实际 " + string.Join(";", status2.StateLines);
                        return result;
                    }
                }
            }
            finally
            {
                DataBox.ResetSignals();
                if (Directory.Exists(pocketRoot))
                {
                    try
                    {
                        Directory.Delete(pocketRoot, true);
                    }
                    catch (Exception)
                    {
                        // 清理失败不影响
                    }
                }
            }
            result.Success = true;
            result.Summary = "积木谱跑测通过——brickflow: Idle → Working → Done（probe.sink 强类型直调真执行）";
            return result;
        }

        /// <summary>
        /// 首条诊断文本
        /// </summary>
        /// <param name="result">编译结果</param>
        /// <returns>诊断摘要</returns>
        private static string FirstDiag(CompileResultV3 result)
        {
            if (result.Diagnostics.Count > 0)
            {
                return result.Diagnostics[0].Code + ":" + result.Diagnostics[0].Message;
            }
            return "未知";
        }
    }
}
