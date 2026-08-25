using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Mau.Runtime;

namespace CH4
{
    /// <summary>
    /// HttpHost 配置区面分部——GET/POST /api/v1/config 读写 + 敏感键掩码。
    /// P7b partial 拆分——自 HttpHost.cs 原样搬移，逻辑零改动。
    /// </summary>
    public sealed partial class HttpHost
    {
        /// <summary>
        /// 配置读取——GET /api/v1/config（配置区 v3）。
        /// 返回合并后的有效配置项：file（llm.cfg 显式值）优先，env（MAU_LLM_* 兜底）补齐；
        /// 敏感键（api_key/secret/token）掩码展示——snapshot 边界铁律同源。
        /// </summary>
        /// <returns>配置 JSON——版本 + items[key/value/source]</returns>
        private IResult HandleConfigGet()
        {
            // [段1] 解析配置存储——未绑定返回空列表（壳形态可渲染）
            ConfigStore cfg = null!;
            bool bound = DataBox.TryResolve<ConfigStore>(out cfg);
            ConfigSchema schema = null!;
            bool schemaBound = DataBox.TryResolve<ConfigSchema>(out schema);
            List<object> items = new List<object>();
            if (!bound || cfg == null)
            {
                var emptyResp = new
                {
                    version = 1,
                    items = items
                };
                return Results.Json(emptyResp);
            }
            // [段2] file 显式项——llm.cfg 值优先；敏感键掩码（effective 记录已显式键）
            KeyValuePair<string, string>[] all = cfg.All();
            Dictionary<string, string> effective = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < all.Length; i++)
            {
                string key = all[i].Key;
                string value = all[i].Value;
                if (!effective.ContainsKey(key))
                {
                    effective[key] = value;
                }
                if (IsSecretKey(key, schema))
                {
                    value = MaskSecret(value);
                }
                items.Add(BuildItem(key, value, "file", schema, schemaBound));
            }
            // [段3] env 兜底——file 未显式提供的 llm.* 键从 MAU_LLM_* 全局环境变量补齐（M1c 退役三键 base_url/api_key/model 不枚举——API 连接三参归配置池）
            string[] envKeys = new string[] { "llm.thinking", "llm.reasoning_effort" };
            string[] envNames = new string[] { "MAU_LLM_THINKING", "MAU_LLM_REASONING_EFFORT" };
            for (int i = 0; i < envKeys.Length; i++)
            {
                if (effective.ContainsKey(envKeys[i]))
                {
                    continue;
                }
                string envValue = Environment.GetEnvironmentVariable(envNames[i]);
                if (string.IsNullOrEmpty(envValue))
                {
                    continue;
                }
                string shown = envValue;
                if (IsSecretKey(envKeys[i], schema))
                {
                    shown = MaskSecret(shown);
                }
                items.Add(BuildItem(envKeys[i], shown, "env", schema, schemaBound));
            }
            var resp = new
            {
                version = 1,
                items = items
            };
            return Results.Json(resp);
        }

        /// <summary>
        /// 配置写入——POST /api/v1/config（body: {"key":"...","value":"..."}）。
        /// 落盘 llm.cfg（ConfigStore.Set + Save 原子写）；敏感键拒绝掩码值回写（防掩码覆盖）。
        /// 注意：运行时生效待 P7（DeepSeekLlmRuntime 构造期读配置——本轮写配置 = 落盘 + 提示重启）。
        /// </summary>
        /// <param name="ctx">HTTP 上下文</param>
        /// <returns>回执 JSON——ok/key/frame</returns>
        private async Task<IResult> HandleConfigPost(HttpContext ctx)
        {
            // [段1] body 解析——防御式 JSON {key,value}
            string body = "";
            using (System.IO.StreamReader reader = new System.IO.StreamReader(ctx.Request.Body))
            {
                body = await reader.ReadToEndAsync();
            }
            string key = "";
            string value = "";
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(body))
                {
                    JsonElement root = doc.RootElement;
                    if (root.TryGetProperty("key", out JsonElement k))
                    {
                        key = k.GetString() ?? "";
                    }
                    if (root.TryGetProperty("value", out JsonElement v))
                    {
                        value = v.GetString() ?? "";
                    }
                }
            }
            catch (Exception)
            {
                // body 非 JSON——key/value 保持空串走下方校验拒绝
                key = "";
                value = "";
            }
            // [段2] 校验——key/value 非空 + 掩码回写拒绝
            if (key.Length == 0)
            {
                return Results.Json(new { ok = false, error = "key 为空" });
            }
            if (value.Length == 0)
            {
                return Results.Json(new { ok = false, error = "value 为空" });
            }
            if (value.IndexOf("****", StringComparison.Ordinal) >= 0)
            {
                return Results.Json(new { ok = false, error = "value 含掩码标记——请输入真实值" });
            }
            // [段3] 写入落盘——P8.5d 统一受控写（ConfigStore.SetChecked：schema 白名单 + 值域校验 + 掩码拒绝 + 原子写回滚）
            ConfigStore cfg = null!;
            bool bound = DataBox.TryResolve<ConfigStore>(out cfg);
            if (!bound || cfg == null)
            {
                return Results.Json(new { ok = false, error = "配置存储未绑定" });
            }
            ConfigSchema schema = null!;
            bool schemaBound = DataBox.TryResolve<ConfigSchema>(out schema);
            string checkError;
            if (!cfg.SetChecked(key, value, schemaBound ? schema : null, out checkError))
            {
                return Results.Json(new { ok = false, error = checkError });
            }
            long frame = FlowRunner.GlobalFrame;
            // 回执值掩码——敏感键不回显新值明文（与 GET 掩码同规）
            string maskedValue;
            if (IsSecretKey(key))
            {
                maskedValue = MaskSecret(value);
            }
            else
            {
                maskedValue = value;
            }
            var resp = new
            {
                ok = true,
                key = key,
                value = maskedValue,
                frame = frame
            };
            return Results.Json(resp);
        }

        /// <summary>
        /// 构建配置项输出对象——schema 绑定时有条目则补 default/writable/desc/file（P8.5 配置群规范）；未绑定/未声明保持旧格式
        /// </summary>
        /// <param name="key">配置键</param>
        /// <param name="value">展示值（已掩码）</param>
        /// <param name="source">来源（file/env）</param>
        /// <param name="schema">配置 schema（可 null）</param>
        /// <param name="schemaBound">schema 是否绑定</param>
        /// <returns>输出对象</returns>
        private static object BuildItem(string key, string value, string source, ConfigSchema schema, bool schemaBound)
        {
            if (!schemaBound || schema == null)
            {
                return new { key = key, value = value, source = source };
            }
            ConfigSchema.Item item = schema.Find(key);
            if (item == null)
            {
                return new { key = key, value = value, source = source };
            }
            return new
            {
                key = key,
                value = value,
                source = source,
                file = item.File,
                @default = item.Default,
                writable = item.Writable,
                desc = item.Desc
            };
        }

        /// <summary>
        /// 敏感键判定——schema 声明优先（ConfigSchema.IsSensitive）；未声明键落旧子串兜底（api_key/secret/token 系内容掩码展示）
        /// </summary>
        /// <param name="key">配置键</param>
        /// <param name="schema">配置 schema（可 null）</param>
        /// <returns>是否敏感</returns>
        private static bool IsSecretKey(string key, ConfigSchema schema)
        {
            if (schema != null && schema.IsSensitive(key))
            {
                return true;
            }
            return IsSecretKey(key);
        }

        /// <summary>
        /// 敏感键判定——api_key/secret/token 系内容掩码展示（边界铁律：密钥不进快照/回显明文）
        /// </summary>
        /// <param name="key">配置键</param>
        /// <returns>是否敏感</returns>
        private static bool IsSecretKey(string key)
        {
            return key.IndexOf("api_key", StringComparison.OrdinalIgnoreCase) >= 0
                || key.IndexOf("secret", StringComparison.OrdinalIgnoreCase) >= 0
                || key.IndexOf("token", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// 敏感值掩码——前4 + **** + 后4（短值全掩码）
        /// </summary>
        /// <param name="value">原始值</param>
        /// <returns>掩码文本</returns>
        private static string MaskSecret(string value)
        {
            if (value == null || value.Length == 0)
            {
                return "";
            }
            if (value.Length <= 8)
            {
                return "****";
            }
            return value.Substring(0, 4) + "****" + value.Substring(value.Length - 4);
        }
    }
}