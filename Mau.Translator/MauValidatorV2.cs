using System;
using System.Collections.Generic;

namespace Mau.Translator
{
    /// <summary>
    /// 验证结果
    /// </summary>
    public sealed class ValidateResultV2
    {
        /// <summary>是否通过——无错误诊断</summary>
        public bool Success = true;

        /// <summary>诊断列表（E2xx 验证）</summary>
        public List<MauDiagnostic> Diagnostics = new List<MauDiagnostic>();
    }

    /// <summary>
    /// 验证层——写入源唯一/引用完整性/绑定完整性/图检查（构筑期——执行前抓错）
    /// </summary>
    public static class MauValidatorV2
    {
        /// <summary>
        /// 验证入口
        /// </summary>
        /// <param name="doc">解析文档</param>
        /// <returns>验证结果</returns>
        public static ValidateResultV2 Validate(MauDocV2 doc)
{
            ValidateResultV2 result = new ValidateResultV2();
            if (doc == null)
            {
                AddError(result, "E200", 0, "文档为空");
                return result;
            }

            // [段1] 版本兼容——声明 ≤ 支持版本（2.0）
            CheckVersion(doc, result);
            if (!result.Success)
            {
                return result;
            }

            // [段2] 状态机唯一 + 状态唯一 + 绑定完整性
            CheckMachines(doc, result);
            if (!result.Success)
            {
                return result;
            }

            // [段3] 资源唯一 + 配额
            CheckResources(doc, result);
            if (!result.Success)
            {
                return result;
            }

            // [段4] 命题写入源唯一
            CheckPropWriters(doc, result);
            if (!result.Success)
            {
                return result;
            }

            // [段5] 控制律引用完整性——条件/结果/状态/命题/资源
            CheckLaws(doc, result);
            if (!result.Success)
            {
                return result;
            }

            // [段6] 测量引用完整性——目标条件 + 采样积木
            CheckMeasures(doc, result);
            if (!result.Success)
            {
                return result;
            }

            // [段7] 语法糖引用完整性
            CheckSugars(doc, result);
            if (!result.Success)
            {
                return result;
            }

            // [段8] 边界完整性——信号引用存在
            CheckBoundaries(doc, result);
            if (!result.Success)
            {
                return result;
            }

            // [段9] 积木契约校验——存在性/参数数量/测量采样（E220-222）
            CheckBricks(doc, result);
            if (!result.Success)
            {
                return result;
            }

            result.Success = result.Diagnostics.Count == 0;
            return result;
        }        /// <summary>
        /// 版本兼容检查——声明 ≤ 2.0
        /// </summary>
        /// <param name="doc">文档</param>
        /// <param name="result">验证结果</param>
        private static void CheckVersion(MauDocV2 doc, ValidateResultV2 result)
        {
            if (doc.SyntaxVersion == null)
            {
                AddError(result, "E203", 0, "缺少语法版本声明——应含 §'Mau' 2.0");
                return;
            }
            if (ParseVersion(doc.SyntaxVersion) > 200)
            {
                AddError(result, "E203", 0, "语法版本过高——'" + doc.SyntaxVersion + "'，当前翻译器支持 ≤ 2.0");
            }
            if (doc.RuntimeVersion != null && ParseVersion(doc.RuntimeVersion) > 200)
            {
                AddError(result, "E203", 0, "运行时基座版本过高——'" + doc.RuntimeVersion + "'，当前支持 ≤ 2.0");
            }
        }

        /// <summary>
        /// 版本数值解析——2.0 → 200
        /// </summary>
        /// <param name="version">版本文本</param>
        /// <returns>数值（非法为 0）</returns>
        private static int ParseVersion(string version)
        {
            int dot = version.IndexOf('.');
            string major = dot >= 0 ? version.Substring(0, dot) : version;
            string minor = dot >= 0 ? version.Substring(dot + 1) : "0";
            int mj = ParseInt(major, 0);
            int mn = ParseInt(minor, 0);
            return mj * 100 + mn;
        }

        /// <summary>
        /// 整数解析
        /// </summary>
        /// <param name="text">文本</param>
        /// <param name="fallback">非法回落</param>
        /// <returns>数值</returns>
        private static int ParseInt(string text, int fallback)
        {
            if (text.Length == 0)
            {
                return fallback;
            }
            int value = 0;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c < '0' || c > '9')
                {
                    return fallback;
                }
                value = value * 10 + (c - '0');
            }
            return value;
        }

        /// <summary>
        /// 状态机检查——重名/状态重名/绑定完整性
        /// </summary>
        /// <param name="doc">文档</param>
        /// <param name="result">验证结果</param>
        private static void CheckMachines(MauDocV2 doc, ValidateResultV2 result)
        {
            for (int i = 0; i < doc.Machines.Count; i++)
            {
                MachineV2 m = doc.Machines[i];
                // 状态机重名
                for (int j = i + 1; j < doc.Machines.Count; j++)
                {
                    if (doc.Machines[j].Name == m.Name)
                    {
                        AddError(result, "E204", 0, "状态机重复声明——'" + m.Name + "'");
                        return;
                    }
                }
                // 状态重名
                for (int a = 0; a < m.States.Count; a++)
                {
                    for (int b = a + 1; b < m.States.Count; b++)
                    {
                        if (m.States[a] == m.States[b])
                        {
                            AddError(result, "E204", 0, "状态重名——'" + m.Name + "' 含重复状态 '" + m.States[a] + "'");
                            return;
                        }
                    }
                }
                // 绑定完整性——父状态存在 + 子机存在
                for (int b = 0; b < m.Binds.Count; b++)
                {
                    MachineBindV2 bind = m.Binds[b];
                    bool parentStateExists = false;
                    for (int s = 0; s < m.States.Count; s++)
                    {
                        if (m.States[s] == bind.ParentState)
                        {
                            parentStateExists = true;
                            break;
                        }
                    }
                    if (!parentStateExists)
                    {
                        AddError(result, "E204", 0, "绑定父状态不存在——'" + bind.ParentName + "." + bind.ParentState + "'");
                        return;
                    }
                    if (doc.FindMachine(bind.ChildName) == null)
                    {
                        AddError(result, "E204", 0, "绑定子状态机未声明——'" + bind.ChildName + "'");
                        return;
                    }
                }
            }
            // 嵌套无并列——同一父状态绑定多个子机 = 并列，拒绝
            for (int i = 0; i < doc.Machines.Count; i++)
            {
                MachineV2 m = doc.Machines[i];
                for (int a = 0; a < m.Binds.Count; a++)
                {
                    for (int b = a + 1; b < m.Binds.Count; b++)
                    {
                        if (m.Binds[a].ParentState == m.Binds[b].ParentState)
                        {
                            AddError(result, "E204", 0, "嵌套并列违规——父状态 '" + m.Name + "." + m.Binds[a].ParentState + "' 绑定多个子机（'"
                                + m.Binds[a].ChildName + "' / '" + m.Binds[b].ChildName + "'）");
                            return;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 资源检查——重名
        /// </summary>
        /// <param name="doc">文档</param>
        /// <param name="result">验证结果</param>
        private static void CheckResources(MauDocV2 doc, ValidateResultV2 result)
        {
            for (int i = 0; i < doc.Resources.Count; i++)
            {
                for (int j = i + 1; j < doc.Resources.Count; j++)
                {
                    if (doc.Resources[j].Name == doc.Resources[i].Name)
                    {
                        AddError(result, "E205", 0, "资源重复声明——'" + doc.Resources[i].Name + "'");
                        return;
                    }
                }
            }
        }

        /// <summary>
        /// 命题写入源唯一——多个写入源冲突
        /// </summary>
        /// <param name="doc">文档</param>
        /// <param name="result">验证结果</param>
        private static void CheckPropWriters(MauDocV2 doc, ValidateResultV2 result)
        {
            for (int i = 0; i < doc.Propositions.Count; i++)
            {
                PropositionV2 prop = doc.Propositions[i];
                if (prop.WriteSource.Contains("+", StringComparison.Ordinal))
                {
                    AddError(result, "E201", 0, "命题写入源冲突——'" + prop.Name + "' 被多个写入源写入（" + prop.WriteSource + "）");
                    return;
                }
            }
        }

        /// <summary>
        /// 控制律检查——条件/结果引用完整性
        /// </summary>
        /// <param name="doc">文档</param>
        /// <param name="result">验证结果</param>
        private static void CheckLaws(MauDocV2 doc, ValidateResultV2 result)
{
            for (int i = 0; i < doc.Laws.Count; i++)
            {
                LawV2 law = doc.Laws[i];
                // 控制律重名
                for (int j = i + 1; j < doc.Laws.Count; j++)
                {
                    if (doc.Laws[j].Name == law.Name)
                    {
                        AddError(result, "E206", 0, "控制律重复声明——'" + law.Name + "'");
                        return;
                    }
                }
                // 线程/汇合合法性——∥ 无 ⋈ → 结果无法回主线程
                if (law.Attrs.IsWorker && !law.Attrs.IsJoin)
                {
                    AddError(result, "E207", 0, "控制律 '" + law.Name + "' 声明 ∥ 但未声明 ⋈——worker 结果无法回主线程");
                    return;
                }
                // worker 律操作约束——名称返回积木 switch 分发依赖同步结果，不允许 ∥ 后台执行（RT.3）
                if (law.Attrs.IsWorker)
                {
                    for (int o = 0; o < law.Ops.Count; o++)
                    {
                        BrickIndexEntry entry;
                        if (BrickIndex.TryGet(law.Ops[o].BrickName, out entry) && entry.Contract != null
                            && entry.Contract.Return == Mau.Contracts.BrickReturnKind.String)
                        {
                            AddError(result, "E208", 0, "worker 控制律 '" + law.Name + "' 操作含名称返回积木 '" + law.Ops[o].BrickName + "'——switch 分发依赖同步结果，不允许 ∥ 后台执行");
                            return;
                        }
                    }
                }
                // 条件引用完整性
                CheckCondTree(doc, law, law.Conditions, result, law.Name);
                if (!result.Success)
                {
                    return;
                }
                // 结果引用完整性
                for (int r = 0; r < law.Results.Count; r++)
                {
                    ResultV2 res = law.Results[r];
                    if (res.Kind == ResultKindV2.StateTransfer)
                    {
                        MachineV2? machine = doc.FindMachine(res.MachineName!);
                        if (machine == null)
                        {
                            AddError(result, "E204", 0, "控制律 '" + law.Name + "' 结果引用状态机不存在——'" + res.MachineName + "'");
                            return;
                        }
                        if (!machine.States.Contains(res.StateName!))
                        {
                            AddError(result, "E204", 0, "控制律 '" + law.Name + "' 结果引用状态不存在——'" + res.MachineName + "." + res.StateName + "'");
                            return;
                        }
                    }
                    else if (res.Kind == ResultKindV2.PropRegister)
                    {
                        PropositionV2? prop = doc.FindProposition(res.PropName!);
                        if (prop == null)
                        {
                            AddError(result, "E202", 0, "控制律 '" + law.Name + "' 结果注册命题未声明——'" + res.PropName + "'（引用即存在需写入源）");
                            return;
                        }
                    }
                }
                // 名称返回积木校验——操作积木 String 返回（多路 switch 分发源）：结果分支 ≥ 2（候选名对应）
                if (law.Ops.Count == 1 && BrickIndex.Count > 0)
                {
                    BrickIndexEntry? routeEntry = null;
                    bool isNameReturn = BrickIndex.TryGet(law.Ops[0].BrickName, out routeEntry)
                        && routeEntry != null && routeEntry.Contract != null
                        && routeEntry.Contract.Return == Mau.Contracts.BrickReturnKind.String;
                    if (isNameReturn && law.Results.Count < 2)
                    {
                        AddError(result, "E223", 0, "名称返回积木 '" + law.Ops[0].BrickName + "' 至少需要 2 路互斥结果（switch 按名分发）——当前 " + law.Results.Count + " 路");
                        return;
                    }
                }
            }
        }
        /// <summary>
        /// 条件树检查——递归
        /// </summary>
        /// <param name="doc">文档</param>
        /// <param name="law">控制律</param>
        /// <param name="cond">条件节点</param>
        /// <param name="result">验证结果</param>
        /// <param name="lawName">控制律名</param>
        private static void CheckCondTree(MauDocV2 doc, LawV2 law, CondV2 cond, ValidateResultV2 result, string lawName)
        {
            if (cond == null)
            {
                AddError(result, "E208", 0, "控制律 '" + lawName + "' 条件为空");
                return;
            }
            if (cond.Kind == CondKindV2.And || cond.Kind == CondKindV2.Or)
            {
                if (cond.Items == null || cond.Items.Count == 0)
                {
                    AddError(result, "E208", 0, "控制律 '" + lawName + "' 条件树为空（∧/∨ 无子项）");
                    return;
                }
                for (int i = 0; i < cond.Items.Count; i++)
                {
                    CheckCondTree(doc, law, cond.Items[i], result, lawName);
                    if (!result.Success)
                    {
                        return;
                    }
                }
                return;
            }
            if (cond.Kind == CondKindV2.StateEquals)
            {
                MachineV2? machine = doc.FindMachine(cond.MachineName!);
                if (machine == null)
                {
                    AddError(result, "E204", 0, "控制律 '" + lawName + "' 前置引用状态机不存在——'" + cond.MachineName + "'");
                    return;
                }
                if (!machine.States.Contains(cond.StateName!))
                {
                    AddError(result, "E204", 0, "控制律 '" + lawName + "' 前置引用状态不存在——'" + cond.MachineName + "." + cond.StateName + "'");
                    return;
                }
                return;
            }
            if (cond.Kind == CondKindV2.PropRef)
            {
                PropositionV2? prop = doc.FindProposition(cond.RefName!);
                if (prop == null || prop.Kind == PropKindV2.Unknown)
                {
                    AddError(result, "E202", 0, "控制律 '" + lawName + "' 前置引用命题无写入源——'" + cond.RefName + "'（拼写错误或未接线）");
                    return;
                }
                return;
            }
            if (cond.Kind == CondKindV2.ResourceRef)
            {
                bool found = false;
                for (int i = 0; i < doc.Resources.Count; i++)
                {
                    if (doc.Resources[i].Name == cond.RefName)
                    {
                        found = true;
                        break;
                    }
                }
                if (!found)
                {
                    AddError(result, "E205", 0, "控制律 '" + lawName + "' 前置引用资源不存在——'" + cond.RefName + "'");
                    return;
                }
            }
        }

        /// <summary>
        /// 测量检查——目标条件存在 + 测量重名
        /// </summary>
        /// <param name="doc">文档</param>
        /// <param name="result">验证结果</param>
        private static void CheckMeasures(MauDocV2 doc, ValidateResultV2 result)
        {
            for (int i = 0; i < doc.Measures.Count; i++)
            {
                MeasureV2 m = doc.Measures[i];
                for (int j = i + 1; j < doc.Measures.Count; j++)
                {
                    if (doc.Measures[j].Name == m.Name)
                    {
                        AddError(result, "E206", 0, "测量重复声明——'" + m.Name + "'");
                        return;
                    }
                }
                PropositionV2? target = doc.FindProposition(m.Target);
                if (target == null || target.Kind != PropKindV2.Condition)
                {
                    AddError(result, "E202", 0, "测量 '" + m.Name + "' 目标非条件命题——'" + m.Target + "'（条件由测量写入）");
                    return;
                }
            }
        }

        /// <summary>
        /// 语法糖检查——引用存在 + 糖名合法
        /// </summary>
        /// <param name="doc">文档</param>
        /// <param name="result">验证结果</param>
        private static void CheckSugars(MauDocV2 doc, ValidateResultV2 result)
        {
            for (int i = 0; i < doc.Sugars.Count; i++)
            {
                SugarV2 sugar = doc.Sugars[i];
                if (sugar.Name != "SEQ" && sugar.Name != "PAR" && sugar.Name != "FBK")
                {
                    AddError(result, "E209", 0, "未知语法糖——'" + sugar.Name + "'（支持 SEQ/PAR/FBK）");
                    return;
                }
                if (sugar.RefNames.Count == 0)
                {
                    AddError(result, "E209", 0, "语法糖 '" + sugar.Name + "' 无引用");
                    return;
                }
                for (int r = 0; r < sugar.RefNames.Count; r++)
                {
                    string refName = sugar.RefNames[r];
                    bool found = false;
                    for (int l = 0; l < doc.Laws.Count; l++)
                    {
                        if (doc.Laws[l].Name == refName)
                        {
                            found = true;
                            break;
                        }
                    }
                    if (!found)
                    {
                        for (int m = 0; m < doc.Measures.Count; m++)
                        {
                            if (doc.Measures[m].Name == refName)
                            {
                                found = true;
                                break;
                            }
                        }
                    }
                    if (!found)
                    {
                        AddError(result, "E209", 0, "语法糖 '" + sugar.Name + "' 引用不存在——'" + refName + "'");
                        return;
                    }
                }
            }
        }

        /// <summary>
        /// 边界检查——输出端口引用命题存在
        /// </summary>
        /// <param name="doc">文档</param>
        /// <param name="result">验证结果</param>
        private static void CheckBoundaries(MauDocV2 doc, ValidateResultV2 result)
        {
            for (int i = 0; i < doc.Boundaries.Count; i++)
            {
                BoundaryV2 b = doc.Boundaries[i];
                if (b.Dir == BoundaryDirV2.Out)
                {
                    continue;
                }
                // 输入边界——信号目标必须存在（⇐ 'P_X' 或 OA 接收目标）
                string signalName = b.MappedName != null ? b.MappedName : b.SignalName;
                PropositionV2? prop = doc.FindProposition(signalName);
                if (prop == null || prop.Kind != PropKindV2.Signal)
                {
                    AddError(result, "E202", 0, "边界输入目标非信号命题——'" + signalName + "'（信号由 ⇐ 声明）");
                    return;
                }
            }
        }
/// <summary>
/// 积木契约校验——积木存在性 + 参数数量匹配（E220 积木未注册 / E221 参数数量超限 / E222 测量采样非 bool 返回）
/// </summary>
/// <param name = "doc">文档</param>
/// <param name = "result">验证结果</param>
private static void CheckBricks(MauDocV2 doc, ValidateResultV2 result)
{
            // [段0] 索引未加载跳过——积木契约校验依赖 Bricks/index.json（由 CLI/编译入口 EnsureIndexLoaded）
            if (BrickIndex.Count == 0)
            {
                return;
            }
            // [段1] 控制律操作——每个积木调用：存在性 + 参数数量
            for (int i = 0; i < doc.Laws.Count; i++)
            {
                LawV2 law = doc.Laws[i];
                for (int o = 0; o < law.Ops.Count; o++)
                {
                    BrickCallV2 call = law.Ops[o];
                    BrickIndexEntry entry;
                    if (!BrickIndex.TryGet(call.BrickName, out entry) || entry.Contract == null)
                    {
                        AddError(result, "E220", 0, "控制律 '" + law.Name + "' 引用积木未注册——'" + call.BrickName + "'（Bricks/index.json 查询）");
                        return;
                    }
                    if (call.Params.Count > entry.Contract.Inputs.Count)
                    {
                        AddError(result, "E221", 0, "控制律 '" + law.Name + "' 积木调用参数超限——'" + call.BrickName + "' 传 " + call.Params.Count + " 个参数，契约输入端口 " + entry.Contract.Inputs.Count + " 个");
                        return;
                    }
                }
            }
            // [段2] 测量采样——积木存在 + 必须 bool 返回（判断积木语义——is_* 命名或 return == Bool）
            for (int m = 0; m < doc.Measures.Count; m++)
            {
                MeasureV2 measure = doc.Measures[m];
                if (measure.Sample == null || measure.Sample.BrickName.Length == 0)
                {
                    AddError(result, "E222", 0, "测量 '" + measure.Name + "' 缺少采样积木");
                    return;
                }
                BrickIndexEntry entry;
                if (!BrickIndex.TryGet(measure.Sample.BrickName, out entry) || entry.Contract == null)
                {
                    AddError(result, "E220", 0, "测量 '" + measure.Name + "' 引用积木未注册——'" + measure.Sample.BrickName + "'");
                    return;
                }
                if (entry.Contract.Return != Mau.Contracts.BrickReturnKind.Bool)
                {
                    AddError(result, "E222", 0, "测量 '" + measure.Name + "' 采样积木 '" + measure.Sample.BrickName + "' 必须 bool 返回（判断积木语义——实测值写条件）");
                    return;
                }
                if (measure.Sample.Params.Count > entry.Contract.Inputs.Count)
                {
                    AddError(result, "E221", 0, "测量 '" + measure.Name + "' 采样调用参数超限——'" + measure.Sample.BrickName + "' 传 " + measure.Sample.Params.Count + " 个参数，契约输入端口 " + entry.Contract.Inputs.Count + " 个");
                    return;
                }
            }
        }        /// <summary>
        /// 错误添加
        /// </summary>
        /// <param name="result">验证结果</param>
        /// <param name="code">错误码</param>
        /// <param name="sectionNo">段号</param>
        /// <param name="message">消息</param>
        private static void AddError(ValidateResultV2 result, string code, int sectionNo, string message)
        {
            result.Diagnostics.Add(new MauDiagnostic(code, sectionNo, message));
            result.Success = false;
        }
    }
}
