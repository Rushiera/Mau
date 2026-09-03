using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Mau.Translator
{
    /// <summary>
    /// MauGeneratorV3 积木面分部——内嵌积木源/BRIKGROUP 组闭包/强类型调用表达式/积木名收集。
    /// P7b partial 拆分——自 MauGeneratorV3.cs 原样搬移，逻辑零改动。
    /// </summary>
    public static partial class MauGeneratorV3
    {
        /// <summary>
        /// 内嵌积木源——收集生成物引用的积木 + 约定工具定义积木 tools.&lt;flowName&gt;，源文件原文贴入生成物（复制即单包，R1 形态）。
        /// 依赖闭包：P5 最小化阶段积木零依赖（index.json dependencies 全空）——闭包解析随积木重生扩展。
        /// </summary>
        /// <param name="sb">输出缓冲</param>
        /// <param name="doc">FSM 网络 IR</param>
        /// <param name="flowName">流程名（工具定义积木约定名 tools.&lt;flowName&gt;）</param>
        private static void AppendBrickSources(StringBuilder sb, MauDocV3 doc, string flowName)
        {
            List<string> names = new List<string>();
            for (int w = 0; w < doc.Wires.Count; w++)
            {
                for (int a = 0; a < doc.Wires[w].Actions.Count; a++)
                {
                    if (doc.Wires[w].Actions[a].BrickName.Length > 0 && !names.Contains(doc.Wires[w].Actions[a].BrickName))
                    {
                        names.Add(doc.Wires[w].Actions[a].BrickName);
                    }
                }
            }
            for (int s = 0; s < doc.Sensors.Count; s++)
            {
                if (!doc.Sensors[s].Passive && doc.Sensors[s].BrickName.Length > 0 && !names.Contains(doc.Sensors[s].BrickName))
                {
                    names.Add(doc.Sensors[s].BrickName);
                }
            }
            // 工具定义积木——约定名 tools.<flowName>（GetToolsJson 调用面；非语料引用，强制收录）
            string toolsBrick = "tools." + flowName.ToLowerInvariant();
            if (!names.Contains(toolsBrick))
            {
                names.Add(toolsBrick);
            }
            if (names.Count == 0)
            {
                return;
            }
            string root = BrickIndex.FindRepoRoot();
            for (int i = 0; i < names.Count; i++)
            {
                BrickIndexEntry entry;
                if (!BrickIndex.TryFind(names[i], out entry) || entry.Path.Length == 0)
                {
                    continue;
                }
                string path = Path.Combine(root, "Bricks", entry.Path);
                if (!File.Exists(path))
                {
                    continue;
                }
                sb.AppendLine("// ═══ 内嵌积木: " + names[i] + "（来源 Bricks/" + entry.Path + "——复制即单包）═══");
                // 剥 using 行——生成物文件级 using 统一在头部（CS1529 判例）
                string source = File.ReadAllText(path);
                string[] lines = source.Split('\n');
                for (int l = 0; l < lines.Length; l++)
                {
                    string trimmed = lines[l].Trim();
                    if (trimmed.StartsWith("using ", StringComparison.Ordinal))
                    {
                        continue;
                    }
                    sb.AppendLine(lines[l].TrimEnd('\r'));
                }
                sb.AppendLine("");
            }
        }

        /// <summary>
        /// 生成 BRIKGROUP.cs——组内全部引用积木源码合并去重（组模式共享闭包，namespace Mau.Bricks）。
        /// 含约定工具定义积木 tools.&lt;flowName&gt;（GetToolsJson 调用面——语料不引用，强制收录）。
        /// 对应 design-ch4-deploy.md §3.2：一组一份 BRIKGROUP.cs，FL_*.cs 只调用不内嵌。
        /// </summary>
        /// <param name="docs">组内全部 IR</param>
        /// <param name="flowNames">组内流程名数组（与 docs 对齐——工具定义积木约定名）</param>
        /// <returns>BRIKGROUP.cs 全文（空 = 组无积木引用）</returns>
        public static string GenerateBrickGroup(List<MauDocV3> docs, string[] flowNames)
        {
            List<string> names = new List<string>();
            for (int d = 0; d < docs.Count; d++)
            {
                List<string> docNames = CollectBrickNames(docs[d]);
                for (int i = 0; i < docNames.Count; i++)
                {
                    if (!names.Contains(docNames[i]))
                    {
                        names.Add(docNames[i]);
                    }
                }
                // 工具定义积木——约定名 tools.<flowName>（GetToolsJson 调用面）
                if (flowNames != null && d < flowNames.Length)
                {
                    string toolsBrick = "tools." + flowNames[d].ToLowerInvariant();
                    if (!names.Contains(toolsBrick))
                    {
                        names.Add(toolsBrick);
                    }
                }
            }
            if (names.Count == 0)
            {
                return "";
            }
            StringBuilder sb = new StringBuilder();
            List<string> usings = new List<string>();
            string root = BrickIndex.FindRepoRoot();
            for (int i = 0; i < names.Count; i++)
            {
                BrickIndexEntry entry;
                if (!BrickIndex.TryFind(names[i], out entry) || entry.Path.Length == 0)
                {
                    continue;
                }
                string path = Path.Combine(root, "Bricks", entry.Path);
                if (!File.Exists(path))
                {
                    continue;
                }
                sb.AppendLine("// ═══ 积木: " + names[i] + "（来源 Bricks/" + entry.Path + "——复制即单包）═══");
                string source = File.ReadAllText(path);
                string[] lines = source.Split('\n');
                for (int l = 0; l < lines.Length; l++)
                {
                    string trimmed = lines[l].Trim();
                    if (trimmed.StartsWith("using ", StringComparison.Ordinal))
                    {
                        if (!usings.Contains(trimmed))
                        {
                            usings.Add(trimmed);
                        }
                        continue;
                    }
                    if (trimmed.StartsWith("#nullable", StringComparison.Ordinal))
                    {
                        // D6：nullable 指令剥离——组合文件头统一唯一标记（避免拼接重复/上下文串扰）
                        continue;
                    }
                    sb.AppendLine(lines[l].TrimEnd('\r'));
                }
                sb.AppendLine("");
            }
            StringBuilder head = new StringBuilder();
            head.AppendLine("// 生成: Mau v3.0 | BRIKGROUP | 组共享积木闭包（design-ch4-deploy.md §3.2）");
            for (int u = 0; u < usings.Count; u++)
            {
                head.AppendLine(usings[u]);
            }
            head.AppendLine("#nullable enable");
            head.AppendLine("");
            head.Append(sb.ToString());
            return head.ToString();
        }

        /// <summary>
        /// 积木强类型调用表达式——Mau.Bricks.Xxx.Yyy(args, out _)——编译期验型（P5 协议 A）。
        /// 索引未命中兑底 false（E4xx 已拦，理论不可达）。
        /// </summary>
        /// <param name="brickName">积木名</param>
        /// <param name="args">参数原文列表</param>
        /// <returns>调用表达式文本</returns>
        private static string BrickCallExpr(string brickName, List<string> args, string captureField, MauDocV3 doc, StringBuilder prelude)
        {
            BrickIndexEntry entry;
            if (!BrickIndex.TryFind(brickName, out entry) || entry.Implementation.Length == 0)
            {
                return "false";
            }
            string s = entry.Implementation + "(";
            bool first = true;
            for (int i = 0; i < args.Count; i++)
            {
                if (!first)
                {
                    s = s + ", ";
                }
                first = false;
                string arg = args[i];
                string type = "";
                if (i < entry.InputTypes.Count)
                {
                    type = entry.InputTypes[i];
                }
                if (IsValueSensor(doc, arg))
                {
                    // 盒子 Key 引用——类型化前置取数（DataBox 位置），表达式用局部变量
                    string boxType = "string";
                    Dictionary<string, string> boxTypes = GenBoxTypes(doc);
                    if (boxTypes.ContainsKey(arg))
                    {
                        boxType = boxTypes[arg];
                    }
                    string local = "v_" + i.ToString();
                    if (boxType == "long")
                    {
                        prelude.AppendLine("                long " + local + " = 0;");
                    }
                    else if (boxType == "int")
                    {
                        prelude.AppendLine("                int " + local + " = 0;");
                    }
                    else if (boxType == "bool")
                    {
                        prelude.AppendLine("                bool " + local + " = false;");
                    }
                    else
                    {
                        prelude.AppendLine("                string " + local + " = \"\";");
                    }
                    prelude.AppendLine("                DataBox.TryGet<" + boxType + ">(" + BoxScopeExpr(arg) + ", \"" + BoxKey(arg) + "\", out " + local + ");");
                    s = s + local;
                }
                else if (type == "int" || type == "long")
                {
                    s = s + arg;
                }
                else if (arg.Length >= 2 && arg[0] == '"')
                {
                    s = s + arg;
                }
                else
                {
                    s = s + "\"" + arg + "\"";
                }
            }
            for (int o = 0; o < entry.OutputCount; o++)
            {
                if (!first)
                {
                    s = s + ", ";
                }
                first = false;
                if (o == 0 && captureField.Length > 0)
                {
                    // 捕获子句——首个 out 端口绑定捕获局部变量
                    s = s + "out " + captureField;
                }
                else
                {
                    s = s + "out _";
                }
            }
            s = s + ")";
            return s;
        }

        /// <summary>
        /// 收集 IR 引用积木名——导线 + 主动传感器（去重有序）
        /// </summary>
        /// <param name="doc">FSM 网络 IR</param>
        /// <returns>积木名列表</returns>
        private static List<string> CollectBrickNames(MauDocV3 doc)
        {
            List<string> names = new List<string>();
            for (int w = 0; w < doc.Wires.Count; w++)
            {
                for (int a = 0; a < doc.Wires[w].Actions.Count; a++)
                {
                    if (doc.Wires[w].Actions[a].BrickName.Length > 0 && !names.Contains(doc.Wires[w].Actions[a].BrickName))
                    {
                        names.Add(doc.Wires[w].Actions[a].BrickName);
                    }
                }
            }
            for (int s = 0; s < doc.Sensors.Count; s++)
            {
                if (!doc.Sensors[s].Passive && doc.Sensors[s].BrickName.Length > 0 && !names.Contains(doc.Sensors[s].BrickName))
                {
                    names.Add(doc.Sensors[s].BrickName);
                }
            }
            return names;
        }
    }
}