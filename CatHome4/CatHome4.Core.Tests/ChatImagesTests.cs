using System;
using System.IO;
using CatHome4.Admin;
using Xunit;

namespace CatHome4.Core.Tests
{
    /// <summary>
    /// 对话图片附件测试（A65 Admin 面纯逻辑）——类型映射 / 文件名安全判定 / 内容寻址命名 / 目录解析。
    /// 端点 IO 面（上传落盘 / 取图）走部署实测（design-ch4-chat-images §九）。
    /// </summary>
    public sealed class ChatImagesTests
    {
        /// <summary>
        /// 类型白名单——命中返回扩展名；未知类型返回空串（入口面零容忍：调用方据此拒绝）。
        /// </summary>
        [Fact]
        public void ImageExtOf_Whitelist_MapsAndRejects()
        {
            Assert.Equal(".png", AdminService.ImageExtOf("image/png"));
            Assert.Equal(".jpg", AdminService.ImageExtOf("image/jpeg"));
            Assert.Equal(".jpg", AdminService.ImageExtOf("image/jpg"));
            Assert.Equal(".webp", AdminService.ImageExtOf("image/webp"));
            Assert.Equal(".gif", AdminService.ImageExtOf("image/gif"));
            Assert.Equal(".bmp", AdminService.ImageExtOf("image/bmp"));
            Assert.Equal("", AdminService.ImageExtOf("image/svg+xml"));
            Assert.Equal("", AdminService.ImageExtOf("text/plain"));
            Assert.Equal("", AdminService.ImageExtOf(""));
        }

        /// <summary>
        /// 扩展名 → MIME——大小写不敏感；未知回落 application/octet-stream。
        /// </summary>
        [Fact]
        public void ImageMimeOfExt_KnownAndUnknown()
        {
            Assert.Equal("image/png", AdminService.ImageMimeOfExt(".png"));
            Assert.Equal("image/jpeg", AdminService.ImageMimeOfExt(".JPG"));
            Assert.Equal("image/webp", AdminService.ImageMimeOfExt(".webp"));
            Assert.Equal("application/octet-stream", AdminService.ImageMimeOfExt(".exe"));
            Assert.Equal("application/octet-stream", AdminService.ImageMimeOfExt(""));
        }

        /// <summary>
        /// 文件名安全判定——取图端点只接受纯文件名（分隔符 / 上级引用 / 空串一律拒绝）。
        /// </summary>
        [Fact]
        public void IsSafeImageName_TraversalAndSeparators_Rejected()
        {
            Assert.True(AdminService.IsSafeImageName("a1b2c3d4e5f60718.png"));
            Assert.False(AdminService.IsSafeImageName(""));
            Assert.False(AdminService.IsSafeImageName("../secret.txt"));
            Assert.False(AdminService.IsSafeImageName("..\\secret.txt"));
            Assert.False(AdminService.IsSafeImageName("sub/a.png"));
            Assert.False(AdminService.IsSafeImageName("sub\\a.png"));
            Assert.False(AdminService.IsSafeImageName(".."));
            Assert.False(AdminService.IsSafeImageName("C:"));
        }

        /// <summary>
        /// 内容寻址命名——同内容同名（去重前提）、异内容异名；16 位十六进制 + 扩展名。
        /// </summary>
        [Fact]
        public void ImageNameOf_ContentAddressed()
        {
            byte[] a = new byte[] { 1, 2, 3, 4 };
            byte[] b = new byte[] { 1, 2, 3, 5 };
            string na = AdminService.ImageNameOf(a, ".png");
            string nb = AdminService.ImageNameOf(a, ".png");
            string nc = AdminService.ImageNameOf(b, ".png");
            Assert.Equal(na, nb);
            Assert.NotEqual(na, nc);
            Assert.Equal(20, na.Length);
            Assert.EndsWith(".png", na);
            Assert.Matches("^[0-9a-f]{16}\\.png$", na);
        }

        /// <summary>
        /// 目录解析——Data 下相对名（换机不改配置）；测试后恢复静态现场。
        /// </summary>
        [Fact]
        public void ImageDirPath_BelowDataRoot()
        {
            string saved = AdminService._dataRoot;
            try
            {
                AdminService._dataRoot = "X";
                Assert.Equal(Path.Combine("X", "Data", "chat-images"), AdminService.ImageDirPath("chat-images"));
            }
            finally
            {
                AdminService._dataRoot = saved;
            }
        }

        /// <summary>
        /// 目录白名单——首项为写入目录且落在白名单内（取图查找面含预留目录）。
        /// </summary>
        [Fact]
        public void ImageDirs_WriteDirIsFirstEntry()
        {
            Assert.True(AdminService.ImageDirs.Length >= 1);
            Assert.Equal(AdminService.ImageWriteDir, AdminService.ImageDirs[0]);
        }
    }
}
