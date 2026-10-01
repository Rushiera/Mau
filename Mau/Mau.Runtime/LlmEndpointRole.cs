using System;

namespace Mau.Runtime
{
    /// <summary>
    /// 会话级 LLM 端点角色状态——主要（默认 / 猫绑定配置）与备用之间的当前选择。
    /// 两态来源：① 自动故障转移（窗口内走切换目标，窗口到期自动回手动态）
    /// ② 手动切换（仅本会话有效——持久到再次点击，不自动回退）。
    /// 角色只决定「本次会话消费池内哪个站」，不改变池自身的默认 / 备用标记。
    /// </summary>
    public sealed class LlmEndpointRole
    {
        /// <summary>故障转移窗口时长（秒）——自动切换后恒定保持，到期自动回手动态。</summary>
        public const int FailoverSeconds = 180;

        /// <summary>手动角色——true=用户点击切到备用（持久；仅本会话有效）。</summary>
        public bool ManualBackup;

        /// <summary>自动故障转移角色——窗口内生效的切换目标（true=备用）。</summary>
        public bool FailoverBackup;

        /// <summary>故障转移窗口截止时刻（UTC）——到期后窗口失效，回手动态。</summary>
        public DateTime FailoverUntilUtc;

        /// <summary>
        /// 生效角色——是否使用备用站。窗口有效期内以窗口角色为准，否则以手动角色为准。
        /// </summary>
        /// <returns>true=本次会话当前走备用站</returns>
        public bool EffectiveUseBackup()
        {
            if (DateTime.UtcNow < FailoverUntilUtc)
            {
                return FailoverBackup;
            }
            return ManualBackup;
        }

        /// <summary>
        /// 登记一次自动故障转移——开窗 + 指定窗口角色（手动角色保留，窗口到期后重新生效）。
        /// </summary>
        /// <param name="useBackup">窗口内是否走备用站</param>
        public void SetFailover(bool useBackup)
        {
            FailoverBackup = useBackup;
            FailoverUntilUtc = DateTime.UtcNow.AddSeconds(FailoverSeconds);
        }

        /// <summary>
        /// 手动切换——对调主要 / 备用（清自动窗口：用户意志优先；仅本会话有效）。
        /// </summary>
        /// <returns>切换后的角色（true=备用）</returns>
        public bool ToggleManual()
        {
            bool current = EffectiveUseBackup();
            FailoverUntilUtc = DateTime.MinValue;
            ManualBackup = !current;
            return ManualBackup;
        }
    }
}
