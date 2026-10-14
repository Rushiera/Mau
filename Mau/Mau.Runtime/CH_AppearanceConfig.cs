using System;

namespace Mau.Runtime
{
    /// <summary>
    /// 一份外观配置条目——「一套外观预设」：桌宠种类 / TTS 风格 / 轮末总结 / 总结成音频 四字段同属一条目。
    /// </summary>
    public sealed class CH_AppearanceConfig
    {
        /// <summary>当前配置结构版本。</summary>
        public int SchemaVersion;

        /// <summary>稳定外观配置身份。</summary>
        public Guid AppearanceId;

        /// <summary>用户可见名称。</summary>
        public string DisplayName;

        /// <summary>桌宠种类——素材名；空=默认素材。</summary>
        public string PetKind;

        /// <summary>TTS 风格槽位——空=无。</summary>
        public string TtsStyle;

        /// <summary>轮末总结开关——true=每轮结束调度总结服务产出总结块。</summary>
        public bool RoundSummary;

        /// <summary>总结成音频开关——true=总结另出音频（依赖 TTS 出口）。</summary>
        public bool SummaryAudio;

        /// <summary>
        /// 默认外观标记——true=未显式指定外观的猫（appearanceId 零值）固定走此配置。
        /// </summary>
        public bool IsDefault;

        /// <summary>创建字段完整的空配置。</summary>
        public CH_AppearanceConfig()
        {
            SchemaVersion = 1;
            AppearanceId = Guid.Empty;
            DisplayName = "";
            PetKind = "";
            TtsStyle = "";
            RoundSummary = false;
            SummaryAudio = false;
            IsDefault = false;
        }
    }
}
