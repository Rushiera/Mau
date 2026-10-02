using System;
using System.IO;
using System.Text;
using Mau.Runtime;
using Xunit;

namespace Mau.Runtime.Tests
{
    /// <summary>
    /// 配置声明通道测试（A152）——Declare 同时写模板与运行副本、内存项即时可写、非法键 / 重复键 / 模板缺失一律拒绝。
    /// 隔离：临时目录 Guid 唯一命名 + finally 清理。
    /// </summary>
    public sealed class ConfigDeclareTests
    {
        /// <summary>
        /// 建临时模板与运行副本（内容一致）——返回模板路径，运行副本路径经出参带回。
        /// </summary>
        /// <param name="sourcePath">运行副本路径（出参）</param>
        /// <returns>模板路径</returns>
        private static string MakeSchemaPair(out string sourcePath)
        {
            string dir = Path.Combine(Path.GetTempPath(), "declcfg_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            string template = Path.Combine(dir, "schema.json.example");
            sourcePath = Path.Combine(dir, "schema.json");
            string[] lines = new string[]
            {
                @"{",
                @"  ""_说明"": ""测试模板"",",
                @"  ""items"": [",
                @"    { ""key"": ""ui.font_scale"", ""file"": ""ui.cfg"", ""default"": ""100"", ""writable"": true, ""type"": ""int"", ""desc"": ""字号"" }",
                @"  ]",
                @"}"
            };
            File.WriteAllLines(template, lines, new UTF8Encoding(false));
            File.Copy(template, sourcePath, true);
            return template;
        }

        /// <summary>临时目录清理（失败不阻断测试结论）。</summary>
        /// <param name="template">模板路径</param>
        private static void Clean(string template)
        {
            try
            {
                string dir = Path.GetDirectoryName(template) ?? "";
                if (dir != null && dir.Length > 0 && Directory.Exists(dir))
                {
                    Directory.Delete(dir, true);
                }
            }
            catch (Exception ex)
            {
                // 测试夹具清理——尽力删除（文件被占用等不影响用例结论）
                Console.WriteLine("[测试清理] 目录删除失败: " + ex.Message);
            }
        }

        /// <summary>声明新段——模板与运行副本同步落盘且内容一致，内存项立即可读可写。</summary>
        [Fact]
        public void Declare_WritesBothFilesAndMemory()
        {
            string sourcePath;
            string template = MakeSchemaPair(out sourcePath);
            try
            {
                ConfigSchema schema = ConfigSchema.Load(sourcePath);
                schema.TemplatePath = template;
                string fileName;
                bool newSegment;
                string error;
                Assert.True(schema.Declare("probe.enabled", "探针开关", out fileName, out newSegment, out error));
                Assert.Equal("", error);
                Assert.Equal("probe.cfg", fileName);
                Assert.True(newSegment);
                Assert.NotNull(schema.Find("probe.enabled"));
                string templateText = File.ReadAllText(template);
                string sourceText = File.ReadAllText(sourcePath);
                Assert.Contains("probe.enabled", templateText);
                Assert.Contains("probe.enabled", sourceText);
                Assert.Equal(templateText, sourceText);
                string validateError;
                Assert.True(schema.Validate("probe.enabled", "on", out validateError));
                Assert.Equal("", validateError);
            }
            finally
            {
                Clean(template);
            }
        }

        /// <summary>声明已有段的新键——沿用该段既有文件归属，且不标新段。</summary>
        [Fact]
        public void Declare_ReusesDeclaredSegmentFile()
        {
            string sourcePath;
            string template = MakeSchemaPair(out sourcePath);
            try
            {
                ConfigSchema schema = ConfigSchema.Load(sourcePath);
                schema.TemplatePath = template;
                string fileName;
                bool newSegment;
                string error;
                Assert.True(schema.Declare("ui.new_flag", "", out fileName, out newSegment, out error));
                Assert.Equal("ui.cfg", fileName);
                Assert.False(newSegment);
            }
            finally
            {
                Clean(template);
            }
        }

        /// <summary>无段前缀的键拒绝——错误文案指明段前缀格式。</summary>
        [Fact]
        public void Declare_RejectsKeyWithoutSegment()
        {
            string sourcePath;
            string template = MakeSchemaPair(out sourcePath);
            try
            {
                ConfigSchema schema = ConfigSchema.Load(sourcePath);
                schema.TemplatePath = template;
                string fileName;
                bool newSegment;
                string error;
                Assert.False(schema.Declare("noseg", "", out fileName, out newSegment, out error));
                Assert.Contains("段前缀", error);
            }
            finally
            {
                Clean(template);
            }
        }

        /// <summary>重复声明拒绝——已存在的键不覆盖。</summary>
        [Fact]
        public void Declare_RejectsDuplicateKey()
        {
            string sourcePath;
            string template = MakeSchemaPair(out sourcePath);
            try
            {
                ConfigSchema schema = ConfigSchema.Load(sourcePath);
                schema.TemplatePath = template;
                string fileName;
                bool newSegment;
                string error;
                Assert.False(schema.Declare("ui.font_scale", "", out fileName, out newSegment, out error));
                Assert.Contains("已声明", error);
            }
            finally
            {
                Clean(template);
            }
        }

        /// <summary>模板未注入拒绝——声明通道需要模板路径（不静默只写运行副本）。</summary>
        [Fact]
        public void Declare_RejectsWhenTemplateMissing()
        {
            string sourcePath;
            string template = MakeSchemaPair(out sourcePath);
            try
            {
                ConfigSchema schema = ConfigSchema.Load(sourcePath);
                string fileName;
                bool newSegment;
                string error;
                Assert.False(schema.Declare("probe.enabled", "", out fileName, out newSegment, out error));
                Assert.Contains("模板", error);
            }
            finally
            {
                Clean(template);
            }
        }
    }
}
