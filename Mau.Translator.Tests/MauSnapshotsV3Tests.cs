#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using Mau.Contracts;
using Mau.Translator;
using Xunit;

namespace Mau.Translator.Tests
{
    /// <summary>
    /// v3 语法谱测试——5 行为样例全编译 + 黄金哈希 TokenId 流两种漂移行为
    /// </summary>
    public class MauSnapshotsV3Tests
    {
        /// <summary>
        /// 仓库根探测——Mau.sln 锚点向上找
        /// </summary>
        /// <returns>仓库根</returns>
        private static string FindRepoRoot()
        {
            DirectoryInfo dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Mau.sln")))
            {
                dir = dir.Parent;
            }
            Assert.NotNull(dir);
            return dir.FullName;
        }

        /// <summary>
        /// 读样例语料
        /// </summary>
        /// <param name="caseName">样例文件名</param>
        /// <returns>语料文本</returns>
        private static string ReadCase(string caseName)
        {
            string root = FindRepoRoot();
            return File.ReadAllText(Path.Combine(root, "Mau.Snapshots", "cases", caseName), System.Text.Encoding.UTF8);
        }

        /// <summary>
        /// 五样例全编译——语言表达力验收清单（CH4 行为类型全覆盖）
        /// </summary>
        [Fact]
        public void AllCases_Compile()
        {
            string[] cases = new string[] { "talk.mau", "tool_loop.mau", "poll.mau", "ui_route.mau", "counter.mau" };
            for (int i = 0; i < cases.Length; i++)
            {
                string source = ReadCase(cases[i]);
                string flowName = FlowName(cases[i]);
                CompileResultV3 result = MauCompilerV3.Compile(source, flowName);
                Assert.True(result.Success, cases[i] + " 编译失败: " + FormatDiags(result));
                Assert.Contains("public sealed class FL_" + flowName, result.GeneratedCode);
            }
        }

        /// <summary>
        /// 黄金哈希——换外观零漂移：ASCII 外观等价语料的 TokenId 流哈希与默认外观相同
        /// </summary>
        [Fact]
        public void GoldenHash_AppearanceChange_NoDrift()
        {
            string source = ReadCase("talk.mau");
            string hashDefault = TokenFlowV3.Hash(source, new DefaultAppearance());
            Assert.NotEqual("", hashDefault);

            // ASCII 外观——语义等价拼写
            string asciiIn = ((char)60).ToString() + ((char)45).ToString();
            string asciiSource =
                "## 'S_Talk' = { 'Idle', 'Thinking', 'Done' }\n" +
                "## 'P_Go' " + asciiIn + "\n" +
                "## 'P_Fail' " + asciiIn + "\n" +
                "## 'T_Start' [t=30] : 'P_Go' & 'S_Talk' = 'Idle' -> 'llm.chat'[\"hi\"] | 'S_Talk' = 'Thinking' | 'S_Talk' = 'Done'\n" +
                "## 'T_Retry' : 'P_Fail' & 'S_Talk' = 'Thinking' -> 'llm.chat'[\"retry\"] | 'S_Talk' = 'Thinking' | 'S_Talk' = 'Done'";
            string hashAscii = TokenFlowV3.Hash(asciiSource, new AsciiAppearance());
            Assert.Equal(hashDefault, hashAscii);
        }

        /// <summary>
        /// 黄金哈希——语义变更漂移：状态名改动 → 哈希变化（改动影响面反证）
        /// </summary>
        [Fact]
        public void GoldenHash_SemanticChange_Drift()
        {
            string source = ReadCase("talk.mau");
            string hash1 = TokenFlowV3.Hash(source, new DefaultAppearance());
            string changed = source.Replace("'Thinking'", "'Thinking2'");
            string hash2 = TokenFlowV3.Hash(changed, new DefaultAppearance());
            Assert.NotEqual(hash1, hash2);
        }

        /// <summary>
        /// Token 流序列化——id:value|id:value 形态
        /// </summary>
        [Fact]
        public void Serialize_Shape()
        {
            LexResultV3 lex = MauLexerV3.Lex("§ 'S_A' = { 'X' }", new DefaultAppearance());
            Assert.True(lex.Success);
            string flow = TokenFlowV3.Serialize(lex.Tokens);
            Assert.StartsWith(TokenIds.Section + "|" + TokenIds.Name + ":S_A", flow);
            Assert.EndsWith(TokenIds.Eof.ToString(), flow);
        }

        /// <summary>
        /// 诊断文本化
        /// </summary>
        /// <param name="result">编译结果</param>
        /// <returns>诊断摘要</returns>
        private static string FormatDiags(CompileResultV3 result)
        {
            string s = "";
            for (int i = 0; i < result.Diagnostics.Count; i++)
            {
                s = s + result.Diagnostics[i].Code + ":" + result.Diagnostics[i].Message + "\n";
            }
            return s;
        }

        /// <summary>
        /// 流程名——talk.mau → Talk
        /// </summary>
        /// <param name="caseName">文件名</param>
        /// <returns>PascalCase 流程名</returns>
        private static string FlowName(string caseName)
        {
            string baseName = Path.GetFileNameWithoutExtension(caseName);
            string[] parts = baseName.Split('_');
            string result = "";
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].Length == 0)
                {
                    continue;
                }
                result = result + char.ToUpperInvariant(parts[i][0]) + parts[i].Substring(1);
            }
            return result;
        }

        /// <summary>
        /// ASCII 测试外观——语义等价拼写（验证外观接口可替换性）：
        /// ## = § / &lt;- = ⇐ / @ = ↻ / -&gt; = →
        /// </summary>
        private sealed class AsciiAppearance : ISymbolAppearance
        {
            /// <summary>
            /// 单字符表
            /// </summary>
            private static readonly Dictionary<char, uint> Single = new Dictionary<char, uint>
            {
                { '=', TokenIds.Eq },
                { ':', TokenIds.Colon },
                { '|', TokenIds.Branch },
                { '&', TokenIds.And },
                { '{', TokenIds.SetOpen },
                { '}', TokenIds.SetClose },
                { ',', TokenIds.Sep },
                { '[', TokenIds.ParamOpen },
                { ']', TokenIds.ParamClose },
                { '@', TokenIds.Sample },
            };

            /// <summary>
            /// 多字符表
            /// </summary>
            private static readonly Dictionary<string, uint> Multi = new Dictionary<string, uint>(StringComparer.Ordinal)
            {
                { "##", TokenIds.Section },
                { ":=", TokenIds.Declare },
                { "->", TokenIds.Arrow },
                { ((char)60).ToString() + ((char)45).ToString(), TokenIds.In },
            };

            /// <summary>
            /// 外观名
            /// </summary>
            public string AppearanceName
            {
                get { return "ascii-test"; }
            }

            /// <summary>
            /// 字形回显
            /// </summary>
            /// <param name="tokenId">TokenId</param>
            /// <returns>字形</returns>
            public string Glyph(uint tokenId)
            {
                return "";
            }

            /// <summary>
            /// 字形映射
            /// </summary>
            /// <param name="glyph">字形</param>
            /// <param name="tokenId">输出</param>
            /// <returns>命中为真</returns>
            public bool TryMap(string glyph, out uint tokenId)
            {
                if (glyph.Length == 2 && Multi.TryGetValue(glyph, out tokenId))
                {
                    return true;
                }
                if (glyph.Length == 1 && Single.TryGetValue(glyph[0], out tokenId))
                {
                    return true;
                }
                tokenId = 0;
                return false;
            }
        }
    }
}
