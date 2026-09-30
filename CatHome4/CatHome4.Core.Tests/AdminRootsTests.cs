using CatHome4.Admin;
using Xunit;

namespace CatHome4.Core.Tests
{
    /// <summary>
    /// 受控根校验测试（A126）——根 id 字符集 + PS 内置驱动器保留名（ps 根寻址失效防护）。
    /// </summary>
    public class AdminRootsTests
    {
        /// <summary>
        /// 合法 id——小写字母/数字组合通过。
        /// </summary>
        [Fact]
        public void IsValidRootIdChars_LowercaseAlnum_True()
        {
            Assert.True(AdminService.IsValidRootIdChars("ccbp"));
            Assert.True(AdminService.IsValidRootIdChars("mau2"));
        }

        /// <summary>
        /// 非法 id——大写/下划线/连字符/空串拒绝（大写由归一化处理，字符集只认小写）。
        /// </summary>
        [Fact]
        public void IsValidRootIdChars_IllegalForms_False()
        {
            Assert.False(AdminService.IsValidRootIdChars("CCBP"));
            Assert.False(AdminService.IsValidRootIdChars("my_root"));
            Assert.False(AdminService.IsValidRootIdChars("my-root"));
            Assert.False(AdminService.IsValidRootIdChars(""));
        }

        /// <summary>
        /// PS 保留名——固定名拒绝（大小写不敏感）；名单来源 = Get-PSDrive 实测并集（5.1 + 7）。
        /// </summary>
        [Fact]
        public void IsReservedPsDriveName_FixedNames_True()
        {
            Assert.True(AdminService.IsReservedPsDriveName("env"));
            Assert.True(AdminService.IsReservedPsDriveName("Env"));
            Assert.True(AdminService.IsReservedPsDriveName("HKLM"));
            Assert.True(AdminService.IsReservedPsDriveName("temp"));
            Assert.True(AdminService.IsReservedPsDriveName("wsman"));
        }

        /// <summary>
        /// PS 保留名——单字母盘符拒绝（A-Z 全覆盖；归一后小写判定）。
        /// </summary>
        [Fact]
        public void IsReservedPsDriveName_SingleLetter_True()
        {
            Assert.True(AdminService.IsReservedPsDriveName("c"));
            Assert.True(AdminService.IsReservedPsDriveName("D"));
        }

        /// <summary>
        /// 普通根 id 放行——现有固定根与业务根不受影响；多字符含数字不误判为盘符。
        /// </summary>
        [Fact]
        public void IsReservedPsDriveName_NormalIds_False()
        {
            Assert.False(AdminService.IsReservedPsDriveName("ccbp"));
            Assert.False(AdminService.IsReservedPsDriveName("mau"));
            Assert.False(AdminService.IsReservedPsDriveName("mauout"));
            Assert.False(AdminService.IsReservedPsDriveName("workspace"));
            Assert.False(AdminService.IsReservedPsDriveName("c2"));
            Assert.False(AdminService.IsReservedPsDriveName("gitee"));
        }
    }
}
