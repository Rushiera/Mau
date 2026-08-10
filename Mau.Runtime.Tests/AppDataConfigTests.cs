using System;
using System.IO;
using Xunit;

namespace Mau.Runtime.Tests
{
    /// <summary>
    /// AppDataConfig 持久化测试——LLM 档案 + 密钥的用户级配置介质（%LOCALAPPDATA%/Mau_wls/CatHome4/）。
    /// 覆盖：读写落盘 / CredentialStore 往返 / LlmBridge 档案重启恢复（ConfigureProfileStore null=AppData 默认）。
    /// 隔离：ConfigureRoot 注入临时目录——不影响真实 AppData；AuditSerial 串行——LlmBridge 静态状态
    /// </summary>
    [Collection("AuditSerial")]
    public sealed class AppDataConfigTests
    {
        /// <summary>
        /// 临时根目录——每测试独立
        /// </summary>
        private readonly string _root = Path.Combine(Path.GetTempPath(),
            "mauappdata_" + Guid.NewGuid().ToString("N").Substring(0, 8));

        /// <summary>
        /// 夹具——注入临时根 + 隔离静态状态
        /// </summary>
        public AppDataConfigTests()
        {
            AppDataConfig.ConfigureRoot(_root);
            CredentialStore.ClearAll();
            LlmBridge.ConfigureProfileStore(null);
        }

        /// <summary>
        /// 配置读写落盘——Set 原子写 + 重新加载恢复（模拟重启）
        /// </summary>
        [Fact]
        public void AppDataConfig_SetAndReload_Persists()
        {
            AppDataConfig.Set("profiles", "[{\"ProfileId\":\"p1\",\"DisplayName\":\"档案一\"}]");
            AppDataConfig.Set("active", "p1");
            Assert.True(File.Exists(AppDataConfig.ConfigPath), "llm.cfg 应已落盘");

            // 模拟重启——重新 ConfigureRoot（强制重载）
            AppDataConfig.ConfigureRoot(_root);
            Assert.Equal("[{\"ProfileId\":\"p1\",\"DisplayName\":\"档案一\"}]", AppDataConfig.Get("profiles", ""));
            Assert.Equal("p1", AppDataConfig.Get("active", ""));
        }

        /// <summary>
        /// CredentialStore 持久化往返——SavePersisted 落盘 → ClearAll + LoadPersisted 恢复
        /// </summary>
        [Fact]
        public void CredentialStore_SaveAndLoadPersisted_Roundtrip()
        {
            CredentialStore.Set("llm.apiKey.p1", "secret-1");
            CredentialStore.Set("llm.apiKey", "legacy-key");
            CredentialStore.SavePersisted();

            CredentialStore.ClearAll();
            Assert.Equal("", CredentialStore.Get("llm.apiKey.p1"));

            CredentialStore.LoadPersisted();
            Assert.Equal("secret-1", CredentialStore.Get("llm.apiKey.p1"));
            Assert.Equal("legacy-key", CredentialStore.Get("llm.apiKey"));
        }

        /// <summary>
        /// LlmBridge 档案经 AppDataConfig 持久化——SaveProfile → 重新绑定 → 档案恢复 + 密钥随档案
        /// </summary>
        [Fact]
        public void LlmBridge_ProfilePersistsViaAppData()
        {
            // [段1] 保存档案 + 密钥
            LlmProfile profile = new LlmProfile();
            profile.ProfileId = "";
            profile.DisplayName = "AppData 档案";
            profile.ApiType = "deepseek";
            profile.Endpoint = "https://api.deepseek.com/v1/chat/completions";
            profile.Model = "deepseek-chat";
            string id = LlmBridge.SaveProfile(profile);
            Assert.True(id.Length > 0, "新建档案应生成 Id");
            LlmBridge.SetProfileSecret(id, "appdata-secret");

            // [段2] 模拟重启——清内存态 + 重新绑定 AppDataConfig
            LlmBridge.ConfigureProfileStore(null);
            LlmProfile[] profiles = LlmBridge.GetAllProfiles();
            Assert.Single(profiles);
            Assert.Equal("AppData 档案", profiles[0].DisplayName);
            Assert.Equal("appdata-secret", LlmBridge.GetProfileSecret(id));
        }
    }
}
