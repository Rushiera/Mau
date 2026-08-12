// ═══════════════════════════════════════════════════
// 积木: llm.ctx_push_reasoning
// ID:   BRIK-LLM-028
// 类别: LLM
// 作用: 思考合并 push——从活跃会话推理累积读完整思考，按 thinkMode 转换（brief/partial/full）追加 System 消息
// 依赖: 无
// 包: 无
// 引用: Mau.Runtime
// 原理: LlmStreamSession.ReasoningBuilder（ParseStreamEvent 累积——G.2 思考显示 2026-08-11）
//       → 按 cat.cfg thinkMode 转换（CH2 BuildThinkDisplay 移植：brief=字符数/partial=折叠/full=全文）
//       → ContextStore 追加一条 System 消息（[思考] 前缀——UI System 条目）
//       配置读取：DataBox "catcfg" scope（宿主 GetOrCreateCatConfig 同步 Bind——与 cat.tools_json 同源）
// 常用: TalkCat 语料 T_PushReasoning——G.2 思考显示（完成链/工具分支链：llm.finish 前调用——会话移除后累积丢失）
// ═══════════════════════════════════════════════════
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// LLM 积木——llm.ctx_push_reasoning 思考合并 push（依赖 LlmSession/ContextStore/DataBox；G.2 思考显示 2026-08-11）
    /// </summary>
    public static class CtxPushReasoningBrick
    {
        /// <summary>
        /// 从会话推理累积读完整思考并追加 System 消息——按猫 thinkMode 配置转换显示形态
        /// </summary>
        /// <param name="requestId">流式会话 ID（必须在 llm.finish 之前调用——会话移除后累积丢失）</param>
        /// <param name="sessionKey">会话 Key（兼猫名——DataBox "catcfg" scope 读 thinkMode 配置）</param>
        /// <returns>true=成功（会话不存在或思考为空也返回 true——push 跳过不阻塞流程）</returns>
        public static bool CtxPushReasoning(string requestId, string sessionKey)
        {
            LlmStreamSession? session = LlmSession.FindSession(requestId);
            if (session == null)
            {
                // 会话已被 finish 移除——累积丢失，push 跳过（与 ctx_push_stream 同容错策略）
                return true;
            }
            string fullThink = session.ReasoningBuilder.ToString();
            if (fullThink.Length == 0)
            {
                return true;
            }
            // [段1] 读猫 thinkMode 配置——DataBox "catcfg"（缺省 brief——CH2 LoadThinkMode 默认）
            string thinkMode = "brief";
            ConfigStore? store;
            DataBox.TryGet<ConfigStore>("catcfg", ContextStore.SafeKey(sessionKey), out store);
            if (store != null)
            {
                string mode = store.Get("thinkMode", "brief");
                if (mode == "brief" || mode == "partial" || mode == "full")
                {
                    thinkMode = mode;
                }
            }
            // [段2] 按 thinkMode 转换显示文本（CH2 BuildThinkDisplay 移植）
            string display;
            if (thinkMode == "brief")
            {
                display = "[思考] 已思考 " + fullThink.Length + " 字符";
            }
            else if (thinkMode == "partial")
            {
                string[] lines = fullThink.Replace("\r\n", "\n").Split('\n');
                if (lines.Length <= 5)
                {
                    display = "[思考]" + fullThink;
                }
                else
                {
                    System.Text.StringBuilder sb = new System.Text.StringBuilder();
                    sb.Append("[思考]");
                    sb.Append(lines[0]);
                    sb.Append(lines[1]);
                    int foldedLines = lines.Length - 4;
                    int foldedChars = 0;
                    for (int i = 2; i < lines.Length - 2; i = i + 1)
                    {
                        foldedChars = foldedChars + lines[i].Length;
                    }
                    sb.Append("（已折叠的思考内容，共 " + foldedLines + " 行，" + foldedChars + " 字符。）");
                    sb.Append(lines[lines.Length - 2]);
                    sb.Append(lines[lines.Length - 1]);
                    display = sb.ToString();
                }
            }
            else
            {
                display = "[思考]" + fullThink;
            }
            // [段3] 追加 System 消息——思考入持久流（UI 显示 + LLM 前文完整）
            ContextSession ctx = ContextStore.GetOrCreate(ContextStore.SafeKey(sessionKey));
            lock (ContextStore.Gate)
            {
                ctx.History.Add(ContextStore.CreateMessage("System", display));
            }
            return true;
        }
    }
}
// #MAU_CHECKSUM:SHA256:5235F71A3AEC276D900639899C94501B69398390476B31812E106156E5962EF4
