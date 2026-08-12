using System;
using System.Collections.Generic;

namespace Mau.Translator
{
    /// <summary>
    /// 糖展开结果
    /// </summary>
    public sealed class ExpandResultV2
    {
        /// <summary>是否成功——无错误诊断</summary>
        public bool Success = true;

        /// <summary>诊断列表</summary>
        public List<MauDiagnostic> Diagnostics = new List<MauDiagnostic>();
    }

    /// <summary>
    /// 语法糖展开器——SEQ/PAR/FBK IR 层变换（验证前；展开产物与手写等价）
    /// </summary>
    public static class MauSugarExpanderV2
    {
        /// <summary>
        /// 展开入口——修改文档（加隐式机器/资源、改控制律条件/结果；清空糖列表）
        /// </summary>
        /// <param name="doc">解析文档</param>
        /// <returns>展开结果</returns>
        public static ExpandResultV2 Expand(MauDocV2 doc)
        {
            ExpandResultV2 result = new ExpandResultV2();
            if (doc == null)
            {
                AddError(result, "E210", "文档为空");
                return result;
            }

            for (int i = 0; i < doc.Sugars.Count; i++)
            {
                SugarV2 sugar = doc.Sugars[i];
                if (sugar.Name == "SEQ")
                {
                    ExpandSeq(doc, sugar, result);
                }
                else if (sugar.Name == "PAR")
                {
                    ExpandPar(doc, sugar, result);
                }
                else if (sugar.Name == "FBK")
                {
                    ExpandFbk(doc, sugar, result);
                }
                else
                {
                    AddError(result, "E210", "未知语法糖——'" + sugar.Name + "'（支持 SEQ/PAR/FBK）");
                }
                if (!result.Success)
                {
                    return result;
                }
            }
            doc.Sugars.Clear();
            result.Success = result.Diagnostics.Count == 0;
            return result;
        }

        /// <summary>
        /// SEQ 展开——隐式状态机 + 各控制律结果侧追加转移
        /// </summary>
        /// <param name="doc">文档</param>
        /// <param name="sugar">糖</param>
        /// <param name="result">展开结果</param>
        private static void ExpandSeq(MauDocV2 doc, SugarV2 sugar, ExpandResultV2 result)
        {
            if (sugar.RefNames.Count < 2)
            {
                AddError(result, "E210", "SEQ 至少需要 2 个控制律引用");
                return;
            }
            // 隐式状态机——S_Seq = { Step1..StepN, Done }
            string seqName = "S_Seq";
            int seqNo = 1;
            while (doc.FindMachine(seqName) != null)
            {
                seqName = "S_Seq" + seqNo.ToString();
                seqNo = seqNo + 1;
            }
            MachineV2 machine = new MachineV2();
            machine.Name = seqName;
            for (int i = 0; i < sugar.RefNames.Count; i++)
            {
                machine.States.Add("Step" + (i + 1).ToString());
            }
            machine.States.Add("Done");
            doc.Machines.Add(machine);

            // 各控制律结果侧追加——T_i 成功 → S_Seq = Step_{i+1}
            for (int i = 0; i < sugar.RefNames.Count; i++)
            {
                LawV2? law = FindLaw(doc, sugar.RefNames[i]);
                if (law == null)
                {
                    AddError(result, "E210", "SEQ 引用控制律不存在——'" + sugar.RefNames[i] + "'");
                    return;
                }
                if (law.ResultKind != ResultKindV2.StateTransfer)
                {
                    AddError(result, "E210", "SEQ 引用控制律 '" + law.Name + "' 结果侧非状态转移——级联追加需要状态转移结果");
                    return;
                }
                if (i < sugar.RefNames.Count - 1)
                {
                    // 前 N-1 个——成功分支追加 → Step_{i+2}
                    law.Results[0].MachineName = law.Results[0].MachineName; // 原转移保留
                    ResultV2 append = new ResultV2();
                    append.Kind = ResultKindV2.StateTransfer;
                    append.MachineName = seqName;
                    append.StateName = "Step" + (i + 2).ToString();
                    law.Results.Add(append);
                }
                else
                {
                    // 最后一个——成功分支追加 → Done
                    ResultV2 append = new ResultV2();
                    append.Kind = ResultKindV2.StateTransfer;
                    append.MachineName = seqName;
                    append.StateName = "Done";
                    law.Results.Add(append);
                }
            }
        }

        /// <summary>
        /// PAR 展开——隐式资源配额 + 各控制律条件追加资源获取
        /// </summary>
        /// <param name="doc">文档</param>
        /// <param name="sugar">糖</param>
        /// <param name="result">展开结果</param>
        private static void ExpandPar(MauDocV2 doc, SugarV2 sugar, ExpandResultV2 result)
        {
            if (sugar.RefNames.Count < 2)
            {
                AddError(result, "E210", "PAR 至少需要 2 个控制律引用");
                return;
            }
            // 隐式资源——R_Parallel: N
            string resName = "R_Parallel";
            int resNo = 1;
            bool nameTaken = false;
            for (int i = 0; i < doc.Resources.Count; i++)
            {
                if (doc.Resources[i].Name == resName)
                {
                    nameTaken = true;
                    break;
                }
            }
            while (nameTaken)
            {
                resName = "R_Parallel" + resNo.ToString();
                resNo = resNo + 1;
                nameTaken = false;
                for (int i = 0; i < doc.Resources.Count; i++)
                {
                    if (doc.Resources[i].Name == resName)
                    {
                        nameTaken = true;
                        break;
                    }
                }
            }
            ResourceV2 resource = new ResourceV2();
            resource.Name = resName;
            resource.Quota = sugar.RefNames.Count;
            doc.Resources.Add(resource);

            // 各控制律条件追加 ∧ R_Parallel（并发上限）
            for (int i = 0; i < sugar.RefNames.Count; i++)
            {
                LawV2? law = FindLaw(doc, sugar.RefNames[i]);
                if (law == null)
                {
                    AddError(result, "E210", "PAR 引用控制律不存在——'" + sugar.RefNames[i] + "'");
                    return;
                }
                AppendResourceCond(law, resName);
            }
        }

        /// <summary>
        /// FBK 展开——隐式状态机 + 条件门控 + 失败回环
        /// </summary>
        /// <param name="doc">文档</param>
        /// <param name="sugar">糖</param>
        /// <param name="result">展开结果</param>
        private static void ExpandFbk(MauDocV2 doc, SugarV2 sugar, ExpandResultV2 result)
        {
            if (sugar.RefNames.Count != 3)
            {
                AddError(result, "E210", "FBK 需要 3 个引用——'M_测量' → 'T_控制律' → 'M_测量'");
                return;
            }
            // 引用校验——测量 → 控制律 → 测量
            MeasureV2? measure = FindMeasure(doc, sugar.RefNames[0]);
            if (measure == null)
            {
                AddError(result, "E210", "FBK 引用测量不存在——'" + sugar.RefNames[0] + "'");
                return;
            }
            LawV2? law = FindLaw(doc, sugar.RefNames[1]);
            if (law == null)
            {
                AddError(result, "E210", "FBK 引用控制律不存在——'" + sugar.RefNames[1] + "'");
                return;
            }
            MeasureV2? measure2 = FindMeasure(doc, sugar.RefNames[2]);
            if (measure2 == null)
            {
                AddError(result, "E210", "FBK 引用测量不存在——'" + sugar.RefNames[2] + "'");
                return;
            }
            if (law.ResultKind != ResultKindV2.StateTransfer)
            {
                AddError(result, "E210", "FBK 引用控制律 '" + law.Name + "' 结果侧非状态转移");
                return;
            }

            // 隐式状态机——S_Fb = { Waiting, Working }
            string fbName = "S_Fb";
            int fbNo = 1;
            while (doc.FindMachine(fbName) != null)
            {
                fbName = "S_Fb" + fbNo.ToString();
                fbNo = fbNo + 1;
            }
            MachineV2 machine = new MachineV2();
            machine.Name = fbName;
            machine.States.Add("Waiting");
            machine.States.Add("Working");
            doc.Machines.Add(machine);

            // T_W 条件追加 ∧ S_Fb = Waiting
            AppendStateCond(law, fbName, "Waiting");

            // 结果——成功追加 Working（失败分支回环 Waiting——仅原结果 ≥ 2 时替换）
            int originalCount = law.Results.Count;
            ResultV2 okResult = new ResultV2();
            okResult.Kind = ResultKindV2.StateTransfer;
            okResult.MachineName = fbName;
            okResult.StateName = "Working";
            law.Results.Add(okResult);
            if (originalCount >= 2)
            {
                // 失败分支改回环
                ResultV2 fail = law.Results[1];
                if (fail.Kind == ResultKindV2.StateTransfer)
                {
                    fail.MachineName = fbName;
                    fail.StateName = "Waiting";
                }
            }
        }

        /// <summary>
        /// 条件追加资源引用——顶层 And 化
        /// </summary>
        /// <param name="law">控制律</param>
        /// <param name="resName">资源名</param>
        private static void AppendResourceCond(LawV2 law, string resName)
        {
            CondV2 resCond = new CondV2();
            resCond.Kind = CondKindV2.ResourceRef;
            resCond.RefName = resName;
            WrapAnd(law, resCond);
        }

        /// <summary>
        /// 条件追加状态断言——顶层 And 化
        /// </summary>
        /// <param name="law">控制律</param>
        /// <param name="machineName">状态机名</param>
        /// <param name="stateName">状态名</param>
        private static void AppendStateCond(LawV2 law, string machineName, string stateName)
        {
            CondV2 stateCond = new CondV2();
            stateCond.Kind = CondKindV2.StateEquals;
            stateCond.MachineName = machineName;
            stateCond.StateName = stateName;
            WrapAnd(law, stateCond);
        }

        /// <summary>
        /// And 包装——顶层非 And 时包成 And 后追加
        /// </summary>
        /// <param name="law">控制律</param>
        /// <param name="cond">追加条件</param>
        private static void WrapAnd(LawV2 law, CondV2 cond)
        {
            if (law.Conditions.Kind == CondKindV2.And)
            {
                law.Conditions.Items!.Add(cond);
                return;
            }
            CondV2 and = new CondV2();
            and.Kind = CondKindV2.And;
            and.Items = new List<CondV2>();
            and.Items.Add(law.Conditions);
            and.Items.Add(cond);
            law.Conditions = and;
        }

        /// <summary>
        /// 查控制律
        /// </summary>
        /// <param name="doc">文档</param>
        /// <param name="name">控制律名</param>
        /// <returns>控制律（无则 null）</returns>
        private static LawV2? FindLaw(MauDocV2 doc, string name)
        {
            for (int i = 0; i < doc.Laws.Count; i++)
            {
                if (doc.Laws[i].Name == name)
                {
                    return doc.Laws[i];
                }
            }
            return null;
        }

        /// <summary>
        /// 查测量
        /// </summary>
        /// <param name="doc">文档</param>
        /// <param name="name">测量名</param>
        /// <returns>测量（无则 null）</returns>
        private static MeasureV2? FindMeasure(MauDocV2 doc, string name)
        {
            for (int i = 0; i < doc.Measures.Count; i++)
            {
                if (doc.Measures[i].Name == name)
                {
                    return doc.Measures[i];
                }
            }
            return null;
        }

        /// <summary>
        /// 错误添加
        /// </summary>
        /// <param name="result">展开结果</param>
        /// <param name="code">错误码</param>
        /// <param name="message">消息</param>
        private static void AddError(ExpandResultV2 result, string code, string message)
        {
            result.Diagnostics.Add(new MauDiagnostic(code, 0, message));
            result.Success = false;
        }
    }
}
