using System;
using System.IO;
using System.Text;
using Mau.Runtime;
using Xunit;

namespace Mau.Runtime.Tests
{
    /// <summary>
    /// 配置 schema 枚举值域测试——values 声明后的写通道校验（合法命中 / 非法拒绝 / 大小写宽容 / 未声明值域放行 / 未声明键拒绝）。
    /// 隔离：临时目录 Guid 唯一命名 + finally 清理。
    /// </summary>
    public sealed class ConfigSchemaValueTests
    {
        /// <summary>写一份临时 schema.json 并装载。</summary>
        /// <param name="itemsJson">items 数组 JSON 片段</param>
        /// <param name="path">临时文件路径（写）</param>
        /// <returns>装载后的 schema</returns>
        private static ConfigSchema LoadTemp(string itemsJson, out string path)
        {
            path = Path.Combine(Path.GetTempPath(), "schema_" + Guid.NewGuid().ToString("N") + ".json");
            string json = "{\"items\":[" + itemsJson + "]}";
            File.WriteAllText(path, json, new UTF8Encoding(false));
            return ConfigSchema.Load(path);
        }

        /// <summary>临时 schema.json 清理（失败不阻断测试结论）。</summary>
        /// <param name="path">文件路径</param>
        private static void Clean(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception)
            {
                // 测试夹具清理——尽力删除（文件被占用等不影响用例结论）
            }
        }

        /// <summary>枚举值域——声明值命中通过。</summary>
        [Fact]
        public void Schema_Values_AcceptsDeclaredValue()
        {
            string path;
            ConfigSchema schema = LoadTemp("{ \"key\": \"llm.cache_isolation\", \"file\": \"llm.cfg\", \"default\": \"cat\", \"writable\": true, \"type\": \"string\", \"values\": [\"cat\", \"session\", \"off\"] }", out path);
            try
            {
                string error;
                Assert.True(schema.Validate("llm.cache_isolation", "cat", out error));
                Assert.True(schema.Validate("llm.cache_isolation", "session", out error));
                Assert.True(schema.Validate("llm.cache_isolation", "off", out error));
                Assert.Equal("", error);
            }
            finally
            {
                Clean(path);
            }
        }

        /// <summary>枚举值域——未声明值拒绝（错误文案带合法值域）。</summary>
        [Fact]
        public void Schema_Values_RejectsUndeclaredValue()
        {
            string path;
            ConfigSchema schema = LoadTemp("{ \"key\": \"llm.cache_isolation\", \"file\": \"llm.cfg\", \"default\": \"cat\", \"writable\": true, \"type\": \"string\", \"values\": [\"cat\", \"session\", \"off\"] }", out path);
            try
            {
                string error;
                Assert.False(schema.Validate("llm.cache_isolation", "bogus", out error));
                Assert.Contains("只接受", error);
            }
            finally
            {
                Clean(path);
            }
        }

        /// <summary>枚举值域——大小写宽容（值原样落盘，消费端归一）。</summary>
        [Fact]
        public void Schema_Values_IgnoresCase()
        {
            string path;
            ConfigSchema schema = LoadTemp("{ \"key\": \"llm.cache_isolation\", \"file\": \"llm.cfg\", \"default\": \"cat\", \"writable\": true, \"type\": \"string\", \"values\": [\"cat\", \"session\", \"off\"] }", out path);
            try
            {
                string error;
                Assert.True(schema.Validate("llm.cache_isolation", "CAT", out error));
            }
            finally
            {
                Clean(path);
            }
        }

        /// <summary>未声明 values 的 string 项——任意值放行（向后兼容）。</summary>
        [Fact]
        public void Schema_StringWithoutValues_AcceptsAny()
        {
            string path;
            ConfigSchema schema = LoadTemp("{ \"key\": \"search.model\", \"file\": \"search.cfg\", \"default\": \"\", \"writable\": true }", out path);
            try
            {
                string error;
                Assert.True(schema.Validate("search.model", "任意模型名", out error));
            }
            finally
            {
                Clean(path);
            }
        }

        /// <summary>未声明键——拒绝（词表外零容忍）。</summary>
        [Fact]
        public void Schema_UndeclaredKey_Rejected()
        {
            string path;
            ConfigSchema schema = LoadTemp("{ \"key\": \"llm.cache_isolation\", \"file\": \"llm.cfg\", \"default\": \"cat\", \"writable\": true, \"type\": \"string\", \"values\": [\"cat\"] }", out path);
            try
            {
                string error;
                Assert.False(schema.Validate("llm.unknown", "cat", out error));
                Assert.Contains("未在 schema 声明", error);
            }
            finally
            {
                Clean(path);
            }
        }
    }
}
