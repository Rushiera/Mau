using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Mau.Runtime;

namespace CH4
{
    /// <summary>
    /// 会话前文管理器——MajorDomoCat 消息历史落盘（CH4 侧实现，借鉴 CH3 ICatContextStore 形态）。
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
            messages = new LlmMessage[0];
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
                SessionFileData data = JsonSerializer.Deserialize<SessionFileData>(json, options);
                if (data == null || data.Messages == null)
                {
                    return false;
                }
                messages = data.Messages;
                return true;
            }
            catch (Exception)
            {
                // 损坏 JSON——保留原文件（下次可手工修复），返回加载失败
                return false;
            }
        }
        /// <summary>
        /// 保存前文——全量覆写会话文件
        /// </summary>
        /// <param name="messages">消息数组</param>
        public void Save(LlmMessage[] messages)
{
            try
            {
                string dir = Path.GetDirectoryName(_path);
                if (dir != null && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                SessionFileData data = new SessionFileData();
                data.Messages = messages;
                // LlmMessage 是 struct——字段序列化需 IncludeFields（System.Text.Json 默认只序列化属性）
                JsonSerializerOptions options = new JsonSerializerOptions();
                options.IncludeFields = true;
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
            public LlmMessage[] Messages { get; set; }
        }
    }
}
