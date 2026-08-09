// ═══════════════════════════════════════════════════
// 积木: ui.config_set
// ID:   BRIK-UI-010
// 类别: UI
// 作用: 写配置——flat 载荷（catName|key|value）按猫 ConfigStore（DataBox "catcfg" scope），"__global__"=全局窗口默认
// 依赖: 无
// 引用: Mau.Runtime
// 原理: flat.Split('|') 三段 → DataBox.TryGet<ConfigStore>("catcfg", catName) → Set + Save（原子写落盘）
// 常用: HomeTab 保存会话配置 → Chat_UI_SaveConfig → UiPet → 本积木
// 包: 无
// ═══════════════════════════════════════════════════
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// UI 积木——ui.config_set 写配置（依赖宿主 Bind 的 ConfigStore 服务）
    /// </summary>
    public static class UiConfigSetBrick
    {
        /// <summary>
        /// 写配置——flat 载荷 "catName|key|value"；catName="__global__"=全局窗口默认
        /// </summary>
        /// <param name="flat">三段式载荷（| 分隔）</param>
        /// <returns>true=成功</returns>
        public static bool ConfigSet(string flat)
        {
            if (flat == null)
            {
                return false;
            }
            string[] parts = flat.Split('|');
            if (parts.Length < 3)
            {
                return false;
            }
            string catName = parts[0];
            string key = parts[1];
            string value = parts[2];
            if (catName.Length == 0 || key.Length == 0)
            {
                return false;
            }
            ConfigStore store;
            DataBox.TryGet<ConfigStore>("catcfg", catName, out store);
            if (store == null)
            {
                return false;
            }
            store.Set(key, value);
            store.Save();
            return true;
        }
    }
}
// #MAU_CHECKSUM:SHA256:03A930B1FEC09ED4CF2CCE4DB90D1BE6822CF2D92D6B03DC7C50AB3BE7F971AA
