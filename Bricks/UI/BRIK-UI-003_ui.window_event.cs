// ═══════════════════════════════════════════════════
// 积木: ui.window_event
// ID:   BRIK-UI-003
// 类别: UI
// 作用: 窗口事件发布——写入 DataBox scope "ui" 的 window 键（open/close/focus），UI 桥轮询消费
// 依赖: 无
// 引用: 无
// 原理: 委托 DataBox.Set——scope "ui" + key "window.{event}"，Pet 主线程发布、UI 桥原子读
// 常用: UiPet 消费按钮指令后发布窗口事件（Chat_UI_Open → window.open）
// ═══════════════════════════════════════════════════
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// UI 积木——ui.window_event 窗口事件发布（DataBox scope "ui"）
    /// </summary>
    public static class UiWindowEventBrick
    {
        /// <summary>
        /// 发布窗口事件——scope "ui" + key "window.{eventName}"（原子覆写）
        /// </summary>
        /// <param name="eventName">事件名（open/close/focus）</param>
        /// <param name="payload">载荷（如 flowId/name JSON 或纯文本）</param>
        /// <returns>true=成功</returns>
        public static bool WindowEvent(string eventName, string payload)
        {
            if (eventName == null || eventName.Length == 0)
            {
                return false;
            }
            if (payload == null)
            {
                payload = "";
            }
            try
            {
                DataBox.Set<string>("ui", "window." + eventName, payload);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
// #MAU_CHECKSUM:SHA256:49C23A97129481F3B364D39050EFD1211E38BFA80769DBDC89625DD5F312EF5E
