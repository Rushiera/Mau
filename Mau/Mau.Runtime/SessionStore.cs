using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Mau.Runtime
{
    /// <summary>
    /// 会话统计——前文真实 usage 持久化（CloseRound 写入；旧文件缺字段兼容——可空）。
    /// 只存真实值（LLM usage 回传），不做任何估算。
    /// </summary>
    public struct SessionStats
    {
        /// <summary>消息条数——真实前文消息数（含 system）</summary>
        public long EntryCount;

        /// <summary>最近一轮真实 prompt token——命中 + 非命中总和（usage.prompt_tokens）</summary>
        public long LastPromptTokens;

        /// <summary>最近一轮缓存命中 token（usage.prompt_tokens_details.cached_tokens）</summary>
        public long LastCacheHitTokens;

        /// <summary>最近一轮输出 token（usage.completion_tokens）</summary>
        public long LastCompletionTokens;

        /// <summary>最近一次请求的单次前文长度（非累计——前文长度数据源；旧文件缺省 0）</summary>
        public long LastContextTokens;
    }
    /// <summary>
    /// 会话前文管理器——MajorDomoCat 消息历史落盘（2026-08-16 下沉 Mau.Runtime，借鉴 CH3 ICatContextStore 形态）。
    /// 落盘：Data/sessions/majordomo.json（system + messages JSON）。
    /// 策略：只保存/恢复 + 基础结构修复——上下文策略（截断/预算）P5 不做。
    /// </summary>
    public sealed class SessionStore
    {
        /// <summary>
        /// 会话文件路径
        /// </summary>
        private readonly string _path;

        /// <summary>
        /// 建立前文管理器
        /// </summary>
        /// <param name="path">会话文件路径</param>
        public SessionStore(string path)
        {
            _path = path;
        }

        /// <summary>
        /// 尝试加载前文——文件不存在返回 false；损坏 JSON 返回 false（保留原文件，不覆盖破坏）
        /// </summary>
        /// <param name="messages">加载的消息数组</param>
        /// <returns>true=加载成功</returns>
        public bool TryLoad(out LlmMessage[] messages)
        {
            SessionStats? stats;
            return TryLoad(out messages, out stats);
        }

        /// <summary>
        /// 尝试加载前文含统计——文件不存在返回 false；损坏 JSON 返回 false（保留原文件，不覆盖破坏）
        /// </summary>
        /// <param name="messages">加载的消息数组</param>
        /// <param name="stats">会话统计（可空=旧文件无统计）</param>
        /// <returns>true=加载成功</returns>
        public bool TryLoad(out LlmMessage[] messages, out SessionStats? stats)
        {
            messages = new LlmMessage[0];
            stats = null;
            if (!File.Exists(_path))
            {
                return false;
            }
            try
            {
                string json = File.ReadAllText(_path);
                // LlmMessage 是 struct——字段序列化需 IncludeFields（System.Text.Json 默认只序列化属性）
                JsonSerializerOptions options = new JsonSerializerOptions();
                options.IncludeFields = true;
                SessionFileData? data = JsonSerializer.Deserialize<SessionFileData>(json, options);
                if (data == null || data.Messages == null)
                {
                    BackupCorruptFile();
                    return false;
                }
                messages = data.Messages;
                stats = data.Stats;
                return true;
            }
            catch (Exception)
            {
                // 损坏 JSON——备份原文件（可手工修复/追溯），返回加载失败
                BackupCorruptFile();
                return false;
            }
        }

        /// <summary>
        /// 保存前文——全量覆写会话文件（无统计——委托带统计重载）
        /// </summary>
        /// <param name="messages">消息数组</param>
        public void Save(LlmMessage[] messages)
        {
            Save(messages, null);
        }

        /// <summary>
        /// 保存前文含统计——全量覆写会话文件（stats 可空=不写统计；旧文件兼容）
        /// </summary>
        /// <param name="messages">消息数组</param>
        /// <param name="stats">会话统计（真实 usage；可空=缺省）</param>
        public void Save(LlmMessage[] messages, SessionStats? stats)
        {
            try
            {
                string? dir = Path.GetDirectoryName(_path);
                if (dir != null && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                SessionFileData data = new SessionFileData();
                data.Messages = messages;
                data.Stats = stats;
                // LlmMessage 是 struct——字段序列化需 IncludeFields（System.Text.Json 默认只序列化属性）
                JsonSerializerOptions options = new JsonSerializerOptions();
                options.IncludeFields = true;
                options.Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping;   // 中文直出（默认 \uXXXX 转义人读不便——2026-09-08 全局统一）
                string json = JsonSerializer.Serialize(data, options);
                File.WriteAllText(_path, json);
            }
            catch (Exception)
            {
                // 保存失败不阻断会话（下次收工/结束再试）——前文是增强不是依赖
            }
        }

        /// <summary>
        /// 会话文件数据——JSON 形态（LlmRole 枚举按数值序列化——机器读文件，可读性非目标）
        /// </summary>
        private sealed class SessionFileData
        {
            /// <summary>
            /// 消息数组
            /// </summary>
            public LlmMessage[]? Messages { get; set; }

            /// <summary>
            /// 会话统计——真实 usage（可空=旧文件无统计）
            /// </summary>
            public SessionStats? Stats { get; set; }
        }

        /// <summary>
        /// 备份损坏前文文件——改名 .bad（保留原样可手工修复/追溯；下次启动不再重复解析坏文件）。
        /// </summary>
        private void BackupCorruptFile()
        {
            try
            {
                string badPath = _path + ".bad";
                if (File.Exists(badPath))
                {
                    File.Delete(badPath);
                }
                File.Move(_path, badPath);
                LogStore.Add("CatHome4", 3, "会话前文损坏，已备份为 " + badPath + "（本次按新会话启动）", "CHAT");
            }
            catch (Exception)
            {
                // 备份失败不阻断加载失败语义——原文件保留
            }
        }
    }
}
