using System;
using System.Collections.Generic;

namespace Mau.Translator
{
    /// <summary>
    /// 验证器 v3——IR 级语义校验（design-mau-v3 §八.5）。
    /// 校验面：名称唯一（跨四柱）+ 引用完整性（传感器/状态机/状态值存在）+ 槽容量合法。
    /// 图论验证（有界环/可达性/扰动覆盖/关键路径）归分析器——本器只做引用与命名。
    /// 错误码：E200 重名 / E201 传感器引用不存在 / E202 状态机引用不存在 / E203 状态值不存在 / E204 主动传感器积木缺失。
    /// </summary>
    public static class MauValidatorV3
    {
        /// <summary>
        /// 语义校验——诊断追加进 doc.Diagnostics
        /// </summary>
        /// <param name="doc">FSM 网络 IR</param>
        /// <returns>true=全部校验通过</returns>
        public static bool Validate(MauDocV3 doc)
        {
            if (doc == null)
            {
                return false;
            }
            int before = doc.Diagnostics.Count;
            // [段1] 名称索引 + 唯一性（跨四柱重名 = 构筑期错误）
            Dictionary<string, string> kinds = new Dictionary<string, string>(StringComparer.Ordinal);
            CheckNames(doc.StateMachines, "状态机", kinds, doc);
            CheckNames(doc.Sensors, "传感器", kinds, doc);
            CheckNames(doc.Wires, "导线", kinds, doc);
            CheckNames(doc.Slots, "槽", kinds, doc);
            // [段2] 引用完整性 + Command key 唯一性
            CheckCmdKeys(doc);
            CheckReferences(doc);
            // [段3] 主动传感器积木存在性（解析层已保证非空——双保险）
            for (int i = 0; i < doc.Sensors.Count; i++)
            {
                SensorDefV3 sensor = doc.Sensors[i];
                if (!sensor.Passive && sensor.BrickName.Length == 0)
                {
                    doc.Diagnostics.Add(new MauDiagnostic("E204", sensor.Line, "主动传感器 '" + sensor.Name + "' 缺采样积木"));
                }
            }
            // [段4] 盒子表构建——写源收集（写源唯一/类型白名单）
            Dictionary<string, string> boxTypes = BuildBoxTypes(doc);
            // [段5] 积木门禁——导线动作 + 主动传感器采样（E4xx——BrickIndex 校验，P5 接入）
            CheckBricks(doc, boxTypes);
            // [段6] 盒子规则——读需写源 + 参数类型匹配（数据全局性体系）
            CheckBoxes(doc, boxTypes);
            return doc.Diagnostics.Count == before;
        }

        /// <summary>
        /// 名称唯一校验——单元自身与跨单元都不可重名
        /// </summary>
        /// <param name="defs">声明集合</param>
        /// <param name="kind">单元类型名</param>
        /// <param name="kinds">全局名称索引（名字 → 类型）</param>
        /// <param name="doc">诊断收集</param>
        private static void CheckNames<T>(List<T> defs, string kind, Dictionary<string, string> kinds, MauDocV3 doc)
            where T : class
        {
            for (int i = 0; i < defs.Count; i++)
            {
                string name = GetName(defs[i]);
                string? existing;
                if (kinds.TryGetValue(name, out existing) && existing != null)
                {
                    doc.Diagnostics.Add(new MauDiagnostic("E200", GetLine(defs[i]), "名称重名: '" + name + "'——已是" + existing + "（当前声明为" + kind + "）"));
                }
                else
                {
                    kinds[name] = kind;
                }
            }
        }
/// <summary>
/// Command key 唯一校验——一 key 一传感器（CommandBus 注册语义：key → 唯一注册者）
/// </summary>
/// <param name = "doc">FSM 网络 IR</param>
private static void CheckCmdKeys(MauDocV3 doc)
{
    Dictionary<string, string> keys = new Dictionary<string, string>(StringComparer.Ordinal);
    for (int i = 0; i < doc.Sensors.Count; i++)
    {
        SensorDefV3 sensor = doc.Sensors[i];
        if (!sensor.IsCmd || sensor.CmdKey.Length == 0)
        {
            continue;
        }

        string? existing;
        if (keys.TryGetValue(sensor.CmdKey, out existing) && existing != null)
        {
            doc.Diagnostics.Add(new MauDiagnostic("E206", sensor.Line, "Command key 重名: '" + sensor.CmdKey + "'——已属于传感器 '" + existing + "'（当前声明为 '" + sensor.Name + "'）"));
        }
        else
        {
            keys[sensor.CmdKey] = sensor.Name;
        }
    }
}
        /// <summary>
        /// 引用完整性校验——导线条件/结果引用的传感器与状态必须存在
        /// </summary>
        /// <param name="doc">FSM 网络 IR</param>
        private static void CheckReferences(MauDocV3 doc)
        {
            HashSet<string> sensors = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < doc.Sensors.Count; i++)
            {
                sensors.Add(doc.Sensors[i].Name);
            }
            Dictionary<string, HashSet<string>> stateValues = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            for (int i = 0; i < doc.StateMachines.Count; i++)
            {
                HashSet<string> values = new HashSet<string>(StringComparer.Ordinal);
                for (int j = 0; j < doc.StateMachines[i].States.Count; j++)
                {
                    values.Add(doc.StateMachines[i].States[j]);
                }
                stateValues[doc.StateMachines[i].Name] = values;
            }
            for (int w = 0; w < doc.Wires.Count; w++)
            {
                WireDefV3 wire = doc.Wires[w];
                // [段1] 条件——传感器沿 / 状态断言
                for (int c = 0; c < wire.Conditions.Count; c++)
                {
                    ConditionV3 cond = wire.Conditions[c];
                    if (cond.IsStateAssert)
                    {
                        CheckStateRef(wire, cond.StateName, cond.StateValue, stateValues, doc);
                    }
                    else if (cond.IsBoxAssert)
                    {
                        // 盒子判真——读需写源归 CheckBoxes（E407）
                    }
                    else
                    {
                        if (!sensors.Contains(cond.SensorName))
                        {
                            doc.Diagnostics.Add(new MauDiagnostic("E201", wire.Line, "导线 '" + wire.Name + "' 条件引用不存在的传感器: '" + cond.SensorName + "'"));
                        }
                    }
                }
                // [段2] 结果——状态转移目标
                for (int r = 0; r < wire.Results.Count; r++)
                {
                    ResultV3 result = wire.Results[r];
                    CheckStateRef(wire, result.StateName, result.StateValue, stateValues, doc);
                }
            }
        }

        /// <summary>
        /// 状态引用校验——状态机存在 + 状态值在其集合内
        /// </summary>
        /// <param name="wire">导线（诊断定位用）</param>
        /// <param name="stateName">状态机名</param>
        /// <param name="stateValue">状态值</param>
        /// <param name="stateValues">状态机 → 状态值集合</param>
        /// <param name="doc">诊断收集</param>
        private static void CheckStateRef(WireDefV3 wire, string stateName, string stateValue,
            Dictionary<string, HashSet<string>> stateValues, MauDocV3 doc)
        {
            HashSet<string>? values;
            if (!stateValues.TryGetValue(stateName, out values) || values == null)
            {
                doc.Diagnostics.Add(new MauDiagnostic("E202", wire.Line, "导线 '" + wire.Name + "' 引用不存在的状态机: '" + stateName + "'"));
                return;
            }
            if (!values.Contains(stateValue))
            {
                doc.Diagnostics.Add(new MauDiagnostic("E203", wire.Line, "导线 '" + wire.Name + "' 引用状态机 '" + stateName + "' 中不存在的状态值: '" + stateValue + "'"));
            }
        }

        /// <summary>
        /// 提取声明名（四种声明统一访问）
        /// </summary>
        /// <param name="def">声明对象</param>
        /// <returns>名称</returns>
        private static string GetName(object def)
        {
            if (def is StateMachineDefV3)
            {
                return ((StateMachineDefV3)def).Name;
            }
            if (def is SensorDefV3)
            {
                return ((SensorDefV3)def).Name;
            }
            if (def is WireDefV3)
            {
                return ((WireDefV3)def).Name;
            }
            return ((SlotDefV3)def).Name;
        }

        /// <summary>
        /// 提取声明行号（四种声明统一访问）
        /// </summary>
        /// <param name="def">声明对象</param>
        /// <returns>行号</returns>
        private static int GetLine(object def)
        {
            if (def is StateMachineDefV3)
            {
                return ((StateMachineDefV3)def).Line;
            }
            if (def is SensorDefV3)
            {
                return ((SensorDefV3)def).Line;
            }
            if (def is WireDefV3)
            {
                return ((WireDefV3)def).Line;
            }
            return ((SlotDefV3)def).Line;
        }

        /// <summary>
        /// 积木门禁——导线动作 + 主动传感器采样引用校验（E4xx）
        /// </summary>
        /// <param name="doc">FSM 网络 IR</param>
        /// <param name="boxTypes">盒子表（Key → 类型）</param>
        private static void CheckBricks(MauDocV3 doc, Dictionary<string, string> boxTypes)
        {
            for (int w = 0; w < doc.Wires.Count; w++)
            {
                WireDefV3 wire = doc.Wires[w];
                for (int a = 0; a < wire.Actions.Count; a++)
                {
                    CheckBrickCall(wire.Actions[a].BrickName, wire.Actions[a].BrickArgs, wire.Actions[a].Line, doc, boxTypes);
                }
            }
            for (int s = 0; s < doc.Sensors.Count; s++)
            {
                SensorDefV3 sensor = doc.Sensors[s];
                if (!sensor.Passive && sensor.BrickName.Length > 0)
                {
                    CheckBrickCall(sensor.BrickName, sensor.BrickArgs, sensor.Line, doc, boxTypes);
                }
                for (int a = 0; a < sensor.TrueActions.Count; a++)
                {
                    CheckBrickCall(sensor.TrueActions[a].BrickName, sensor.TrueActions[a].BrickArgs, sensor.Line, doc, boxTypes);
                }
                for (int a = 0; a < sensor.FalseActions.Count; a++)
                {
                    CheckBrickCall(sensor.FalseActions[a].BrickName, sensor.FalseActions[a].BrickArgs, sensor.Line, doc, boxTypes);
                }
            }
        }

        /// <summary>
        /// 盒子 Key 引用判定——@ 前缀私有盒，或已注册写源的全局盒
        /// </summary>
        /// <param name="boxTypes">盒子表（Key → 类型）</param>
        /// <param name="arg">参数原文</param>
        /// <returns>true=盒子引用</returns>
        private static bool IsBoxRef(Dictionary<string, string> boxTypes, string arg)
        {
            if (arg.Length > 0 && arg[0] == '@')
            {
                return true;
            }
            return boxTypes.ContainsKey(arg);
        }

        /// <summary>
        /// 盒子表构建——写源收集（导线捕获 + 壳探测捕获）：Key → 类型
        /// </summary>
        /// <param name="doc">IR</param>
        /// <returns>盒子表</returns>
        private static Dictionary<string, string> BuildBoxTypes(MauDocV3 doc)
        {
            Dictionary<string, string> boxTypes = new Dictionary<string, string>(StringComparer.Ordinal);
            // [段1] 导线动作捕获落盒——类型从积木 out 端口推导
            for (int w = 0; w < doc.Wires.Count; w++)
            {
                WireDefV3 wire = doc.Wires[w];
                for (int a = 0; a < wire.Actions.Count; a++)
                {
                    if (wire.Actions[a].CaptureTarget.Length > 0)
                    {
                        CollectBoxWriter(boxTypes, wire.Actions[a].CaptureTarget, wire.Actions[a].BrickName, wire.Actions[a].Line, doc);
                    }
                }
            }
            // [段2] 主动传感器壳探测捕获——bool 判断语义固定落盒
            for (int s = 0; s < doc.Sensors.Count; s++)
            {
                SensorDefV3 sensor = doc.Sensors[s];
                if (!sensor.Passive && sensor.CaptureTarget.Length > 0)
                {
                    if (boxTypes.ContainsKey(sensor.CaptureTarget))
                    {
                        doc.Diagnostics.Add(new MauDiagnostic("E408", sensor.Line, "盒子 '" + sensor.CaptureTarget + "' 重复写源——一个 Key 恰一个写入点"));
                    }
                    else
                    {
                        boxTypes[sensor.CaptureTarget] = "bool";
                    }
                }
            }
            return boxTypes;
        }

        /// <summary>
        /// 单写源收集——同 Key 重复写 E408；类型白名单外 E406；积木无 out 端口 E405
        /// </summary>
        /// <param name="boxTypes">盒子表</param>
        /// <param name="key">盒子 Key</param>
        /// <param name="brickName">写源积木</param>
        /// <param name="line">行号</param>
        /// <param name="doc">诊断收集</param>
        private static void CollectBoxWriter(Dictionary<string, string> boxTypes, string key, string brickName, int line, MauDocV3 doc)
        {
            if (boxTypes.ContainsKey(key))
            {
                doc.Diagnostics.Add(new MauDiagnostic("E408", line, "盒子 '" + key + "' 重复写源——一个 Key 恰一个写入点（数据全局性铁律）"));
                return;
            }
            BrickIndexEntry entry;
            if (!BrickIndex.TryFind(brickName, out entry))
            {
                boxTypes[key] = "string";
                return;
            }
            if (entry.OutputCount < 1)
            {
                doc.Diagnostics.Add(new MauDiagnostic("E405", line, "捕获目标 '" + key + "' 但积木 '" + brickName + "' 无输出端口"));
                return;
            }
            string captureType = "string";
            if (entry.OutputTypes.Count > 0)
            {
                captureType = entry.OutputTypes[0];
            }
            if (captureType != "string" && captureType != "long" && captureType != "int" && captureType != "bool")
            {
                doc.Diagnostics.Add(new MauDiagnostic("E406", line, "捕获类型 '" + captureType + "' 不支持——白名单 string/long/int/bool"));
                return;
            }
            boxTypes[key] = captureType;
        }

        /// <summary>
        /// 盒子规则校验——读需写源（E407）+ 参数类型匹配（E402 严格版）
        /// </summary>
        /// <param name="doc">IR</param>
        /// <param name="boxTypes">盒子表</param>
        private static void CheckBoxes(MauDocV3 doc, Dictionary<string, string> boxTypes)
        {
            // [段1] 导线动作参数 + 条件盒子判真
            for (int w = 0; w < doc.Wires.Count; w++)
            {
                WireDefV3 wire = doc.Wires[w];
                for (int a = 0; a < wire.Actions.Count; a++)
                {
                    CheckBoxArgs(wire.Actions[a].BrickArgs, wire.Actions[a].BrickName, wire.Actions[a].Line, doc, boxTypes);
                }
                for (int c = 0; c < wire.Conditions.Count; c++)
                {
                    ConditionV3 cond = wire.Conditions[c];
                    if (cond.IsBoxAssert)
                    {
                        CheckBoxRead(cond.BoxName, wire.Line, doc, boxTypes);
                    }
                }
            }
            // [段2] 壳动作参数
            for (int s = 0; s < doc.Sensors.Count; s++)
            {
                SensorDefV3 sensor = doc.Sensors[s];
                if (sensor.Passive)
                {
                    continue;
                }
                for (int a = 0; a < sensor.TrueActions.Count; a++)
                {
                    CheckBoxArgs(sensor.TrueActions[a].BrickArgs, sensor.TrueActions[a].BrickName, sensor.Line, doc, boxTypes);
                }
                for (int a = 0; a < sensor.FalseActions.Count; a++)
                {
                    CheckBoxArgs(sensor.FalseActions[a].BrickArgs, sensor.FalseActions[a].BrickName, sensor.Line, doc, boxTypes);
                }
            }
        }

        /// <summary>
        /// 单调用参数盒子检查——读需写源（E407）+ 类型匹配（E402）
        /// </summary>
        /// <param name="args">参数原文</param>
        /// <param name="brickName">积木名</param>
        /// <param name="line">行号</param>
        /// <param name="doc">诊断收集</param>
        /// <param name="boxTypes">盒子表</param>
        private static void CheckBoxArgs(List<string> args, string brickName, int line, MauDocV3 doc, Dictionary<string, string> boxTypes)
{
            BrickIndexEntry entry;
            if (!BrickIndex.TryFind(brickName, out entry))
            {
                return;
            }
            for (int i = 0; i < args.Count; i++)
            {
                string arg = args[i].Trim();
                if (ArgKind(arg) != "name" || !IsBoxRef(boxTypes, arg))
                {
                    continue;
                }
                if (!boxTypes.ContainsKey(arg))
                {
                    // B1 豁免——全局盒（无 @ 前缀）写源在语料外（宿主 Command 投递落盒）——数据全局性哲学：真实存在的东西都是全局的
                    if (arg.Length == 0 || arg[0] == '@')
                    {
                        doc.Diagnostics.Add(new MauDiagnostic("E407", line, "盒子 '" + arg + "' 读无写源——先有捕获落盒（> @key / > key）才能引用"));
                    }
                    continue;
                }
                string boxType = boxTypes[arg];
                string inType = "";
                if (i < entry.InputTypes.Count)
                {
                    inType = entry.InputTypes[i];
                }
                if (inType != boxType)
                {
                    doc.Diagnostics.Add(new MauDiagnostic("E402", line, "盒子 '" + arg + "' 类型 '" + boxType + "' 与积木 '" + brickName + "' 第 " + (i + 1).ToString() + " 参数类型 '" + inType + "' 不匹配"));
                }
            }
        }
        /// <summary>
        /// 单盒子读检查——条件判真引用需有写源
        /// </summary>
        /// <param name="key">盒子 Key</param>
        /// <param name="line">行号</param>
        /// <param name="doc">诊断收集</param>
        /// <param name="boxTypes">盒子表</param>
        private static void CheckBoxRead(string key, int line, MauDocV3 doc, Dictionary<string, string> boxTypes)
{
            if (!boxTypes.ContainsKey(key))
            {
                // B1 豁免——全局盒（无 @ 前缀）写源在语料外——数据全局性哲学
                if (key.Length == 0 || key[0] == '@')
                {
                    doc.Diagnostics.Add(new MauDiagnostic("E407", line, "盒子 '" + key + "' 读无写源——先有捕获落盒（> @key / > key）才能引用"));
                }
            }
        }
        /// <summary>
        /// 单次积木调用校验——E400 未知积木 / E401 参数数量 / E402 参数类别
        /// </summary>
        /// <param name="name">积木名</param>
        /// <param name="args">参数原文列表</param>
        /// <param name="line">声明行号</param>
        /// <param name="doc">诊断收集</param>
        /// <param name="boxTypes">盒子表（Key → 类型）</param>
        private static void CheckBrickCall(string name, List<string> args, int line, MauDocV3 doc, Dictionary<string, string> boxTypes)
{
            BrickIndexEntry entry;
            if (!BrickIndex.TryFind(name, out entry))
            {
                doc.Diagnostics.Add(new MauDiagnostic("E400", line, "未知积木: '" + name + "'——不在 Bricks/index.json 索引中"));
                return;
            }
            if (args.Count != entry.InputTypes.Count)
            {
                doc.Diagnostics.Add(new MauDiagnostic("E401", line, "积木 '" + name + "' 参数数量不符——期望 " + entry.InputTypes.Count.ToString() + " 个，实际 " + args.Count.ToString() + " 个"));
                return;
            }
            for (int i = 0; i < args.Count; i++)
            {
                string trimmed = args[i].Trim();
                string actualKind = ArgKind(args[i]);
                if (actualKind == "name" && IsBoxRef(boxTypes, trimmed))
                {
                    // 盒子 Key 引用——存在性与类型匹配归 CheckBoxes 统一查
                    continue;
                }
                if (actualKind == "name" && trimmed.Length > 0 && trimmed[0] != '@')
                {
                    // 裸词 B1 豁免——全局盒（无 @ 前缀）写源在语料外（宿主/Command 落盒），类型信任 string
                    // R2-P2-02 收紧：仅 string 期望豁免（全局盒当 string 参数合法）；int/long/bool 期望传裸词 → E402（原静默兜底诊断不友好）
                    if (ContractKind(entry.InputTypes[i]) == "str")
                    {
                        continue;
                    }
                }
                string expectedKind = ContractKind(entry.InputTypes[i]);
                if (actualKind.Length == 0 || actualKind != expectedKind)
                {
                    doc.Diagnostics.Add(new MauDiagnostic("E402", line, "积木 '" + name + "' 第 " + (i + 1).ToString() + " 参数类别不符——期望 " + entry.InputTypes[i] + "（" + expectedKind + "），实际 '" + args[i] + "'（" + actualKind + "）"));
                }
            }
        }
        /// <summary>
        /// 参数原文类别——"..." → str / 纯数字 → num / 其他 → name
        /// </summary>
        /// <param name="raw">参数原文</param>
        /// <returns>类别标记（空 = 无法识别）</returns>
        private static string ArgKind(string raw)
        {
            string t = raw.Trim();
            if (t.Length >= 2 && t[0] == '"')
            {
                return "str";
            }
            bool allDigits = t.Length > 0;
            for (int i = 0; i < t.Length; i++)
            {
                char c = t[i];
                bool digit = c >= '0' && c <= '9';
                bool dot = c == '.';
                if (!digit && !dot)
                {
                    allDigits = false;
                    break;
                }
            }
            if (allDigits)
            {
                return "num";
            }
            return "name";
        }

        /// <summary>
        /// 契约类型 → 类别标记——string → str / int,long,bool → num / string[] → name
        /// </summary>
        /// <param name="type">契约类型文本</param>
        /// <returns>类别标记（空 = 未支持类型）</returns>
        private static string ContractKind(string type)
        {
            if (type == "string")
            {
                return "str";
            }
            if (type == "int" || type == "long" || type == "bool")
            {
                return "num";
            }
            if (type == "string[]")
            {
                return "name";
            }
            return "";
        }
    }
}
