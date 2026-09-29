using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Mau.Development;
using Mau.Runtime;
using Xunit;

namespace Mau.Development.Tests
{
    /// <summary>
    /// 工具参数面对账测试（A7）——声明面（Bricks/TOOLS 工具定义 JSON）与校验面（MauRoslynBridge.ValidateToolArgs）一致性行为断言。
    /// 断言面：未知参数拒绝 / 必填缺值拒绝 / 枚举非法值拒绝 / 宿主注入保留键放行。
    /// </summary>
    public class ToolArgContractTests : IDisposable
    {
        /// <summary>
        /// 临时受控根
        /// </summary>
        private readonly string _root;

        /// <summary>
        /// 桥实例——校验发生在路径解析之前，故无需真实项目
        /// </summary>
        private readonly MauRoslynBridge _bridge;

        /// <summary>
        /// 创建夹具——临时受控根 + 桥
        /// </summary>
        public ToolArgContractTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "mau_a7_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
            WorkspaceConfig.RootEntry entry = new WorkspaceConfig.RootEntry();
            entry.Id = "tmp";
            entry.Path = _root;
            entry.Writable = true;
            _bridge = new MauRoslynBridge(new WorkspaceConfig.RootEntry[] { entry });
        }

        /// <summary>
        /// 清理——删临时根
        /// </summary>
        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_root))
                {
                    Directory.Delete(_root, true);
                }
            }
            catch (IOException ex)
            {
                // 测试夹具清理——尽力删除（目录被占用等不影响用例结论）
                Console.WriteLine("[测试清理] 工作区目录删除失败: " + ex.Message);
            }
        }

        /// <summary>
        /// 声明面工具规格——工具名 + 属性键 + 必填键
        /// </summary>
        private sealed class ToolSpec
        {
            /// <summary>
            /// 工具名（cs-xxx）
            /// </summary>
            public string Name = "";

            /// <summary>
            /// 声明面属性键
            /// </summary>
            public List<string> Properties = new List<string>();

            /// <summary>
            /// 声明面必填键
            /// </summary>
            public List<string> Required = new List<string>();
        }

        /// <summary>
        /// 读 CsCat 声明面——Bricks/TOOLS/BRIK-TOOLS-003 工具定义 JSON（转义字面量逐行拼接 → JSON 解析）
        /// </summary>
        /// <returns>工具规格列表</returns>
        private static List<ToolSpec> DeclarationTools()
        {
            string dir = AppContext.BaseDirectory;
            while (dir.Length > 3 && !Directory.Exists(Path.Combine(dir, "Bricks")))
            {
                dir = Path.GetDirectoryName(dir) ?? "";
            }
            string file = Path.Combine(dir, "Bricks", "TOOLS", "BRIK-TOOLS-003_tools.cscat.cs");
            Assert.True(File.Exists(file), "声明面积木缺失: " + file);
            List<ToolSpec> specs = new List<ToolSpec>();
            string[] lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i = i + 1)
            {
                Match nameMatch = Regex.Match(lines[i], "\\\\\"name\\\\\":\\\\\"(cs-[a-z_]+)\\\\\"");
                if (!nameMatch.Success)
                {
                    continue;
                }
                ToolSpec spec = new ToolSpec();
                spec.Name = nameMatch.Groups[1].Value;
                Match propsMatch = Regex.Match(lines[i], "\\\\\"properties\\\\\":\\{(.*?)\\},\\\\\"required\\\\\"");
                if (propsMatch.Success)
                {
                    foreach (Match pm in Regex.Matches(propsMatch.Groups[1].Value, "\\\\\"([a-zA-Z_]+)\\\\\":\\{"))
                    {
                        spec.Properties.Add(pm.Groups[1].Value);
                    }
                }
                Match reqMatch = Regex.Match(lines[i], "\\\\\"required\\\\\":\\[(.*?)\\]");
                if (reqMatch.Success)
                {
                    foreach (Match rm in Regex.Matches(reqMatch.Groups[1].Value, "\\\\\"([a-zA-Z_]+)\\\\\""))
                    {
                        spec.Required.Add(rm.Groups[1].Value);
                    }
                }
                specs.Add(spec);
            }
            return specs;
        }

        /// <summary>
        /// 工具名 → 桥方法名（cs-check → check）
        /// </summary>
        /// <param name="toolName">工具名</param>
        /// <returns>方法名</returns>
        private static string MethodOf(string toolName)
        {
            return toolName.Substring(3);
        }

        /// <summary>
        /// 未知参数一律拒绝——声明面属性外的任意键 → ERR|BAD_ARGS|未知参数
        /// </summary>
        [Fact]
        public void UnknownArgRejected()
        {
            foreach (ToolSpec spec in DeclarationTools())
            {
                string result;
                _bridge.Invoke(MethodOf(spec.Name), "{\"zzz_unknown\":\"1\"}", out result);
                Assert.StartsWith("ERR|BAD_ARGS|未知参数", result);
            }
        }

        /// <summary>
        /// 必填缺值一律拒绝——逐个省略声明面必填键 → ERR|BAD_ARGS|缺参数
        /// </summary>
        [Fact]
        public void RequiredArgMissingRejected()
        {
            foreach (ToolSpec spec in DeclarationTools())
            {
                for (int skip = 0; skip < spec.Required.Count; skip = skip + 1)
                {
                    StringBuilder body = new StringBuilder("{");
                    for (int i = 0; i < spec.Required.Count; i = i + 1)
                    {
                        if (i == skip)
                        {
                            continue;
                        }
                        if (body.Length > 1)
                        {
                            body.Append(",");
                        }
                        body.Append("\"" + spec.Required[i] + "\":\"x\"");
                    }
                    body.Append("}");
                    string result;
                    _bridge.Invoke(MethodOf(spec.Name), body.ToString(), out result);
                    Assert.Contains("缺参数 " + spec.Required[skip], result);
                }
            }
        }

        /// <summary>
        /// 枚举非法值一律拒绝——op / mode / type / full 值域外取值 → ERR|BAD_ARGS
        /// </summary>
        [Fact]
        public void EnumValueRejected()
        {
            string result;
            _bridge.Invoke("member", "{\"path\":\"x\",\"class\":\"C\",\"op\":\"zzz\"}", out result);
            Assert.Contains("op 非法值", result);
            _bridge.Invoke("format", "{\"path\":\"x\",\"mode\":\"zzz\"}", out result);
            Assert.Contains("mode 非法值", result);
            _bridge.Invoke("comment", "{\"path\":\"x\",\"class\":\"C\",\"type\":\"zzz\",\"text\":\"t\"}", out result);
            Assert.Contains("type 非法值", result);
            _bridge.Invoke("check", "{\"path\":\"x\",\"full\":\"zzz\"}", out result);
            Assert.Contains("full 非法值", result);
        }

        /// <summary>
        /// code 与 codes 互斥——同给两参数一律拒绝（A92）；codes 非数组明示拒绝
        /// </summary>
        [Fact]
        public void CodeAndCodesMutuallyExclusive()
        {
            string result;
            _bridge.Invoke("member", "{\"path\":\"x\",\"class\":\"C\",\"op\":\"insert\",\"code\":\"public int A() { return 1; }\",\"codes\":[\"public int B() { return 2; }\"]}", out result);
            Assert.Contains("互斥", result);
            _bridge.Invoke("member", "{\"path\":\"x\",\"class\":\"C\",\"op\":\"insert\",\"codes\":\"nope\"}", out result);
            Assert.Contains("codes 必须是字符串数组", result);
        }

        /// <summary>
        /// 宿主注入保留键放行——catId 不进声明面，校验面一律放行（不报未知参数）
        /// </summary>
        [Fact]
        public void HostInjectedKeyAllowed()
        {
            string result;
            _bridge.Invoke("dead", "{\"path\":\"x\",\"catId\":\"majordomo\"}", out result);
            Assert.DoesNotContain("未知参数", result);
        }

        /// <summary>
        /// 工具定义 JSON 解析探针（A107）——遍历 Bricks/TOOLS 全部工具定义积木，反转义拼接后真正解析。
        /// 判例 2026-09-29：漏项间逗号 → JSON 非法 → 整组被 ToolPool 静默丢弃；原正则式断言"匹配成功"不等于"JSON 合法"。
        /// </summary>
        [Fact]
        public void ToolDefinitionJsonParses()
        {
            string dir = AppContext.BaseDirectory;
            while (dir.Length > 3 && !Directory.Exists(Path.Combine(dir, "Bricks")))
            {
                dir = Path.GetDirectoryName(dir) ?? "";
            }
            string toolsDir = Path.Combine(dir, "Bricks", "TOOLS");
            Assert.True(Directory.Exists(toolsDir), "工具定义积木目录缺失: " + toolsDir);
            string[] files = Directory.GetFiles(toolsDir, "BRIK-TOOLS-*.cs");
            Assert.NotEmpty(files);
            for (int f = 0; f < files.Length; f = f + 1)
            {
                string name = Path.GetFileName(files[f]);
                string json = ExtractToolsJson(files[f]);
                Assert.True(json.Length > 0, "未提取到工具定义 JSON: " + name);
                JsonDocument doc = null!;
                try
                {
                    doc = JsonDocument.Parse(json);
                }
                catch (JsonException ex)
                {
                    Assert.Fail("工具定义 JSON 非法: " + name + " —— " + ex.Message);
                }
                JsonElement root = doc.RootElement;
                JsonElement groupEl;
                Assert.True(root.TryGetProperty("group", out groupEl), "缺 group 字段: " + name);
                JsonElement toolsEl;
                Assert.True(root.TryGetProperty("tools", out toolsEl), "缺 tools 数组: " + name);
                Assert.Equal(JsonValueKind.Array, toolsEl.ValueKind);
                for (int t = 0; t < toolsEl.GetArrayLength(); t = t + 1)
                {
                    JsonElement item = toolsEl[t];
                    JsonElement itemName;
                    Assert.True(item.TryGetProperty("name", out itemName), "工具条目缺 name: " + name);
                    Assert.Equal(JsonValueKind.String, itemName.ValueKind);
                }
                doc.Dispose();
            }
        }

        /// <summary>
        /// 提取工具定义积木的 JSON——取 GetToolsJson 的 return 语句区间，反转义拼接全部字符串字面量（A107 探针）。
        /// </summary>
        /// <param name="file">积木源码路径</param>
        /// <returns>拼接后的 JSON 文本（未提取到 = 空串）</returns>
        private static string ExtractToolsJson(string file)
        {
            string text = File.ReadAllText(file);
            int start = text.IndexOf("return \"", StringComparison.Ordinal);
            if (start < 0)
            {
                return "";
            }
            int end = text.IndexOf(';', start);
            if (end <= start)
            {
                return "";
            }
            string region = text.Substring(start, end - start);
            StringBuilder json = new StringBuilder();
            int i = 0;
            while (i < region.Length)
            {
                if (region[i] != '"')
                {
                    i = i + 1;
                    continue;
                }
                i = i + 1;
                StringBuilder literal = new StringBuilder();
                while (i < region.Length && region[i] != '"')
                {
                    if (region[i] == '\\' && i + 1 < region.Length)
                    {
                        literal.Append(region[i]);
                        literal.Append(region[i + 1]);
                        i = i + 2;
                        continue;
                    }
                    literal.Append(region[i]);
                    i = i + 1;
                }
                i = i + 1;
                json.Append(UnescapeLiteral(literal.ToString()));
            }
            return json.ToString();
        }

        /// <summary>
        /// C# 字面量反转义——\" \\ \n \r \t 还原（A107 探针；其余原样保留）。
        /// </summary>
        /// <param name="literal">字面量内容（不含引号）</param>
        /// <returns>还原文本</returns>
        private static string UnescapeLiteral(string literal)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < literal.Length; i = i + 1)
            {
                if (literal[i] == '\\' && i + 1 < literal.Length)
                {
                    char next = literal[i + 1];
                    if (next == '"')
                    {
                        sb.Append('"');
                        i = i + 1;
                        continue;
                    }
                    if (next == '\\')
                    {
                        sb.Append('\\');
                        i = i + 1;
                        continue;
                    }
                    if (next == 'n')
                    {
                        sb.Append('\n');
                        i = i + 1;
                        continue;
                    }
                    if (next == 'r')
                    {
                        sb.Append('\r');
                        i = i + 1;
                        continue;
                    }
                    if (next == 't')
                    {
                        sb.Append('\t');
                        i = i + 1;
                        continue;
                    }
                }
                sb.Append(literal[i]);
            }
            return sb.ToString();
        }

        /// <summary>
        /// 多项目入口解析——.sln 聚合分派（受控根 = 仓库根；断言分项目分组输出）
        /// </summary>
        [Fact]
        public void SolutionEntryAggregates()
        {
            string dir = AppContext.BaseDirectory;
            while (dir.Length > 3 && !File.Exists(Path.Combine(dir, "Mau.sln")))
            {
                dir = Path.GetDirectoryName(dir) ?? "";
            }
            string sln = Path.Combine(dir, "Mau.sln");
            Assert.True(File.Exists(sln), "仓库根 sln 缺失: " + sln);
            WorkspaceConfig.RootEntry entry = new WorkspaceConfig.RootEntry();
            entry.Id = "repo";
            entry.Path = dir;
            entry.Writable = false;
            MauRoslynBridge bridge = new MauRoslynBridge(new WorkspaceConfig.RootEntry[] { entry });
            string result;
            bridge.Invoke("list", "{\"path\":\"" + sln.Replace("\\", "\\\\") + "\"}", out result);
            Assert.Contains("聚合 ", result);
            Assert.Contains("Mau.Contracts", result);
            Assert.Contains("Mau.Runtime", result);
        }
    }
}
