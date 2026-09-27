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
            catch (IOException)
            {
                // 测试夹具清理——尽力删除（目录被占用等不影响用例结论）
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
