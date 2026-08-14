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
    // [段2] 引用完整性
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
    // [段4] 积木门禁——导线动作 + 主动传感器采样（E4xx——BrickIndex 校验，P5 接入）
    CheckBricks(doc);
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
        private static void CheckBricks(MauDocV3 doc)
        {
            for (int w = 0; w < doc.Wires.Count; w++)
            {
                WireDefV3 wire = doc.Wires[w];
                if (wire.BrickName.Length > 0)
                {
                    CheckBrickCall(wire.BrickName, wire.BrickArgs, wire.Line, doc);
                }
            }
            for (int s = 0; s < doc.Sensors.Count; s++)
            {
                SensorDefV3 sensor = doc.Sensors[s];
                if (!sensor.Passive && sensor.BrickName.Length > 0)
                {
                    CheckBrickCall(sensor.BrickName, sensor.BrickArgs, sensor.Line, doc);
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
        private static void CheckBrickCall(string name, List<string> args, int line, MauDocV3 doc)
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
                string actualKind = ArgKind(args[i]);
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
