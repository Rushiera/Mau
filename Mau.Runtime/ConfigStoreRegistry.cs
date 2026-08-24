using System.Collections.Generic;

namespace Mau.Runtime
{
    /// <summary>
    /// 配置存储注册表——多实例按猫路由（P9.4：config.* 工具 per-cat）。
    /// 静态注册面（AuditStore.Default 同模式）：宿主 SetDefault 全局实例 + Register 每猫实例；积木 Resolve 按 catId 路由（空/未注册 → 默认）。
    /// 线程模型：注册写 = 宿主主线程（cat.new/delete）；读 = 积木执行（主线程 Tick 内）——锁保护保险。
    /// </summary>
    public static class ConfigStoreRegistry
    {
        /// <summary>默认全局实例——catId 空/未注册时回退（宿主 Bootstrap SetDefault 注入）</summary>
        private static ConfigStore _default = null!;

        /// <summary>每猫实例表——catId → ConfigStore</summary>
        private static readonly Dictionary<string, ConfigStore> _byCat = new Dictionary<string, ConfigStore>();

        /// <summary>注册表锁</summary>
        private static readonly object _gate = new object();

        /// <summary>
        /// 设置默认全局实例——宿主 Bootstrap 调用（llm.cfg + ui.json 配置群）。
        /// </summary>
        /// <param name="store">全局配置存储</param>
        public static void SetDefault(ConfigStore store)
        {
            lock (_gate)
            {
                _default = store;
            }
        }

        /// <summary>
        /// 注册每猫实例——cat.new / 启动扫描时调用（覆盖已存在同 ID）。
        /// </summary>
        /// <param name="catId">会话 ID</param>
        /// <param name="store">该猫配置存储</param>
        public static void Register(string catId, ConfigStore store)
        {
            if (catId == null || catId.Length == 0 || store == null)
            {
                return;
            }
            lock (_gate)
            {
                _byCat[catId] = store;
            }
        }

        /// <summary>
        /// 注销每猫实例——cat.delete 时调用。
        /// </summary>
        /// <param name="catId">会话 ID</param>
        public static void Unregister(string catId)
        {
            if (catId == null || catId.Length == 0)
            {
                return;
            }
            lock (_gate)
            {
                _byCat.Remove(catId);
            }
        }

        /// <summary>
        /// 按 catId 路由——空/未注册回退默认实例；无默认返回 null（积木侧报 CONFIG_NO_STORE）。
        /// </summary>
        /// <param name="catId">会话 ID（空=默认）</param>
        /// <returns>配置存储实例；未注册且无默认 null</returns>
        public static ConfigStore? Resolve(string catId)
        {
            lock (_gate)
            {
                if (catId != null && catId.Length > 0)
                {
                    ConfigStore? catStore;
                    if (_byCat.TryGetValue(catId, out catStore))
                    {
                        return catStore;
                    }
                }
                return _default;
            }
        }
    }
}
