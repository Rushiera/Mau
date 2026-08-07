// ═══════════════════════════════════════════════════
// 积木: data.box_get_dic
// ID:   BRIK-DATA-006
// 类别: DATA
// 作用: 读取数据包（JSON 文本）——boxId 作用域隔离
// 依赖: 无
// 引用: 无
// 原理: 委托 BoxStore.GetDic（未命中返回 {}）
// 常用: 可序列化暂存读取
// ═══════════════════════════════════════════════════

using Mau.Runtime;
namespace Mau.Bricks
{
    /// <summary>
    /// 数据积木——data.box_get_dic 读取数据包（依赖 BoxStore）
    /// </summary>
    public static class DataBoxGetDicBrick
    {
        /// <summary>
        /// 读取数据包（JSON 文本）
        /// </summary>
        /// <param name="boxId">作用域 ID</param>
        /// <param name="packetKey">包名</param>
        /// <param name="dataJson">JSON 对象文本</param>
        /// <returns>true=成功</returns>
        public static bool GetDic(string boxId, string packetKey, out string dataJson)
        {
            return BoxStore.GetDic(boxId, packetKey, out dataJson);
        }
    }
}
// #MAU_CHECKSUM:SHA256:42167C021D0AA938F5C0FE36CA6024E2F98E76D9E8A5D7AA08339197770C17AC
