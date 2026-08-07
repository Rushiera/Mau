// ═══════════════════════════════════════════════════
// 积木: tool.collect
// ID:   BRIK-TOOL-011
// 类别: TOOL
// 作用: 统合工具结果——按 officeIds 顺序读回执，拼装 OpenAI 兼容结果 JSON
// 依赖: 无
// 引用: Mau.Runtime
// 原理: 逐单读回执（call_id/content）→ Utf8JsonWriter 拼装数组
// 常用: 批量工具结果统合
// ═══════════════════════════════════════════════════
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// 工具积木——tool.collect 统合结果（依赖 OaBridge）
    /// </summary>
    public static class ToolCollectBrick
    {
        /// <summary>
        /// 统合工具结果——按 officeIds 顺序读回执，拼装 OpenAI 兼容结果 JSON
        /// </summary>
        /// <param name="officeIds">待统合的 OfficeId 数组</param>
        /// <param name="toolCallsJson">统合结果 JSON 数组</param>
        /// <returns>true=统合成功</returns>
        public static bool Collect(long[] officeIds, out string toolCallsJson)
        {
            toolCallsJson = "";
            IOA? oa;
            DataBox.TryResolve<IOA>(out oa);
            if (oa == null || officeIds == null)
            {
                return false;
            }
            using System.IO.MemoryStream stream = new System.IO.MemoryStream();
            using (System.Text.Json.Utf8JsonWriter writer = new System.Text.Json.Utf8JsonWriter(stream))
            {
                writer.WriteStartArray();
                for (int i = 0; i < officeIds.Length; i = i + 1)
                {
                    Office office = oa.GetOffice(officeIds[i]);
                    string callId = "";
                    string? rawId;
                    if (office.Data.Strs.TryGetValue("call_id", out rawId) && rawId != null)
                    {
                        callId = rawId;
                    }
                    string content = "";
                    string? rawContent;
                    if (office.Result.Strs.TryGetValue("content", out rawContent) && rawContent != null)
                    {
                        content = rawContent;
                    }
                    writer.WriteStartObject();
                    writer.WriteString("call_id", callId);
                    writer.WriteString("content", content);
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
            }
            toolCallsJson = System.Text.Encoding.UTF8.GetString(stream.ToArray());
            return true;
        }
    }
}
// #MAU_CHECKSUM:SHA256:DC74627BCB4A0987BD675632C56AD13A9373E1C9C8B9690FCC0A3D10402965E0
