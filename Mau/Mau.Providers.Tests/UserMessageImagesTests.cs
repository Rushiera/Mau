using System;
using System.IO;
using System.Text.Json;
using Mau.Runtime;
using Xunit;

namespace Mau.Providers.Tests
{
    /// <summary>
    /// user 消息图片附件测试（design-ch4-chat-images §8.2 / §8.4）——
    /// 内容块数组形态 / 失效引用可见（不阻断请求）/ 图片载荷构建（本地 base64 · 缺失 · URL 直通 · 超长）。
    /// </summary>
    public sealed class UserMessageImagesTests
    {
        /// <summary>
        /// 建临时图片文件——内容为任意字节（载荷构建只做 base64，不校验图片结构）。
        /// </summary>
        /// <param name="extension">扩展名（含点）</param>
        /// <returns>文件绝对路径（正斜杠形态）</returns>
        private static string CreateTempImage(string extension)
        {
            string path = Path.Combine(Path.GetTempPath(), "mauimg_" + Guid.NewGuid().ToString("N") + extension);
            File.WriteAllBytes(path, new byte[] { 1, 2, 3, 4 });
            return path.Replace("\\", "/");
        }

        /// <summary>
        /// 构造带附件的 user 消息。
        /// </summary>
        /// <param name="text">正文</param>
        /// <param name="imagesJson">附件引用 JSON 数组</param>
        /// <returns>消息</returns>
        private static LlmMessage UserMessage(string text, string imagesJson)
        {
            LlmMessage m = new LlmMessage();
            m.Role = LlmRole.User;
            m.Content = text;
            m.ImagesJson = imagesJson;
            return m;
        }

        /// <summary>
        /// 带有效附件——wire content 为内容块数组（text 块 + image_url 块，data URL 内联）。
        /// </summary>
        [Fact]
        public void BuildUserMessage_WithValidImage_ProducesContentParts()
        {
            string path = CreateTempImage(".png");
            try
            {
                object wire = DeepSeekLlmRuntime.BuildUserMessageWithImages(UserMessage("看图", "[\"" + path + "\"]"));
                string json = JsonSerializer.Serialize(wire);
                Assert.Contains("\"type\":\"text\"", json, StringComparison.Ordinal);
                Assert.Contains("\"image_url\"", json, StringComparison.Ordinal);
                Assert.Contains("data:image/png;base64,", json, StringComparison.Ordinal);
            }
            finally
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }

        /// <summary>
        /// 失效引用——不阻断请求：无 image_url 块，正文并入失败信息（失败必须可见）。
        /// </summary>
        [Fact]
        public void BuildUserMessage_WithMissingImage_ReportsInText()
        {
            string path = Path.Combine(Path.GetTempPath(), "mauimg_missing_" + Guid.NewGuid().ToString("N") + ".png").Replace("\\", "/");
            object wire = DeepSeekLlmRuntime.BuildUserMessageWithImages(UserMessage("看图", "[\"" + path + "\"]"));
            string json = JsonSerializer.Serialize(wire);
            Assert.Contains("IMAGE_NOT_FOUND", json, StringComparison.Ordinal);
            Assert.DoesNotContain("\"image_url\"", json, StringComparison.Ordinal);
            // 中文断言经解析（System.Text.Json 默认 Unicode 转义——判例：直接断言原文必失败）
            using (JsonDocument doc = JsonDocument.Parse(json))
            {
                string text = doc.RootElement.GetProperty("content")[0].GetProperty("text").GetString() ?? "";
                Assert.Contains("图片引用失效", text, StringComparison.Ordinal);
            }
        }

        /// <summary>
        /// 引用数组畸形——不抛异常；正文兜底可读（解析失败可见）。
        /// </summary>
        [Fact]
        public void BuildUserMessage_WithMalformedRefs_DoesNotThrow()
        {
            object wire = DeepSeekLlmRuntime.BuildUserMessageWithImages(UserMessage("看图", "{not-json"));
            string json = JsonSerializer.Serialize(wire);
            Assert.Contains("IMAGE_REF_PARSE", json, StringComparison.Ordinal);
        }

        /// <summary>
        /// 载荷构建——本地文件 → data URL（media type 按扩展名）。
        /// </summary>
        [Fact]
        public void Resolve_LocalFile_ReturnsDataUrl()
        {
            string path = CreateTempImage(".jpg");
            try
            {
                string error = "";
                string url = ImagePayload.Resolve(path, out error);
                Assert.Equal("", error);
                Assert.StartsWith("data:image/jpeg;base64,", url, StringComparison.Ordinal);
            }
            finally
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }

        /// <summary>
        /// 载荷构建——文件不存在 → ERR|IMAGE_NOT_FOUND。
        /// </summary>
        [Fact]
        public void Resolve_MissingFile_ReturnsError()
        {
            string error = "";
            string url = ImagePayload.Resolve("C:/definitely/not/here.png", out error);
            Assert.Equal("", url);
            Assert.StartsWith("ERR|IMAGE_NOT_FOUND", error, StringComparison.Ordinal);
        }

        /// <summary>
        /// 载荷构建——http(s) URL 直通（长度合规）。
        /// </summary>
        [Fact]
        public void Resolve_HttpUrl_PassesThrough()
        {
            string error = "";
            string url = ImagePayload.Resolve("https://example.com/a.png", out error);
            Assert.Equal("", error);
            Assert.Equal("https://example.com/a.png", url);
        }

        /// <summary>
        /// 载荷构建——超长 URL → ERR|IMAGE_URL_TOO_LONG。
        /// </summary>
        [Fact]
        public void Resolve_TooLongUrl_ReturnsError()
        {
            string error = "";
            string longUrl = "https://example.com/" + new string('a', 9000) + ".png";
            string url = ImagePayload.Resolve(longUrl, out error);
            Assert.Equal("", url);
            Assert.StartsWith("ERR|IMAGE_URL_TOO_LONG", error, StringComparison.Ordinal);
        }
    }
}
