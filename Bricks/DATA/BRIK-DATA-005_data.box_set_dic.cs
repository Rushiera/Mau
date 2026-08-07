// ═══════════════════════════════════════════════════
// 积木: data.box_set_dic
// ID:   BRIK-DATA-005
// 类别: DATA
// 作用: 写入数据包（JSON 文本）——boxId 作用域隔离
// 依赖: 无
// 引用: 无
// 原理: 委托 BoxStore.SetDic（JSON 对象校验内置）
// 常用: 可序列化暂存 / 结构化数据包
// ═══════════════════════════════════════════════════

using Mau.Runtime;
namespace Mau.Bricks
{
    /// <summary>
    /// 数据积木——data.box_set_dic 写入数据包（依赖 BoxStore）
    /// </summary>
    public static class DataBoxSetDicBrick
    {
        /// <summary>
        /// 写入数据包（JSON 文本）
        /// </summary>
        /// <param name="boxId">作用域 ID</param>
        /// <param name="packetKey">包名</param>
        /// <param name="dataJson">JSON 对象文本</param>
        /// <returns>true=成功</returns>
        public static bool SetDic(string boxId, string packetKey, string dataJson)
        {
            return BoxStore.SetDic(boxId, packetKey, dataJson);
        }
    }
}
// #MAU_CHECKSUM:SHA256:60539DA675D3ACD415C09C2F9589E960B6EF277FD4DCDE7F926A6959B9D10DEA
