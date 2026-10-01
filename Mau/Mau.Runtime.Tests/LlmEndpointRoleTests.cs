using System;
using Mau.Runtime;
using Xunit;

namespace Mau.Runtime.Tests
{
    /// <summary>
    /// 会话级端点角色测试——主要 / 备用两态：自动故障转移窗口 + 手动对调（仅本会话有效）。
    /// 纯逻辑测试（时间判定以窗口时长构造，不做等待）。
    /// </summary>
    public sealed class LlmEndpointRoleTests
    {
        /// <summary>默认态——主要站（不启用备用）。</summary>
        [Fact]
        public void Role_Default_IsPrimary()
        {
            LlmEndpointRole role = new LlmEndpointRole();
            Assert.False(role.EffectiveUseBackup());
        }

        /// <summary>自动故障转移——窗口内生效，到期回手动态。</summary>
        [Fact]
        public void Role_SetFailover_WindowAppliesThenExpires()
        {
            LlmEndpointRole role = new LlmEndpointRole();
            role.SetFailover(true);
            Assert.True(role.EffectiveUseBackup());
            // 窗口过期（手动态未被改动 = 主要）→ 回主要
            role.FailoverUntilUtc = DateTime.UtcNow.AddSeconds(-1);
            Assert.False(role.EffectiveUseBackup());
        }

        /// <summary>窗口到期回手动态——手动切到备用后，过期的自动窗口不再覆盖手动态。</summary>
        [Fact]
        public void Role_ExpiredWindow_FallsBackToManual()
        {
            LlmEndpointRole role = new LlmEndpointRole();
            Assert.True(role.ToggleManual());
            role.FailoverBackup = false;
            role.FailoverUntilUtc = DateTime.UtcNow.AddSeconds(-1);
            Assert.True(role.EffectiveUseBackup());
        }

        /// <summary>手动对调——翻转当前角色并清自动窗口（用户意志优先）。</summary>
        [Fact]
        public void Role_ToggleManual_SwitchesAndClearsWindow()
        {
            LlmEndpointRole role = new LlmEndpointRole();
            role.SetFailover(true);
            Assert.True(role.EffectiveUseBackup());
            // 对调——窗口内角色为备用 → 手动切成主要；窗口清除后仍为主要
            Assert.False(role.ToggleManual());
            Assert.False(role.EffectiveUseBackup());
            // 再点——切到备用（持久，无窗口）
            Assert.True(role.ToggleManual());
            Assert.True(role.EffectiveUseBackup());
        }

        /// <summary>故障转移窗口时长——180 秒（恒定；与实现常量同源断言）。</summary>
        [Fact]
        public void Role_FailoverSeconds_Is180()
        {
            LlmEndpointRole role = new LlmEndpointRole();
            role.SetFailover(true);
            double seconds = (role.FailoverUntilUtc - DateTime.UtcNow).TotalSeconds;
            Assert.True(seconds > LlmEndpointRole.FailoverSeconds - 5 && seconds <= LlmEndpointRole.FailoverSeconds);
            Assert.Equal(180, LlmEndpointRole.FailoverSeconds);
        }
    }
}
