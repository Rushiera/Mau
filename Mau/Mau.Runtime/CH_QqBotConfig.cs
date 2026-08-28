using System;

namespace Mau.Runtime
{
    /// <summary>一份不包含 Secret 明文的 QQ Bot 连接配置。</summary>
    public sealed class CH_QqBotConfig
    {
        /// <summary>当前配置结构版本。</summary>
        public int SchemaVersion;

        /// <summary>稳定 QQ Bot 配置身份。</summary>
        public Guid QqBotId;

        /// <summary>用户可见名称。</summary>
        public string DisplayName;

        /// <summary>QQ 开放平台 Bot 应用 ID。</summary>
        public string AppId;

        /// <summary>沙箱标志——true=沙箱端点（sandbox.api.sgroup.qq.com）/ false=正式端点（api.sgroup.qq.com）。</summary>
        public bool Sandbox;

        /// <summary>创建字段完整的空配置。</summary>
        public CH_QqBotConfig()
        {
            SchemaVersion = 1;
            QqBotId = Guid.Empty;
            DisplayName = "";
            AppId = "";
            Sandbox = true;
        }
    }
}
