#nullable disable warnings

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace Mau.Development.Tests
{
    /// <summary>
    /// 工具声明面 ↔ 校验面对账（A99）——Bricks/TOOLS 工具定义 JSON（LLM 消费面）与积木内联 ValidateArgs 字面量（宿主校验面）文本级一致性。
    /// 动机：参数契约手写在两处（声明面给 LLM 填参 / 校验面拒绝非法）——改一边另一边不跟，编译与门禁均不可见（同族判例：BRIK-TOOLS-009 JSON 畸形整组静默丢弃）。
    /// 覆盖：TextCat / FileCat / MauCat / VisionCat / SearchCat / TempToolCat（标准五参 ValidateArgs 形态）。
    /// 豁免：CsCat（ToolArgContractTests 行为级已覆盖）· ConfigCat（config.bridge 按 method 分支设变量，静态不可判）· MajordomoCat（单参重载）· 无参工具（声明面 properties 空——如 temp-info，语料层直接认领）。
    /// </summary>
    public class ToolDeclarationContractTests
    {
        /// <summary>
        /// 组规格——组名 + 声明面积木 + 校验面积木目录
        /// </summary>
        private sealed class GroupSpec
        {
            /// <summary>组名（Flow 注册名）</summary>
            public string Group = "";

            /// <summary>声明面积木文件名（Bricks/TOOLS 下）</summary>
            public string DeclFile = "";

            /// <summary>校验面积木目录名（Bricks 下）</summary>
            public string BrickDir = "";
        }

        /// <summary>
        /// 声明面工具规格——工具名 + 属性键 + 必填键 + 原始行
        /// </summary>
        private sealed class DeclTool
        {
            /// <summary>工具名</summary>
            public string Name = "";

            /// <summary>声明面属性键（有序）</summary>
            public List<string> Properties = new List<string>();

            /// <summary>声明面必填键（有序）</summary>
            public List<string> Required = new List<string>();

            /// <summary>声明面原始行——枚举值域宽松断言用</summary>
            public string Line = "";
        }

        /// <summary>
        /// 校验面规格——ValidateArgs 五参字面量
        /// </summary>
        private sealed class ValidSpec
        {
            /// <summary>允许键（空格分隔）</summary>
            public string Allowed = "";

            /// <summary>必填键（空格分隔）</summary>
            public string Required = "";

            /// <summary>枚举参数名（空=无）</summary>
            public string EnumName = "";

            /// <summary>枚举值域（| 分隔）</summary>
            public string EnumValues = "";
        }

        /// <summary>工具名 ↔ 积木名特例（少数不一致样本）</summary>
        private static readonly Dictionary<string, string> NameAlias = BuildAlias();

        /// <summary>
        /// 建工具名 ↔ 积木名特例表。
        /// </summary>
        /// <returns>特例映射</returns>
        private static Dictionary<string, string> BuildAlias()
        {
            Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.Ordinal);
            map["image-analyze"] = "vision.analyze";
            return map;
        }

        /// <summary>
        /// 组清单——声明面积木 + 校验面积木目录。
        /// </summary>
        /// <returns>组规格列表</returns>
        private static List<GroupSpec> GroupSpecs()
        {
            List<GroupSpec> list = new List<GroupSpec>();
            list.Add(MakeGroup("TextCat", "BRIK-TOOLS-001_tools.textcat.cs", "TEXT"));
            list.Add(MakeGroup("FileCat", "BRIK-TOOLS-011_tools.filecat.cs", "FILE"));
            list.Add(MakeGroup("MauCat", "BRIK-TOOLS-002_tools.maucat.cs", "MAU"));
            list.Add(MakeGroup("VisionCat", "BRIK-TOOLS-006_tools.visioncat.cs", "VISION"));
            list.Add(MakeGroup("SearchCat", "BRIK-TOOLS-005_tools.searchcat.cs", "WEB"));
            list.Add(MakeGroup("TempToolCat", "BRIK-TOOLS-007_tools.temptoolcat.cs", "TEMP"));
            return list;
        }

        /// <summary>
        /// 构造组规格。
        /// </summary>
        /// <param name="group">组名</param>
        /// <param name="declFile">声明面积木文件名</param>
        /// <param name="brickDir">校验面积木目录名</param>
        /// <returns>组规格</returns>
        private static GroupSpec MakeGroup(string group, string declFile, string brickDir)
        {
            GroupSpec spec = new GroupSpec();
            spec.Group = group;
            spec.DeclFile = declFile;
            spec.BrickDir = brickDir;
            return spec;
        }

        /// <summary>
        /// 仓库根——自测试程序集目录上溯找 Bricks。
        /// </summary>
        /// <returns>仓库根绝对路径</returns>
        private static string RepoRoot()
        {
            string dir = AppContext.BaseDirectory;
            while (dir.Length > 3 && !Directory.Exists(Path.Combine(dir, "Bricks")))
            {
                string? parent = Path.GetDirectoryName(dir);
                if (parent == null)
                {
                    break;
                }
                dir = parent;
            }
            return dir;
        }

        /// <summary>
        /// 提取 properties 对象体——字符级括号配平（嵌套对象下正则非贪婪易失配，配平为唯一可靠口径）。
        /// </summary>
        /// <param name="line">声明面原始行</param>
        /// <returns>properties 对象体（含首尾花括号）；未找到 = 空串</returns>
        private static string PropertiesBody(string line)
        {
            string marker = "properties\\\":{";
            int start = line.IndexOf(marker, StringComparison.Ordinal);
            if (start < 0)
            {
                return "";
            }
            int open = start + marker.Length - 1;
            int depth = 0;
            for (int i = open; i < line.Length; i = i + 1)
            {
                char c = line[i];
                if (c == '{')
                {
                    depth = depth + 1;
                }
                else if (c == '}')
                {
                    depth = depth - 1;
                    if (depth == 0)
                    {
                        return line.Substring(open, i - open + 1);
                    }
                }
            }
            return "";
        }

        /// <summary>
        /// 解析声明面——Bricks/TOOLS/&lt;文件&gt; 逐行提取工具名 / 属性键 / 必填键。
        /// </summary>
        /// <param name="declFile">声明面积木文件名</param>
        /// <returns>声明面工具规格列表</returns>
        private static List<DeclTool> DeclaredTools(string declFile)
        {
            string file = Path.Combine(RepoRoot(), "Bricks", "TOOLS", declFile);
            Assert.True(File.Exists(file), "声明面积木缺失: " + file);
            List<DeclTool> specs = new List<DeclTool>();
            string[] lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i = i + 1)
            {
                Match nameMatch = Regex.Match(lines[i], "\\\\\"name\\\\\":\\\\\"([a-z_-]+)\\\\\"");
                if (!nameMatch.Success)
                {
                    continue;
                }
                DeclTool tool = new DeclTool();
                tool.Name = nameMatch.Groups[1].Value;
                tool.Line = lines[i];
                string body = PropertiesBody(lines[i]);
                if (body.Length > 0)
                {
                    foreach (Match pm in Regex.Matches(body, "\\\\\"([a-zA-Z0-9_]+)\\\\\":\\{"))
                    {
                        tool.Properties.Add(pm.Groups[1].Value);
                    }
                }
                Match reqMatch = Regex.Match(lines[i], "\\\\\"required\\\\\":\\[(.*?)\\]");
                if (reqMatch.Success)
                {
                    foreach (Match rm in Regex.Matches(reqMatch.Groups[1].Value, "\\\\\"([a-zA-Z0-9_]+)\\\\\""))
                    {
                        tool.Required.Add(rm.Groups[1].Value);
                    }
                }
                specs.Add(tool);
            }
            return specs;
        }

        /// <summary>
        /// 解析校验面——积木源码里 ValidateArgs 五参调用字面量（键集合 / 必填 / 枚举）。
        /// </summary>
        /// <param name="brickDir">积木目录名</param>
        /// <param name="brickName">积木内嵌名（如 text.replace）</param>
        /// <returns>校验面规格；未找到 = null</returns>
        private static ValidSpec ValidatedSpec(string brickDir, string brickName)
        {
            string dir = Path.Combine(RepoRoot(), "Bricks", brickDir);
            if (!Directory.Exists(dir))
            {
                return null;
            }
            string[] files = Directory.GetFiles(dir, "*.cs");
            for (int i = 0; i < files.Length; i = i + 1)
            {
                string name = Path.GetFileNameWithoutExtension(files[i]);
                int cut = name.IndexOf('_');
                if (cut < 0)
                {
                    continue;
                }
                if (name.Substring(cut + 1) != brickName)
                {
                    continue;
                }
                string text = File.ReadAllText(files[i]);
                Match call = Regex.Match(text, "ValidateArgs\\(argsJson,\\s*\"([^\"]*)\"\\s*,\\s*\"([^\"]*)\"(?:\\s*,\\s*\"([^\"]*)\"\\s*,\\s*\"([^\"]*)\")?\\)");
                if (!call.Success)
                {
                    return new ValidSpec();
                }
                ValidSpec spec = new ValidSpec();
                spec.Allowed = call.Groups[1].Value;
                spec.Required = call.Groups[2].Value;
                spec.EnumName = call.Groups[3].Value;
                spec.EnumValues = call.Groups[4].Value;
                return spec;
            }
            return null;
        }

        /// <summary>
        /// 空格分隔串 → 有序键列表（去空）。
        /// </summary>
        /// <param name="raw">原始串</param>
        /// <returns>键列表</returns>
        private static List<string> SplitKeys(string raw)
        {
            List<string> list = new List<string>();
            if (raw == null)
            {
                return list;
            }
            string[] parts = raw.Split(' ');
            for (int i = 0; i < parts.Length; i = i + 1)
            {
                if (parts[i].Length > 0)
                {
                    list.Add(parts[i]);
                }
            }
            return list;
        }

        /// <summary>
        /// 声明面 ↔ 校验面一致性——键集合 / 必填集合 / 枚举值域首值，逐组逐工具断言。
        /// </summary>
        [Fact]
        public void DeclarationMatchesValidation()
        {
            int checkedTools = 0;
            int skippedNoArg = 0;
            List<GroupSpec> groups = GroupSpecs();
            for (int g = 0; g < groups.Count; g = g + 1)
            {
                GroupSpec group = groups[g];
                List<DeclTool> declared = DeclaredTools(group.DeclFile);
                Assert.True(declared.Count > 0, group.Group + " 声明面未解析出工具");
                for (int t = 0; t < declared.Count; t = t + 1)
                {
                    DeclTool tool = declared[t];
                    if (tool.Properties.Count == 0 && tool.Required.Count == 0)
                    {
                        skippedNoArg = skippedNoArg + 1;
                        continue;
                    }
                    string brickName = tool.Name.Replace('-', '.');
                    string alias;
                    if (NameAlias.TryGetValue(tool.Name, out alias))
                    {
                        brickName = alias;
                    }
                    ValidSpec valid = ValidatedSpec(group.BrickDir, brickName);
                    Assert.True(valid != null, group.Group + "/" + tool.Name + " 校验面积木缺失（期望 " + group.BrickDir + "/*_" + brickName + ".cs）");
                    Assert.True(valid.Allowed.Length > 0, group.Group + "/" + tool.Name + " 校验面非五参 ValidateArgs 形态（声明面有参数却无字面量）");
                    List<string> allowed = SplitKeys(valid.Allowed);
                    Assert.True(allowed.Count == tool.Properties.Count,
                        group.Group + "/" + tool.Name + " 键集合数量不一致：声明 " + string.Join(",", tool.Properties) + " ↔ 校验 " + valid.Allowed);
                    for (int k = 0; k < tool.Properties.Count; k = k + 1)
                    {
                        Assert.True(allowed.Contains(tool.Properties[k]),
                            group.Group + "/" + tool.Name + " 声明面键 " + tool.Properties[k] + " 不在校验面（" + valid.Allowed + "）");
                    }
                    List<string> must = SplitKeys(valid.Required);
                    Assert.True(must.Count == tool.Required.Count,
                        group.Group + "/" + tool.Name + " 必填集合数量不一致：声明 " + string.Join(",", tool.Required) + " ↔ 校验 " + valid.Required);
                    for (int k = 0; k < tool.Required.Count; k = k + 1)
                    {
                        Assert.True(must.Contains(tool.Required[k]),
                            group.Group + "/" + tool.Name + " 声明面必填 " + tool.Required[k] + " 不在校验面（" + valid.Required + "）");
                    }
                    if (valid.EnumName.Length > 0 && valid.EnumValues.Length > 0)
                    {
                        string firstValue = valid.EnumValues.Split('|')[0];
                        Assert.True(tool.Line.Contains(firstValue),
                            group.Group + "/" + tool.Name + " 枚举值域未声明：" + valid.EnumName + " 首值 " + firstValue);
                    }
                    checkedTools = checkedTools + 1;
                }
            }
            Assert.True(checkedTools >= 15, "对账覆盖工具数异常偏低: " + checkedTools.ToString() + "（跳过无参 " + skippedNoArg.ToString() + "）");
        }
    }
}
