// ═══════════════════════════════════════════════════
// 积木: ui.snapshot_home
// ID:   BRIK-UI-004
// 类别: UI
// 作用: 主页实体列表快照合成——FlowRunner 注册表 + 宿主状态 → HomeSnapshot JSON
// 依赖: 无
// 引用: Mau.Runtime · System.Text
// 原理: FlowRunner.GetStatus() 实体条目（跳 dog）→ Utf8JsonWriter 合成（PascalCase——与 C# 字段默认匹配）；
//       实体状态（active/disabled）从 DataBox scope "host" key "states" 读（宿主 Tick 刷新）
// 常用: UiPet 每帧主页快照合成（Pet-UI 模式——HomeTab 只读渲染）
// 包: 无
// ═══════════════════════════════════════════════════
using System.IO;
using System.Text;

using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// UI 积木——ui.snapshot_home 主页实体列表快照合成（依赖 FlowRunner；Pet-UI 模式）
    /// </summary>
    public static class UiSnapshotHomeBrick
    {
        /// <summary>
        /// 合成 HomeSnapshot JSON——Entities 数组（Id/Name/TypeName/Kind/State，跳过 dog）
        /// </summary>
        /// <param name="homeJson">HomeSnapshot JSON</param>
        /// <returns>true=成功</returns>
        public static bool SnapshotHome(out string homeJson)
        {
            homeJson = "";
            FlowRunner? runner;
            DataBox.TryResolve<FlowRunner>(out runner);
            if (runner == null)
            {
                return false;
            }
            HostSnapshot snap = runner.GetStatus();
            // 实体状态映射——宿主 DataBox scope "host" key "states"（JSON 对象：flowName → state）
            string statesRaw = "";
            DataBox.TryGet<string>("host", "states", out statesRaw);
            using (MemoryStream stream = new MemoryStream())
            {
                using (System.Text.Json.Utf8JsonWriter writer = new System.Text.Json.Utf8JsonWriter(stream))
                {
                    writer.WriteStartObject();
                    writer.WritePropertyName("Entities");
                    writer.WriteStartArray();
                    if (snap.Flows != null)
                    {
                        for (int i = 0; i < snap.Flows.Length; i = i + 1)
                        {
                            FlowEntry flow = snap.Flows[i];
                            if (flow.Kind == "dog")
                            {
                                continue;
                            }
                            // 主页只显示有对话能力的猫猫（Talk 类型）——工具 Cat/Pet 等常驻实体在 Console 快照可见
                            if (!flow.TypeName.Contains("Talk"))
                            {
                                continue;
                            }
                            writer.WriteStartObject();
                            writer.WriteNumber("Id", flow.Id);
                            writer.WriteString("Name", flow.Name);
                            writer.WriteString("TypeName", flow.TypeName);
                            writer.WriteString("Kind", flow.Kind);
                            writer.WriteString("State", GetState(statesRaw, flow.Name));
                            writer.WriteBoolean("Damaged", GetDamaged(statesRaw, flow.Name));
                            writer.WriteEndObject();
                        }
                    }
                    writer.WriteEndArray();
                    writer.WriteEndObject();
                }
                homeJson = Encoding.UTF8.GetString(stream.ToArray());
            }
            return true;
        }

        /// <summary>
        /// 从宿主状态 JSON 查实体损坏标记——值格式 "state|damaged"（damaged=1 损坏）
        /// </summary>
        /// <param name="statesRaw">宿主状态 JSON（对象）</param>
        /// <param name="flowName">实体名</param>
        /// <returns>true=损坏</returns>
        private static bool GetDamaged(string statesRaw, string flowName)
        {
            if (statesRaw == null || statesRaw.Length == 0)
            {
                return false;
            }
            try
            {
                using (System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(statesRaw))
                {
                    System.Text.Json.JsonElement root = doc.RootElement;
                    System.Text.Json.JsonElement value;
                    if (root.ValueKind == System.Text.Json.JsonValueKind.Object
                        && root.TryGetProperty(flowName, out value)
                        && value.ValueKind == System.Text.Json.JsonValueKind.String)
                    {
                        string? raw = value.GetString();
                        if (raw != null)
                        {
                            int bar = raw.IndexOf('|');
                            if (bar >= 0 && bar + 1 < raw.Length)
                            {
                                return raw.Substring(bar + 1) == "1";
                            }
                        }
                    }
                }
            }
            catch
            {
                // 状态 JSON 损坏——默认非损坏
            }
            return false;
        }

        /// <summary>
        /// 从宿主状态 JSON 查实体状态——值格式 "state|damaged"；未收录返回 "active"
        /// </summary>
        /// <param name="statesRaw">宿主状态 JSON（对象）</param>
        /// <param name="flowName">实体名</param>
        /// <returns>状态</returns>
        private static string GetState(string statesRaw, string flowName)
        {
            if (statesRaw == null || statesRaw.Length == 0)
            {
                return "active";
            }
            try
            {
                using (System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(statesRaw))
                {
                    System.Text.Json.JsonElement root = doc.RootElement;
                    System.Text.Json.JsonElement value;
                    if (root.ValueKind == System.Text.Json.JsonValueKind.Object
                        && root.TryGetProperty(flowName, out value)
                        && value.ValueKind == System.Text.Json.JsonValueKind.String)
                    {
                        string? raw = value.GetString();
                        if (raw != null)
                        {
                            int bar = raw.IndexOf('|');
                            if (bar >= 0)
                            {
                                return raw.Substring(0, bar);
                            }
                            return raw;
                        }
                    }
                }
            }
            catch
            {
                // 状态 JSON 损坏——默认 active
            }
            return "active";
        }
    }
}
// #MAU_CHECKSUM:SHA256:C380267F588F339E0094D14A180BDE09BCD99280FAC954977C876382B5A88407
