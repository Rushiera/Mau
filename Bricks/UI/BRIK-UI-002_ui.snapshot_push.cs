// ═══════════════════════════════════════════════════
// 积木: ui.snapshot_push
// ID:   BRIK-UI-002
// 类别: UI
// 作用: 快照原子推送——合成 JSON 写入 DataBox scope "ui"（跨线程原子缓存，UI 线程只读）
// 依赖: 无
// 引用: 无
// 原理: 委托 DataBox.Set——scope "ui" + 指定 key，Pet 主线程写、UI Timer 读（原子 string）
// 常用: UiPet 每帧快照推送（Pet-UI 模式缓存通道）
// ═══════════════════════════════════════════════════
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// UI 积木——ui.snapshot_push 快照原子推送（DataBox scope "ui"）
    /// </summary>
    public static class UiSnapshotPushBrick
    {
        /// <summary>
        /// 推送快照到 DataBox——scope "ui" + key（原子覆写）
        /// </summary>
        /// <param name="key">快照键（如 chat.talkcat-1 / entities）</param>
        /// <param name="json">快照 JSON</param>
        /// <returns>true=成功</returns>
        public static bool SnapshotPush(string key, string json)
        {
            if (key == null || key.Length == 0)
            {
                return false;
            }
            if (json == null)
            {
                json = "";
            }
            try
            {
                DataBox.Set<string>("ui", key, json);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
// #MAU_CHECKSUM:SHA256:2C0BC437546A92D69A6ECCE8EC123C2E178480BF41DB3D030030D21E03A25303
