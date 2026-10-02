using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using Mau.Runtime;

namespace CH4
{
    /// <summary>
    /// 宿主会话实体——Note 任务追踪分部（M4a 会话内直执工具——CH2 语义移植）。
    /// 内存态不落盘，会话关闭即消失；前端面板经 SSE note 事件实时重绘。
    /// </summary>
    internal sealed partial class ChatSession
    {
        // [段1] Note 任务追踪三字段
        /// <summary>Note 任务列表——set 写入的任务数组（\n 分割）</summary>
        private string[] _noteTasks;

        /// <summary>Note 当前任务索引——0-based</summary>
        private int _noteCurrent;

        /// <summary>Note 已完成任务数</summary>
        private int _noteDone;

        /// <summary>Note 刚全部完成标志——完成态展示（前端"🎉 全部完成"；新计划/新增/回滚清除——Q7 2026-09-08）</summary>
        private bool _noteJustCompleted;
        /// <summary>会话视图存储——F4 视图持久化（内存整块 + 文件落盘；真实前文派生态）</summary>
        private readonly SessionViewStore _viewStore;
        /// <summary>思考流式累积——reasoning_content 增量实时拼接（思考段终结 SealReasonStream 取值；LaunchLlm 清空）</summary>
        private readonly StringBuilder _reasonAccum = new StringBuilder();
        /// <summary>
        /// Note 工具执行体——M4a（CH2 语义移植：set 写入/无参推进/全完成清空；返回文本 = LLM 唯一状态面）。
        /// </summary>
        /// <param name="argsJson">参数 JSON（action/content/force）</param>
        /// <returns>进度反馈文本</returns>
        private string ExecuteNote(string argsJson)
        {
            string action = "";
            string[] contentItems = null;
            string contentError = "";
            bool force = false;
            if (argsJson != null && argsJson.Length > 0 && argsJson.StartsWith("{"))
            {
                try
                {
                    using (JsonDocument doc = JsonUtil.ParseStrict(argsJson))
                    {
                        JsonElement root = doc.RootElement;
                        if (root.TryGetProperty("action", out JsonElement a) && a.ValueKind == JsonValueKind.String)
                        {
                            action = a.GetString() ?? "";
                        }
                        if (root.TryGetProperty("content", out JsonElement c))
                        {
                            if (c.ValueKind != JsonValueKind.Array)
                            {
                                contentError = "content 须为字符串数组（原生 JSON array）——收到 " + c.ValueKind.ToString();
                            }
                            else if (c.GetArrayLength() == 0)
                            {
                                contentError = "content 为空数组——计划至少一条";
                            }
                            else
                            {
                                List<string> items = new List<string>();
                                int itemIndex = 0;
                                foreach (JsonElement item in c.EnumerateArray())
                                {
                                    itemIndex = itemIndex + 1;
                                    if (item.ValueKind != JsonValueKind.String)
                                    {
                                        contentError = "content 第 " + itemIndex + " 项须为字符串——收到 " + item.ValueKind.ToString();
                                        break;
                                    }
                                    string one = (item.GetString() ?? "").Trim();
                                    if (one.Length == 0)
                                    {
                                        contentError = "content 第 " + itemIndex + " 项为空——条目不得为空串";
                                        break;
                                    }
                                    items.Add(one);
                                }
                                if (contentError.Length == 0)
                                {
                                    contentItems = items.ToArray();
                                }
                            }
                        }
                        if (root.TryGetProperty("force", out JsonElement f) && f.ValueKind == JsonValueKind.True)
                        {
                            force = true;
                        }
                    }
                }
                catch (Exception ex)
                {
                    // 参数 JSON 损坏——按无参数推进语义处理（CH2 同款容错）
                    LogStore.Add("ChatSession", 2, "Note 参数 JSON 损坏，按无参数推进: " + ex.Message, "SYS");
                }
            }
            // [段1] 形态校验（A129——多值参数一律原生数组；非数组 / 非字符串元素 / 空串元素一律出声，退役静默回落与静默截断）
            if (contentError.Length > 0)
            {
                LogStore.Add("ChatSession", 2, "Note 参数形态非法——" + contentError, "SYS");
                return "ERR|BAD_ARGS|Note content 形态非法——" + contentError + "；正确形态 content=[\"任务1\",\"任务2\"]";
            }
            string result;
            // [段2] set——写入新计划（A129：原生数组，元素已 Trim；未完成时无 force 拒绝覆盖）
            if (action == "set" && contentItems != null && contentItems.Length > 0)
            {
                int oldRemain = 0;
                if (_noteTasks != null && _noteTasks.Length > 0 && _noteCurrent < _noteTasks.Length)
                {
                    oldRemain = _noteTasks.Length - _noteCurrent;
                }
                if (oldRemain > 0 && !force)
                {
                    result = "[Note] ⚠️ 还有 " + oldRemain + " 条未完成。用 force=true 强制覆盖，或用 Note() 继续推进。";
                }
                else
                {
                    _noteTasks = contentItems;
                    _noteCurrent = 0;
                    _noteDone = 0;
                    _noteJustCompleted = false;
                    result = BuildNoteProgress();
                }
            }
            else
            {
                // [段3] 无参数——推进一条
                if (_noteTasks != null && _noteTasks.Length > 0 && _noteCurrent < _noteTasks.Length)
                {
                    _noteDone = _noteDone + 1;
                    _noteCurrent = _noteCurrent + 1;
                }
                // [段4] 全部完成——清空内存
                if (_noteTasks != null && _noteCurrent >= _noteTasks.Length)
                {
                    int total = _noteTasks.Length;
                    _noteTasks = null;
                    _noteCurrent = 0;
                    _noteDone = 0;
                    _noteJustCompleted = true;
                    // 结构化返回体（design-ch4-tools 附录）——首行 JSON 元数据头 + 正文定界
                    System.Collections.Generic.Dictionary<string, object> doneFields = new System.Collections.Generic.Dictionary<string, object>();
                    doneFields["state"] = "done";
                    doneFields["total"] = total;
                    result = ToolMetaHead.With("Note", true, doneFields, "[Note] 🎉 全部 " + total + " 条任务已完成！");
                }
                else
                {
                    result = BuildNoteProgress();
                }
            }
            // M4c 前端面板——状态变化推送 SSE note 事件
            PushNoteState();
            return result;
        }

        /// <summary>
        /// Note 进度反馈文本——ExecuteNote 返回面（无计划/进度/最后一条提示）。
        /// </summary>
        /// <returns>进度文本</returns>
        private string BuildNoteProgress()
        {
            if (_noteTasks == null || _noteTasks.Length == 0)
            {
                string emptyText = "[Note] 暂无计划。用 action=set + content=任务1\\n任务2 来创建。";
                // 结构化返回体（design-ch4-tools 附录）——首行 JSON 元数据头 + 正文定界
                System.Collections.Generic.Dictionary<string, object> emptyFields = new System.Collections.Generic.Dictionary<string, object>();
                emptyFields["state"] = "empty";
                return ToolMetaHead.With("Note", true, emptyFields, emptyText);
            }
            // [段1] 待完成口径——总数 - 已完成（含当前未完成条）：已完成 + 待完成 == 总数
            int remain = _noteTasks.Length - _noteDone;
            string msg = "[Note] 第" + (_noteCurrent + 1) + "/" + _noteTasks.Length + "条  已完成" + _noteDone + "  待完成" + remain + "\n任务目标：" + _noteTasks[_noteCurrent];
            // [段2] 末条提示——当前即最后一条（旧判据 remain==0 随待完成口径修正失效）
            bool last = false;
            if (_noteCurrent + 1 >= _noteTasks.Length)
            {
                msg = msg + "（已是最后一条需求，完成后可结束本轮）";
                last = true;
            }
            // [段3] 结构化返回体（design-ch4-tools 附录）——首行 JSON 元数据头 + 正文定界
            System.Collections.Generic.Dictionary<string, object> noteFields = new System.Collections.Generic.Dictionary<string, object>();
            noteFields["state"] = "progress";
            noteFields["index"] = _noteCurrent + 1;
            noteFields["total"] = _noteTasks.Length;
            noteFields["done"] = _noteDone;
            noteFields["remain"] = remain;
            noteFields["last"] = last;
            return ToolMetaHead.With("Note", true, noteFields, msg);
        }

        /// <summary>
        /// Note 状态推送——SSE note 事件（M4c 前端悬浮气泡实时重绘；宿主未 Attach 时静默）。
        /// </summary>
        private void PushNoteState()
        {
            // F4 视图——note 控制块（SSE view 事件；前端悬浮气泡实时重绘）
            string noteJson = JsonUtil.Object(("type", "note"), ("state", JsonUtil.Raw(BuildNoteJson())));
            _viewBus.PushControl(noteJson);
        }

        /// <summary>
        /// Note 手动新增——前端 note.add 指令执行体（追加到末尾；空计划时创建；仅主线程调用）。
        /// 只进队列不推 LLM——计划一次性输出由 note.start（NoteStart）承担（莎拍板 2026-08-25：输入=新增队列，开始 Note=一次性输出）。
        /// </summary>
        /// <param name="text">任务文本</param>
        public void NoteAdd(string text)
        {
            if (text == null || text.Length == 0)
            {
                return;
            }
            string t = text.Trim();
            if (t.Length == 0)
            {
                return;
            }
            List<string> list = new List<string>();
            if (_noteTasks != null)
            {
                list.AddRange(_noteTasks);
            }
            list.Add(t);
            _noteTasks = list.ToArray();
            _noteJustCompleted = false;
            PushNoteState();
        }

        /// <summary>
        /// Note 启动——前端 note.start 指令执行体（拼接计划全文 + 当前进度，以 user 名义推给 LLM 开始执行；仅主线程调用）。
        /// </summary>
        public void NoteStart()
        {
            if (_noteTasks == null || _noteTasks.Length == 0)
            {
                return;
            }
            StringBuilder sb = new StringBuilder();
            sb.Append("[Note 计划] 共 ");
            sb.Append(_noteTasks.Length);
            sb.Append(" 条，当前第 ");
            sb.Append(_noteCurrent + 1);
            sb.Append(" 条：\n");
            for (int i = 0; i < _noteTasks.Length; i = i + 1)
            {
                sb.Append(i + 1);
                sb.Append(". ");
                sb.Append(_noteTasks[i]);
                sb.Append('\n');
            }
            sb.Append("请从当前任务开始逐条执行，每条完成后调用 Note 推进。");
            PostUserMessage(sb.ToString());
            LogStore.Add("CatHome4", 1, "Note 启动：共 " + _noteTasks.Length + " 条任务，当前第 " + (_noteCurrent + 1) + " 条", "CHAT");
        }

        /// <summary>
        /// Note 状态 JSON——GET /api/v1/note 数据源（tasks/current/done；空计划 tasks=[]；数组引用替换原子——HTTP 线程读安全）。
        /// </summary>
        /// <returns>Note 状态 JSON 文本</returns>
        public string BuildNoteJson()
        {
            List<string> tasks = new List<string>();
            if (_noteTasks != null)
            {
                tasks.AddRange(_noteTasks);
            }
            var obj = new
            {
                tasks = tasks.ToArray(),
                current = _noteCurrent,
                done = _noteDone,
                justCompleted = _noteJustCompleted
            };
            return JsonUtil.Serialize(obj);
        }
    }
}
