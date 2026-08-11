using System;
using System.IO;
using Xunit;

namespace Mau.Runtime.Tests
{
    /// <summary>
    /// 生命周期契约测试（D29）——程序级静态状态跨宿主/跨测试污染回归防线。
    /// 覆盖：LlmBridge 绑定先清空（空配置/损坏配置）/Reset 契约 / CredentialStore 先清后载 / 宿主 A→B→C 切换。
    /// 隔离：AuditSerial 串行——LlmBridge/CredentialStore/AppDataConfig/AuditStore 静态状态。
    /// </summary>
    [Collection("AuditSerial")]
    public sealed class LifecycleTests
    {
        /// <summary>
        /// 临时根目录——每测试独立
        /// </summary>
        private readonly string _root = Path.Combine(Path.GetTempPath(),
            "maulife_" + Guid.NewGuid().ToString("N").Substring(0, 8));

        /// <summary>
        /// 夹具——隔离全部程序级静态状态
        /// </summary>
        public LifecycleTests()
        {
            AppDataConfig.Reset();
            CredentialStore.Reset();
            LlmBridge.Reset();
            AuditStore.Reset();
            DataBox.Reset();
            AppDataConfig.ConfigureRoot(_root);
        }

        /// <summary>
        /// 构造带档案的 ConfigStore——宿主 A 场景
        /// </summary>
        private static ConfigStore BuildStoreWithProfiles()
        {
            ConfigStore cfg = new ConfigStore();
            cfg.Set("profiles", "[{\"ProfileId\":\"pA\",\"DisplayName\":\"档案A\"}]");
            cfg.Set("active", "pA");
            return cfg;
        }

        /// <summary>
        /// LlmBridge 绑定空存储——旧档案必须清空（D27：空配置也必须清空；P0-1 实锤"期望 0 实际 1"）
        /// </summary>
        [Fact]
        public void LlmBridge_ConfigureEmptyStore_ClearsOldProfiles()
        {
            // [段1] 宿主 A——绑定带档案的存储
            LlmBridge.ConfigureProfileStore(BuildStoreWithProfiles());
            Assert.Single(LlmBridge.GetAllProfiles());
            Assert.Equal("pA", LlmBridge.ActiveProfileId);

            // [段2] 宿主 B——绑定空存储（无 profiles 键）
            ConfigStore empty = new ConfigStore();
            LlmBridge.ConfigureProfileStore(empty);

            Assert.Empty(LlmBridge.GetAllProfiles());
            Assert.Equal("", LlmBridge.ActiveProfileId);
        }

        /// <summary>
        /// LlmBridge 绑定损坏配置——保持确定空状态（D27：加载失败保持空）
        /// </summary>
        [Fact]
        public void LlmBridge_ConfigureCorruptStore_KeepsEmpty()
        {
            LlmBridge.ConfigureProfileStore(BuildStoreWithProfiles());
            Assert.Single(LlmBridge.GetAllProfiles());

            ConfigStore corrupt = new ConfigStore();
            corrupt.Set("profiles", "{not-json!!");
            corrupt.Set("active", "pX");
            LlmBridge.ConfigureProfileStore(corrupt);

            Assert.Empty(LlmBridge.GetAllProfiles());
            Assert.Equal("", LlmBridge.ActiveProfileId);
        }

        /// <summary>
        /// LlmBridge.Reset——清空档案/生效 Id/存储绑定（D26）
        /// </summary>
        [Fact]
        public void LlmBridge_Reset_ClearsAllState()
        {
            ConfigStore cfg = new ConfigStore();
            LlmBridge.ConfigureProfileStore(cfg);
            LlmProfile p = new LlmProfile();
            p.DisplayName = "重置测试";
            string id = LlmBridge.SaveProfile(p);
            LlmBridge.SetActiveProfile(id);
            Assert.Single(LlmBridge.GetAllProfiles());

            LlmBridge.Reset();

            Assert.Empty(LlmBridge.GetAllProfiles());
            Assert.Equal("", LlmBridge.ActiveProfileId);
        }

        /// <summary>
        /// CredentialStore.LoadPersisted 先清后载——介质中已删除的凭证不残留内存（D27 延伸）
        /// </summary>
        [Fact]
        public void CredentialStore_LoadPersisted_ClearsStaleKeys()
        {
            // 旧凭证入内存（模拟宿主 A 遗留）
            CredentialStore.Set("llm.apiKey.pA", "stale-secret");
            CredentialStore.SavePersisted();

            // 模拟重启——根目录重置（介质变空）+ 重新加载
            AppDataConfig.ConfigureRoot(Path.Combine(_root, "empty"));
            CredentialStore.LoadPersisted();

            Assert.Equal("", CredentialStore.Get("llm.apiKey.pA"));
            Assert.False(CredentialStore.Has("llm.apiKey.pA"));
        }

        /// <summary>
        /// 宿主 A → 空宿主 B → 宿主 C 三连切换——每步状态干净（D30 多宿主契约）
        /// </summary>
        [Fact]
        public void HostSwitch_ABackToBack_NoCrossHostPollution()
        {
            // 宿主 A——带档案
            LlmBridge.ConfigureProfileStore(BuildStoreWithProfiles());
            Assert.Single(LlmBridge.GetAllProfiles());

            // 宿主 B——空存储
            ConfigStore emptyB = new ConfigStore();
            LlmBridge.ConfigureProfileStore(emptyB);
            Assert.Empty(LlmBridge.GetAllProfiles());

            // 宿主 C——再次带档案（档案不串）
            ConfigStore cfgC = new ConfigStore();
            cfgC.Set("profiles", "[{\"ProfileId\":\"pC\",\"DisplayName\":\"档案C\"}]");
            cfgC.Set("active", "pC");
            LlmBridge.ConfigureProfileStore(cfgC);
            LlmProfile[] profiles = LlmBridge.GetAllProfiles();
            Assert.Single(profiles);
            Assert.Equal("pC", profiles[0].ProfileId);
            Assert.Equal("pC", LlmBridge.ActiveProfileId);
        }

        /// <summary>
        /// Reset 后重新绑定——存储绑定回到未配置（null），SaveProfile 不落盘不崩溃
        /// </summary>
        [Fact]
        public void LlmBridge_ResetThenSave_WorksWithoutStore()
        {
            LlmBridge.Reset();

            LlmProfile p = new LlmProfile();
            p.DisplayName = "无存储档案";
            string id = LlmBridge.SaveProfile(p);
            Assert.True(id.Length > 0);
            Assert.Single(LlmBridge.GetAllProfiles());

            // 重新绑定后——内存态按新存储重建（旧档案被清）
            ConfigStore cfg = new ConfigStore();
            LlmBridge.ConfigureProfileStore(cfg);
            Assert.Empty(LlmBridge.GetAllProfiles());
        }
    }
}
