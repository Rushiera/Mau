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

        /// <summary>
        /// 固定根读面归一——磁盘旧 note / writable 不回流管理面（存量 workspace.json 旧值曾致保存死锁）。
        /// </summary>
        [Fact]
        public void BuildRootJson_FixedRoot_UsesSystemDefinition()
        {
            Mau.Runtime.WorkspaceConfig.RootEntry entry = new Mau.Runtime.WorkspaceConfig.RootEntry();
            entry.Id = "workspace";
            entry.Path = "D:/Mau/WorkSpace";
            entry.Writable = false;
            entry.Note = "默认WorkSpace";
            object json = AdminService.BuildRootJson(entry);
            Assert.Equal("系统工作区（必选）", (string)json.GetType().GetProperty("note").GetValue(json));
            Assert.True((bool)json.GetType().GetProperty("writable").GetValue(json));
            Assert.True((bool)json.GetType().GetProperty("fixedRoot").GetValue(json));
        }

        /// <summary>
        /// 非固定根读面透传——磁盘值原样渲染（归一只作用于固定命名根）。
        /// </summary>
        [Fact]
        public void BuildRootJson_BusinessRoot_PassesThrough()
        {
            Mau.Runtime.WorkspaceConfig.RootEntry entry = new Mau.Runtime.WorkspaceConfig.RootEntry();
            entry.Id = "ccbp";
            entry.Path = "D:/Mau/CatCatBigParty";
            entry.Writable = true;
            entry.Note = "CCBP知识网络";
            object json = AdminService.BuildRootJson(entry);
            Assert.Equal("CCBP知识网络", (string)json.GetType().GetProperty("note").GetValue(json));
            Assert.True((bool)json.GetType().GetProperty("writable").GetValue(json));
            Assert.False((bool)json.GetType().GetProperty("fixedRoot").GetValue(json));
        }
    }
}
