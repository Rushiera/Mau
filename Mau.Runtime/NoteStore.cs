using System.Text;

namespace Mau.Runtime
{
    /// <summary>
    /// Note 状态——单猫轻量任务追踪（CH2 ExecuteNote 移植；G.5 Note 面板 2026-08-11 D.3）
    /// </summary>
    public sealed class NoteState
    {
        /// <summary>
        /// 任务列表——空数组=无 Note
        /// </summary>
        public string[] Tasks = new string[0];

        /// <summary>
        /// 当前任务索引（0-based）
        /// </summary>
        public int Current;

        /// <summary>
        /// 已完成任务数
        /// </summary>
        public int Done;

        /// <summary>
        /// 是否全部完成（Current 越界）
        /// </summary>
        public bool IsFinished
        {
            get { return Tasks.Length > 0 && Current >= Tasks.Length; }
        }
    }

    /// <summary>
    /// Note 存储——每猫 Note 状态（DataBox scope "note"——BRIK 唯一数据协议；G.5 Note 面板 2026-08-11 D.3）
    /// </summary>
    public static class NoteStore
    {
        /// <summary>
        /// 获取或创建猫的 Note 状态
        /// </summary>
        /// <param name="catName">猫名</param>
        /// <returns>Note 状态</returns>
        public static NoteState GetOrCreate(string catName)
        {
            return DataBox.GetOrCreate<NoteState>("note", BrickText.SafeKey(catName));
        }

        /// <summary>
        /// 序列化 Note 状态——"current|done\ntask1\ntask2\n..."（UI 渲染格式——CH2 RenderNotePanel 同构）
        /// </summary>
        /// <param name="state">Note 状态</param>
        /// <returns>序列化文本（空=无 Note）</returns>
        public static string Serialize(NoteState state)
        {
            if (state == null || state.Tasks == null || state.Tasks.Length == 0
                || state.Current < 0 || state.Current >= state.Tasks.Length)
            {
                return "";
            }
            StringBuilder sb = new StringBuilder();
            sb.Append(state.Current);
            sb.Append('|');
            sb.Append(state.Done);
            for (int i = 0; i < state.Tasks.Length; i = i + 1)
            {
                sb.Append('\n');
                sb.Append(state.Tasks[i]);
            }
            return sb.ToString();
        }

        /// <summary>
        /// 写新计划——action=set（有未完成任务且非 force 时拒绝——CH2 语义）
        /// </summary>
        /// <param name="state">Note 状态（GetOrCreate 获取）</param>
        /// <param name="content">计划文本（换行分割任务）</param>
        /// <param name="force">强制覆盖未完成计划</param>
        /// <param name="message">返回消息（LLM 工具结果）</param>
        /// <returns>true=写入成功</returns>
        public static bool SetPlan(NoteState state, string content, bool force, out string message)
        {
            int oldRemain = (state.Tasks != null && state.Tasks.Length > 0
                && state.Current >= 0 && state.Current < state.Tasks.Length)
                ? state.Tasks.Length - state.Current : 0;
            if (oldRemain > 0 && !force)
            {
                message = "[Note] ⚠️ 还有 " + oldRemain + " 条未完成。用 force=true 强制覆盖，或用 Note() 继续推进。";
                return false;
            }
            string[] rawTasks = BrickText.SafeText(content).Replace("\r\n", "\n").Split('\n');
            System.Collections.Generic.List<string> valid = new System.Collections.Generic.List<string>();
            for (int i = 0; i < rawTasks.Length; i = i + 1)
            {
                string task = rawTasks[i].Trim();
                if (task.Length > 0)
                {
                    valid.Add(task);
                }
            }
            state.Tasks = valid.ToArray();
            state.Current = 0;
            state.Done = 0;
            if (state.Tasks.Length == 0)
            {
                message = "[Note] 计划为空。";
                return true;
            }
            message = "[Note] 已写入 " + state.Tasks.Length + " 条任务计划。";
            return true;
        }

        /// <summary>
        /// 推进任务——无 action 调用（CH2 语义：当前任务完成 → 下一任务；全部完成 → 清空）
        /// </summary>
        /// <param name="state">Note 状态</param>
        /// <param name="message">返回消息（LLM 工具结果）</param>
        /// <returns>true=推进成功（空 Note 推进返回 false——无任务可推进）</returns>
        public static bool NextTask(NoteState state, out string message)
        {
            if (state.Tasks == null || state.Tasks.Length == 0
                || state.Current < 0 || state.Current >= state.Tasks.Length)
            {
                message = "[Note] 当前没有进行中的任务计划。";
                return false;
            }
            state.Done = state.Done + 1;
            state.Current = state.Current + 1;
            if (state.Current >= state.Tasks.Length)
            {
                int total = state.Tasks.Length;
                state.Tasks = new string[0];
                state.Current = 0;
                state.Done = 0;
                message = "[Note] 🎉 全部 " + total + " 条任务已完成！";
                return true;
            }
            message = "[Note] 当前任务：" + state.Tasks[state.Current];
            return true;
        }
    }
}
