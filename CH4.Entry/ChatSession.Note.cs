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

        /// <summary>
        /// Note 工具执行体——M4a（CH2 语义移植：set 写入/无参推进/全完成清空；返回文本 = LLM 唯一状态面）。
        /// </summary>
        /// <param name="argsJson">参数 JSON（action/content/force）</param>
        /// <returns>进度反馈文本</returns>
        private string ExecuteNote(string argsJson)
        {
            string action = "";
            string content = "";
            bool force = false;
            if (argsJson != null && argsJson.Length > 0 && argsJson.StartsWith("{"))
            {
                try
                {
                    using (JsonDocument doc = JsonDocument.Parse(argsJson))
                    {
                        JsonElement root = doc.RootElement;
                        if (root.TryGetProperty("action", out JsonElement a) && a.ValueKind == JsonValueKind.String)
                        {
                            action = a.GetString() ?? "";
                        }
                        if (root.TryGetProperty("content", out JsonElement c) && c.ValueKind == JsonValueKind.String)
                        {
                            content = c.GetString() ?? "";
                        }
                        if (root.TryGetProperty("force", out JsonElement f) && f.ValueKind == JsonValueKind.True)
                        {
                            force = true;
                        }
                    }
                }
                catch (Exception)
                {
                    // 参数 JSON 损坏——按无参数推进语义处理（CH2 同款容错）
                }
            }
            string result;
            // [段2] set——写入新计划（\n 分割 + Trim 过滤空行；未完成时无 force 拒绝覆盖）
            if (action == "set" && content.Length > 0)
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
                    string[] lines = content.Replace("\r\n", "\n").Split('\n');
                    List<string> valid = new List<string>();
                    for (int i = 0; i < lines.Length; i = i + 1)
                    {
                        string t = lines[i].Trim();
                        if (t.Length > 0)
                        {
                            valid.Add(t);
                        }
                    }
                    _noteTasks = valid.ToArray();
                    _noteCurrent = 0;
                    _noteDone = 0;
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
                    result = "[Note] 🎉 全部 " + total + " 条任务已完成！";
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
                return "[Note] 暂无计划。用 action=set + content=任务1\\n任务2 来创建。";
            }
            int remain = _noteTasks.Length - _noteCurrent - 1;
            string msg = "[Note] 第" + (_noteCurrent + 1) + "/" + _noteTasks.Length + "条  已完成" + _noteDone + "  待完成" + remain + "\n任务目标：" + _noteTasks[_noteCurrent];
            if (remain == 0)
            {
                msg = msg + "（已是最后一条需求，完成后可结束本轮）";
            }
            return msg;
        }

        /// <summary>
        /// Note 状态推送——SSE note 事件（M4c 前端悬浮气泡实时重绘；宿主未 Attach 时静默）。
        /// </summary>
        private void PushNoteState()
        {
            if (_httpHost != null)
            {
                _httpHost.PushNoteState(BuildNoteJson());
            }
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
            LogStore.Add("CH4.Entry", 1, "Note 启动 | 共 " + _noteTasks.Length + " 条 | 当前 " + (_noteCurrent + 1), "CHAT");
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
                done = _noteDone
            };
            return JsonSerializer.Serialize(obj);
        }
    }
}
