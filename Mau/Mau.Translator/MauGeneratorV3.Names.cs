using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Mau.Translator
{
    /// <summary>
    /// MauGeneratorV3 名称辅助面分部——盒子引用/类型映射/传感器查找/名称转换。
    /// P7b partial 拆分——自 MauGeneratorV3.cs 原样搬移，逻辑零改动。
    /// </summary>
    public static partial class MauGeneratorV3
    {
        /// <summary>
        /// 盒子 Key 去 @ 前缀——@key → key（scope 由 BoxScopeExpr 决定）
        /// </summary>
        /// <param name="boxRef">盒子引用原文</param>
        /// <returns>Key 原文</returns>
        private static string BoxKey(string boxRef)
        {
            if (boxRef.Length > 0 && boxRef[0] == '@')
            {
                return boxRef.Substring(1);
            }
            return boxRef;
        }
        /// <summary>
        /// 盒子 scope 表达式——@key → FlowId 私有；key → "global"
        /// </summary>
        /// <param name="boxRef">盒子引用原文</param>
        /// <returns>C# scope 表达式文本</returns>
        private static string BoxScopeExpr(string boxRef)
        {
            if (boxRef.Length > 0 && boxRef[0] == '@')
            {
                return "FlowContext.CurrentFlowId.ToString()";
            }
            return "\"global\"";
        }
        /// <summary>
        /// 捕获写 C# 类型——按积木 out 端口契约
        /// </summary>
        /// <param name="brickName">写源积木</param>
        /// <returns>C# 类型文本</returns>
        private static string CaptureCSType(string brickName)
        {
            BrickIndexEntry entry;
            if (BrickIndex.TryFind(brickName, out entry) && entry.OutputTypes.Count > 0)
            {
                return entry.OutputTypes[0];
            }
            return "string";
        }

        /// <summary>
        /// 按名查传感器声明
        /// </summary>
        /// <param name="doc">IR</param>
        /// <param name="name">传感器名</param>
        /// <returns>传感器声明</returns>
        private static SensorDefV3 FindSensor(MauDocV3 doc, string name)
        {
            for (int i = 0; i < doc.Sensors.Count; i++)
            {
                if (doc.Sensors[i].Name == name)
                {
                    return doc.Sensors[i];
                }
            }
            return new SensorDefV3();
        }
        /// <summary>
        /// 生成器盒子表——写源收集（导线捕获 + 壳探测捕获）：Key → C# 类型
        /// </summary>
        /// <param name="doc">IR</param>
        /// <returns>盒子表</returns>
        private static Dictionary<string, string> GenBoxTypes(MauDocV3 doc)
        {
            Dictionary<string, string> boxTypes = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int w = 0; w < doc.Wires.Count; w++)
            {
                WireDefV3 wire = doc.Wires[w];
                if (wire.CaptureTarget.Length > 0 && !boxTypes.ContainsKey(wire.CaptureTarget))
                {
                    boxTypes[wire.CaptureTarget] = CaptureCSType(wire.BrickName);
                }
            }
            for (int s = 0; s < doc.Sensors.Count; s++)
            {
                SensorDefV3 sensor = doc.Sensors[s];
                if (!sensor.Passive && sensor.CaptureTarget.Length > 0 && !boxTypes.ContainsKey(sensor.CaptureTarget))
                {
                    boxTypes[sensor.CaptureTarget] = "bool";
                }
            }
            return boxTypes;
        }
        /// <summary>
        /// 盒子 Key 引用判定——@ 前缀私有盒或已注册写源的全局盒
        /// </summary>
        /// <param name="doc">IR</param>
        /// <param name="name">参数引用名</param>
        /// <returns>true=盒子引用</returns>
        private static bool IsValueSensor(MauDocV3 doc, string name)
{
            string t = name.Trim();
            // 盒子 Key 引用判定——@ 前缀 = 私有盒 / 已注册写源全局盒 / 裸词 = 全局盒（外部写源——宿主 Command 落盒）
            if (t.Length > 0 && t[0] == '@')
            {
                return true;
            }
            if (GenBoxTypes(doc).ContainsKey(t))
            {
                return true;
            }
            // 字符串字面量（"..."）与纯数字——非盒子引用
            if (t.Length >= 2 && t[0] == '"')
            {
                return false;
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
                return false;
            }
            // 全局盒裸词——动作参数中无 @ 的裸词即全局盒引用（B1 豁免面——类型默认 string）
            return true;
        }
        /// <summary>
        /// 单元体名 PascalCase（去前缀）——P_Go → Go / R_Slot → Slot（方法名用）
        /// </summary>
        /// <param name="name">声明名</param>
        /// <returns>去前缀 PascalCase</returns>
        private static string NameBodyPascal(string name)
{
            int sep = name.IndexOf('_');
            string body = name;
            if (sep >= 0)
            {
                body = name.Substring(sep + 1);
            }
            return NamePascal(body);
        }
        /// <summary>
        /// 名称字段化——S_Talk → _s_talk / P_Go → _p_go（前缀字母小写 + 下划线去尾）
        /// </summary>
        /// <param name="name">声明名</param>
        /// <returns>C# 字段名</returns>
        private static string NameField(string name)
{
            int sep = name.IndexOf('_');
            string rest = name;
            if (sep >= 0)
            {
                rest = name.Substring(sep + 1);
            }
            string result = "";
            for (int i = 0; i < rest.Length; i++)
            {
                char c = rest[i];
                if (c == '_')
                {
                    continue;
                }
                if (i == 0)
                {
                    result = result + char.ToLowerInvariant(c);
                }
                else
                {
                    result = result + c;
                }
            }
            return "_" + result;
        }
        /// <summary>
        /// 名称 PascalCase——S_Talk → STalk / file.read → FileRead / Idle → Idle
        /// </summary>
        /// <param name="name">声明名</param>
        /// <returns>PascalCase 标识符</returns>
        private static string NamePascal(string name)
        {
            string result = "";
            bool upperNext = true;
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (c == '_' || c == '.' || c == '-' || c == '@')
                {
                    upperNext = true;
                    continue;
                }
                if (upperNext)
                {
                    result = result + char.ToUpperInvariant(c);
                    upperNext = false;
                }
                else
                {
                    result = result + c;
                }
            }
            return result;
        }
    }
}