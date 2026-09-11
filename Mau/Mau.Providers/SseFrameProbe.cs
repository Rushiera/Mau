using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using Mau.Runtime;

namespace Mau.Providers
{
    /// <summary>
    /// SSE 返回体帧探针——诊断纯函数：把一串 SSE data 载荷归纳为结构摘要（usage 位置 / 字段形态 / 尾部余帧 / 工具调用形态）。
    /// 用途：API 供应商返回体差异排查（如 opencode zen v4.1 后端把 usage 内嵌在 finish 帧）；宿主 CLI --probe-llm 消费。
    /// 安全：摘要只含字段名与数值，不含正文文本（content / reasoning 内容不输出）。
    /// 判定口径：usageLocation = finish-frame（与 finish_reason 同帧）/ usage-only-frame（choices 空数组独立帧）/ both / absent。
    /// </summary>
    public static class SseFrameProbe
    {
        /// <summary>
        /// 归纳帧序列结构——返回 JSON 摘要文本。
        /// </summary>
        /// <param name="frames">SSE data 载荷序列（不含 "data: " 前缀；[DONE] 原样传入）</param>
        /// <param name="httpStatus">HTTP 状态码（0=未知）</param>
        /// <param name="endpoint">端点（诊断定位）</param>
        /// <param name="model">模型名</param>
        /// <returns>摘要 JSON</returns>
        public static string Describe(string[] frames, int httpStatus, string endpoint, string model)
        {
            // [段1] 计数面——帧统计与形态标志
            int frameCount = 0;
            int malformedFrames = 0;
            int trailingAfterDone = 0;
            int usageFrames = 0;
            int nullUsageFrames = 0;
            int emptyChoicesFrames = 0;
            bool doneSeen = false;
            bool finishFrameUsage = false;
            bool usageOnlyFrameUsage = false;
            int finishFrameUsageCount = 0;
            int usageOnlyFrameUsageCount = 0;
            bool reasoningSeen = false;
            bool toolCallsSeen = false;
            long lastPrompt = 0;
            long lastCompletion = 0;
            long lastCacheHit = 0;
            bool usageValueSeen = false;

            // [段2] 集合面——顶层字段/usage 字段/delta 字段/finish 原因（去重保序）
            Dictionary<string, bool> topLevel = new Dictionary<string, bool>(StringComparer.Ordinal);
            Dictionary<string, bool> usageFields = new Dictionary<string, bool>(StringComparer.Ordinal);
            Dictionary<string, bool> deltaFields = new Dictionary<string, bool>(StringComparer.Ordinal);
            Dictionary<string, bool> finishReasons = new Dictionary<string, bool>(StringComparer.Ordinal);

            if (frames != null)
            {
                for (int i = 0; i < frames.Length; i = i + 1)
                {
                    string data = frames[i];
                    if (data == null || data.Length == 0)
                    {
                        continue;
                    }
                    frameCount = frameCount + 1;
                    if (data == "[DONE]")
                    {
                        doneSeen = true;
                        continue;
                    }
                    if (doneSeen)
                    {
                        trailingAfterDone = trailingAfterDone + 1;
                    }
                    // [段2a] 帧解析——畸形帧计数不中断（与运行时容忍口径一致）
                    JsonDocument doc;
                    try
                    {
                        doc = JsonDocument.Parse(data);
                    }
                    catch (JsonException)
                    {
                        malformedFrames = malformedFrames + 1;
                        continue;
                    }
                    using (doc)
                    {
                        JsonElement root = doc.RootElement;
                        if (root.ValueKind != JsonValueKind.Object)
                        {
                            malformedFrames = malformedFrames + 1;
                            continue;
                        }
                        foreach (JsonProperty prop in root.EnumerateObject())
                        {
                            if (!topLevel.ContainsKey(prop.Name))
                            {
                                topLevel.Add(prop.Name, true);
                            }
                        }
                        // [段2b] choices 面——数组长度 / finish_reason / delta 字段族
                        int choicesLength = 0;
                        JsonElement choices;
                        if (root.TryGetProperty("choices", out choices) && choices.ValueKind == JsonValueKind.Array)
                        {
                            choicesLength = choices.GetArrayLength();
                            if (choicesLength == 0)
                            {
                                emptyChoicesFrames = emptyChoicesFrames + 1;
                            }
                            else
                            {
                                JsonElement first = choices[0];
                                if (first.ValueKind == JsonValueKind.Object)
                                {
                                    JsonElement reasonEl;
                                    if (first.TryGetProperty("finish_reason", out reasonEl) && reasonEl.ValueKind == JsonValueKind.String)
                                    {
                                        string? reasonText = reasonEl.GetString();
                                        if (reasonText == null)
                                        {
                                            reasonText = "";
                                        }
                                        if (!finishReasons.ContainsKey(reasonText))
                                        {
                                            finishReasons.Add(reasonText, true);
                                        }
                                    }
                                    JsonElement delta;
                                    if (first.TryGetProperty("delta", out delta) && delta.ValueKind == JsonValueKind.Object)
                                    {
                                        foreach (JsonProperty dprop in delta.EnumerateObject())
                                        {
                                            if (!deltaFields.ContainsKey(dprop.Name))
                                            {
                                                deltaFields.Add(dprop.Name, true);
                                            }
                                            if (dprop.Name == "reasoning_content" && dprop.Value.ValueKind == JsonValueKind.String)
                                            {
                                                string? reasoningText = dprop.Value.GetString();
                                                if (reasoningText != null && reasoningText.Length > 0)
                                                {
                                                    reasoningSeen = true;
                                                }
                                            }
                                            if (dprop.Name == "tool_calls" && dprop.Value.ValueKind == JsonValueKind.Array && dprop.Value.GetArrayLength() > 0)
                                            {
                                                toolCallsSeen = true;
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        // [段2c] usage 面——字段族（含一层嵌套）+ 数值 + 位置形态
                        JsonElement usageEl;
                        if (root.TryGetProperty("usage", out usageEl) && usageEl.ValueKind == JsonValueKind.Object)
                        {
                            usageFrames = usageFrames + 1;
                            foreach (JsonProperty uprop in usageEl.EnumerateObject())
                            {
                                if (!usageFields.ContainsKey(uprop.Name))
                                {
                                    usageFields.Add(uprop.Name, true);
                                }
                                if (uprop.Value.ValueKind == JsonValueKind.Object)
                                {
                                    foreach (JsonProperty nested in uprop.Value.EnumerateObject())
                                    {
                                        string nestedName = uprop.Name + "." + nested.Name;
                                        if (!usageFields.ContainsKey(nestedName))
                                        {
                                            usageFields.Add(nestedName, true);
                                        }
                                    }
                                }
                            }
                            long promptValue = ReadNumber(usageEl, "prompt_tokens");
                            long completionValue = ReadNumber(usageEl, "completion_tokens");
                            long cacheHitValue = ReadNumber(usageEl, "prompt_cache_hit_tokens");
                            if (cacheHitValue == 0)
                            {
                                JsonElement details;
                                if (usageEl.TryGetProperty("prompt_tokens_details", out details) && details.ValueKind == JsonValueKind.Object)
                                {
                                    cacheHitValue = ReadNumber(details, "cached_tokens");
                                }
                            }
                            lastPrompt = promptValue;
                            lastCompletion = completionValue;
                            lastCacheHit = cacheHitValue;
                            usageValueSeen = true;
                            if (choicesLength > 0)
                            {
                                finishFrameUsage = true;
                                finishFrameUsageCount = finishFrameUsageCount + 1;
                            }
                            else
                            {
                                usageOnlyFrameUsage = true;
                                usageOnlyFrameUsageCount = usageOnlyFrameUsageCount + 1;
                            }
                        }
                        else if (root.TryGetProperty("usage", out usageEl) && usageEl.ValueKind == JsonValueKind.Null)
                        {
                            nullUsageFrames = nullUsageFrames + 1;
                        }
                    }
                }
            }

            // [段3] 形态判定与摘要输出
            string usageLocation = "absent";
            if (finishFrameUsage && usageOnlyFrameUsage)
            {
                usageLocation = "both";
            }
            else if (finishFrameUsage)
            {
                usageLocation = "finish-frame";
            }
            else if (usageOnlyFrameUsage)
            {
                usageLocation = "usage-only-frame";
            }
            StringBuilder builder = new StringBuilder();
            builder.Append("{\"httpStatus\":");
            builder.Append(httpStatus.ToString());
            builder.Append(",\"endpoint\":");
            builder.Append(JsonUtil.Serialize(endpoint == null ? "" : endpoint));
            builder.Append(",\"model\":");
            builder.Append(JsonUtil.Serialize(model == null ? "" : model));
            builder.Append(",\"frameCount\":");
            builder.Append(frameCount.ToString());
            builder.Append(",\"malformedFrames\":");
            builder.Append(malformedFrames.ToString());
            builder.Append(",\"doneSeen\":");
            builder.Append(doneSeen ? "true" : "false");
            builder.Append(",\"trailingAfterDone\":");
            builder.Append(trailingAfterDone.ToString());
            builder.Append(",\"usageLocation\":");
            builder.Append(JsonUtil.Serialize(usageLocation));
            builder.Append(",\"usageFrames\":");
            builder.Append(usageFrames.ToString());
            builder.Append(",\"finishFrameUsageCount\":");
            builder.Append(finishFrameUsageCount.ToString());
            builder.Append(",\"usageOnlyFrameCount\":");
            builder.Append(usageOnlyFrameUsageCount.ToString());
            builder.Append(",\"nullUsageFrames\":");
            builder.Append(nullUsageFrames.ToString());
            builder.Append(",\"emptyChoicesFrames\":");
            builder.Append(emptyChoicesFrames.ToString());
            builder.Append(",\"usageValueSeen\":");
            builder.Append(usageValueSeen ? "true" : "false");
            builder.Append(",\"usagePrompt\":");
            builder.Append(lastPrompt.ToString());
            builder.Append(",\"usageCompletion\":");
            builder.Append(lastCompletion.ToString());
            builder.Append(",\"usageCacheHit\":");
            builder.Append(lastCacheHit.ToString());
            builder.Append(",\"reasoningSeen\":");
            builder.Append(reasoningSeen ? "true" : "false");
            builder.Append(",\"toolCallsSeen\":");
            builder.Append(toolCallsSeen ? "true" : "false");
            builder.Append(",\"topLevelFields\":");
            builder.Append(BuildArray(topLevel));
            builder.Append(",\"usageFields\":");
            builder.Append(BuildArray(usageFields));
            builder.Append(",\"deltaFields\":");
            builder.Append(BuildArray(deltaFields));
            builder.Append(",\"finishReasons\":");
            builder.Append(BuildArray(finishReasons));
            builder.Append("}");
            return builder.ToString();
        }

        /// <summary>
        /// 读取对象内数值字段——缺失/非数值返回 0。
        /// </summary>
        /// <param name="obj">父对象</param>
        /// <param name="name">字段名</param>
        /// <returns>数值或 0</returns>
        private static long ReadNumber(JsonElement obj, string name)
        {
            JsonElement value;
            if (!obj.TryGetProperty(name, out value) || value.ValueKind != JsonValueKind.Number)
            {
                return 0;
            }
            long number;
            if (!value.TryGetInt64(out number))
            {
                return 0;
            }
            return number;
        }

        /// <summary>
        /// 键集合转 JSON 字符串数组——去重保序（Dictionary 插入序）。
        /// </summary>
        /// <param name="keys">键集合</param>
        /// <returns>JSON 数组文本</returns>
        private static string BuildArray(Dictionary<string, bool> keys)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append("[");
            bool first = true;
            foreach (KeyValuePair<string, bool> item in keys)
            {
                if (!first)
                {
                    builder.Append(",");
                }
                first = false;
                builder.Append(JsonUtil.Serialize(item.Key));
            }
            builder.Append("]");
            return builder.ToString();
        }
    }
}
