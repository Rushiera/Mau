using System;
using System.Text.Json;

namespace Mau.Serve
{
    /// <summary>
    /// 服务请求——一行 JSON，NamedPipe 传输
    /// </summary>
    public sealed class ServeRequest
    {
        /// <summary>
        /// 操作——ping / stop / list / read / find_ref / diag / comment_check / patch / compile
        /// </summary>
        public string Op = "";

        /// <summary>
        /// 参数一——list 无 / read 类型名 / find_ref 标识符 / patch 类型名 / compile 逻辑名
        /// </summary>
        public string Arg1 = "";

        /// <summary>
        /// 参数二——read 成员名 / patch 方法名
        /// </summary>
        public string Arg2 = "";

        /// <summary>
        /// 载荷——patch 新方法体 / compile 完整 C# 源码
        /// </summary>
        public string Body = "";

        /// <summary>
        /// 序列化为 JSON 行
        /// </summary>
        /// <returns>JSON 文本</returns>
        public string ToJsonLine()
        {
            return JsonSerializer.Serialize(this, JsonOptions);
        }

        /// <summary>
        /// 从 JSON 行反序列化
        /// </summary>
        /// <param name="line">JSON 文本</param>
        /// <returns>请求</returns>
        public static ServeRequest FromJsonLine(string line)
        {
            ServeRequest? request = JsonSerializer.Deserialize<ServeRequest>(line, JsonOptions);
            if (request == null)
            {
                throw new InvalidOperationException("请求反序列化失败。");
            }
            return request;
        }

        /// <summary>
        /// JSON 选项——包含公共字段
        /// </summary>
        private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

        /// <summary>
        /// 创建 JSON 选项
        /// </summary>
        /// <returns>选项</returns>
        private static JsonSerializerOptions CreateJsonOptions()
        {
            JsonSerializerOptions options = new JsonSerializerOptions();
            options.IncludeFields = true;
            return options;
        }
    }

    /// <summary>
    /// 服务响应——一行 JSON
    /// </summary>
    public sealed class ServeResponse
    {
        /// <summary>
        /// 是否成功
        /// </summary>
        public bool Ok = false;

        /// <summary>
        /// 多行结果——list / find_ref / diag / comment_check
        /// </summary>
        public string[] Lines = Array.Empty<string>();

        /// <summary>
        /// 单行结果——read / ping / compile
        /// </summary>
        public string Text = "";

        /// <summary>
        /// 错误消息——Ok=false 时
        /// </summary>
        public string Error = "";

        /// <summary>
        /// 序列化为 JSON 行
        /// </summary>
        /// <returns>JSON 文本</returns>
        public string ToJsonLine()
        {
            return JsonSerializer.Serialize(this, JsonOptions);
        }

        /// <summary>
        /// 从 JSON 行反序列化
        /// </summary>
        /// <param name="line">JSON 文本</param>
        /// <returns>响应</returns>
        public static ServeResponse FromJsonLine(string line)
        {
            ServeResponse? response = JsonSerializer.Deserialize<ServeResponse>(line, JsonOptions);
            if (response == null)
            {
                throw new InvalidOperationException("响应反序列化失败。");
            }
            return response;
        }

        /// <summary>
        /// JSON 选项——包含公共字段
        /// </summary>
        private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

        /// <summary>
        /// 创建 JSON 选项
        /// </summary>
        /// <returns>选项</returns>
        private static JsonSerializerOptions CreateJsonOptions()
        {
            JsonSerializerOptions options = new JsonSerializerOptions();
            options.IncludeFields = true;
            return options;
        }
    }
}
