using System;
using System.Collections.Generic;
using CatHome4.Http;
using Mau.Runtime;
using CH4;

namespace CatHome4.Observe
{
    /// <summary>
    /// Observe 域依赖注入面——入口壳 Bootstrap 一次性注入（S5 程序集拆分：观测面迁入）。
    /// 依赖：OA/会话桥/Flow 句柄表/帧流/HTTP 外观层（命名与入口壳原静态成员一致——引用零改动）。
    /// </summary>
    internal static partial class ObserveService
    {
        // [段1] 依赖注入字段——入口壳 Configure 一次性设置（命名与入口壳原静态成员一致——引用零改动）
        /// <summary>OA 工单平台——快照 OA 计数/审计（宿主注入）</summary>
        public static OA _oa;

        /// <summary>会话协调桥——会话段数据源（Core 域）</summary>
        public static ChatBridge _chatBridge;

        /// <summary>QuickCat Flow 句柄——状态观测（入口壳注入）</summary>
        public static FlowHandle _quickHandle;

        /// <summary>工具组 Flow 句柄表——按 Flow 名索引（入口壳注入）</summary>
        public static Dictionary<string, FlowHandle> _toolFlowHandles;

        /// <summary>注册 ID——观测与回收用</summary>
        public static long _quickId;

        /// <summary>工具组 Flow 注册 ID 表——按 Flow 名索引（入口壳注入）</summary>
        public static Dictionary<string, long> _toolFlowIds;

        /// <summary>Flow 驱动——注册表遍历（入口壳注入）</summary>
        public static FlowRunner _runner;

        /// <summary>HTTP 外观层——端口观测（入口壳注入；可空=未启动）</summary>
        public static HttpHost _httpHost;

        /// <summary>
        /// 注入 Observe 域依赖——入口壳 Bootstrap 调用（S5：程序集拆分接线）。
        /// </summary>
        /// <param name="oa">OA 工单平台</param>
        /// <param name="chatBridge">会话协调桥</param>
        /// <param name="quickHandle">QuickCat Flow 句柄</param>
        /// <param name="toolFlowHandles">工具组 Flow 句柄表</param>
        /// <param name="quickId">QuickCat 注册 ID</param>
        /// <param name="toolFlowIds">工具组注册 ID 表</param>
        /// <param name="runner">Flow 驱动</param>
        /// <param name="httpHost">HTTP 外观层（可空）</param>
        public static void Configure(OA oa, ChatBridge chatBridge, FlowHandle quickHandle, Dictionary<string, FlowHandle> toolFlowHandles, long quickId, Dictionary<string, long> toolFlowIds, FlowRunner runner, HttpHost httpHost)
        {
            _oa = oa;
            _chatBridge = chatBridge;
            _quickHandle = quickHandle;
            _toolFlowHandles = toolFlowHandles;
            _quickId = quickId;
            _toolFlowIds = toolFlowIds;
            _runner = runner;
            _httpHost = httpHost;
        }
    }
}
