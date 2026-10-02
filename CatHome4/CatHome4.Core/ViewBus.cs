using CatHome4.Contracts;

namespace CH4
{
    /// <summary>
    /// 会话视图出口——实时区（A155 步二 2a：视图层双区化的结构收口）。
    /// 职责：全部视图 SSE 直推 + 实时区序号（流式文本 / 思考容器、retry 气泡、工具卡先行卡）。
    /// 分区：固化块区（SessionViewStore）管落盘持久块；本类管不落盘的过程块出口与序号。
    /// 宿主推送面未附加时全部推送静默跳过（与拆分前会话侧 _httpHost 判空语义一致）。
    /// </summary>
    internal sealed class ViewBus
    {
        /// <summary>宿主推送面——Bootstrap 段6 宿主 HTTP 启动后 Attach 赋值（构造时宿主 HTTP 未启动）</summary>
        private IHostPush _host;

        /// <summary>流式文本块序号——流式增量容器标识（整块到达时 replace 定位）</summary>
        private long _textStreamSeq;

        /// <summary>流式思考块序号——流式增量容器标识（reason 整块 replace；纯文本轮无整块）</summary>
        private long _reasonStreamSeq;

        /// <summary>retry 气泡视图序号——多次重试复用同一气泡（replaceSeq 替换不堆叠）</summary>
        private long _retrySeq;

        /// <summary>推送面就绪标志——宿主外观层已附加</summary>
        public bool Ready
        {
            get
            {
                return _host != null;
            }
        }

        /// <summary>retry 气泡在途标志——本轮是否推过 retry 视图（错误中止措辞分档判据）</summary>
        public bool RetryActive
        {
            get
            {
                return _retrySeq != 0;
            }
        }

        /// <summary>
        /// 附加宿主推送面——Bootstrap 段6 宿主 HTTP 启动后调用（SSE 转发面就位）。
        /// </summary>
        /// <param name="host">HTTP 外观层实例</param>
        public void Attach(IHostPush host)
        {
            _host = host;
        }

        /// <summary>流式文本增量——首个增量分配容器序号，后续复用（整块 text 到达时以该序号替换）</summary>
        /// <param name="json">载荷 JSON</param>
        public void PushTextStream(string json)
        {
            if (_host == null)
            {
                return;
            }
            _textStreamSeq = _host.PushView("stream", json, -1, _textStreamSeq);
        }

        /// <summary>流式思考增量——reasoning 独立容器序号（reason 整块到达时替换）</summary>
        /// <param name="json">载荷 JSON</param>
        public void PushReasonStream(string json)
        {
            if (_host == null)
            {
                return;
            }
            _reasonStreamSeq = _host.PushView("stream", json, -1, _reasonStreamSeq);
        }

        /// <summary>流式文本序号复位——整块到达 / 轮终统一调用</summary>
        public void ResetTextStream()
        {
            _textStreamSeq = 0;
        }

        /// <summary>流式思考序号复位——整块收口 / 轮终统一调用</summary>
        public void ResetReasonStream()
        {
            _reasonStreamSeq = 0;
        }

        /// <summary>取流式思考序号并复位——思考段终结取值（原会话侧「取值 + 清零」两步合并为单点）</summary>
        /// <returns>在途思考容器序号（0 = 无在途流式块）</returns>
        public long TakeReasonStreamSeq()
        {
            long seq = _reasonStreamSeq;
            _reasonStreamSeq = 0;
            return seq;
        }

        /// <summary>文本整块——replaceSeq 指向流式容器（无容器时前端新建气泡）</summary>
        /// <param name="json">载荷 JSON</param>
        public void PushTextBlock(string json)
        {
            if (_host == null)
            {
                return;
            }
            _host.PushView("text", json, _textStreamSeq, 0);
        }

        /// <summary>思考整块——replaceSeq 指向流式容器序号（思考段终结唯一出口传入）</summary>
        /// <param name="json">载荷 JSON</param>
        /// <param name="streamSeq">流式容器序号（0 = 无在途流式块）</param>
        public void PushReasonBlock(string json, long streamSeq)
        {
            if (_host == null)
            {
                return;
            }
            _host.PushView("reason", json, streamSeq, 0);
        }

        /// <summary>用户消息块——所有进内核消息的统一出口（前端气泡唯一来源）</summary>
        /// <param name="json">载荷 JSON</param>
        public void PushUser(string json)
        {
            if (_host == null)
            {
                return;
            }
            _host.PushView("user", json, -1, 0);
        }

        /// <summary>控制块——usage / chatdone / paused / note / session_reset（前端阶段控制唯一入口）</summary>
        /// <param name="json">载荷 JSON</param>
        public void PushControl(string json)
        {
            if (_host == null)
            {
                return;
            }
            _host.PushView("control", json, -1, 0);
        }

        /// <summary>错误气泡块——LLM 错误 / 发送失败（独立渲染面）</summary>
        /// <param name="json">载荷 JSON</param>
        public void PushError(string json)
        {
            if (_host == null)
            {
                return;
            }
            _host.PushView("error", json, -1, 0);
        }

        /// <summary>轮末统计块——本轮 Token 消耗 + 工具次数 + 四态用时 + 总耗时</summary>
        /// <param name="json">载荷 JSON</param>
        public void PushRoundSum(string json)
        {
            if (_host == null)
            {
                return;
            }
            _host.PushView("roundsum", json, -1, 0);
        }

        /// <summary>retry 气泡新建——分配 / 复用气泡序号（retrying 态）</summary>
        /// <param name="json">载荷 JSON</param>
        public void PushRetryNew(string json)
        {
            if (_host == null)
            {
                return;
            }
            _retrySeq = _host.PushView("retry", json, -1, _retrySeq);
        }

        /// <summary>retry 气泡原位更新——replaceSeq = 既有气泡序号（resolved / failed 态）</summary>
        /// <param name="json">载荷 JSON</param>
        public void PushRetryUpdate(string json)
        {
            if (_host == null)
            {
                return;
            }
            _host.PushView("retry", json, _retrySeq, 0);
        }

        /// <summary>retry 气泡序号复位——轮终（正常 / 中断 / 错误中止）统一调用，防跨轮残留</summary>
        public void ResetRetry()
        {
            _retrySeq = 0;
        }

        /// <summary>工具卡先行卡——分配独立序号（完成 / 中断以该序号原位替换）</summary>
        /// <param name="json">载荷 JSON</param>
        /// <returns>先行卡序号（推送面未就绪 = 0）</returns>
        public long PushToolCardPending(string json)
        {
            if (_host == null)
            {
                return 0;
            }
            return _host.PushView("toolcard", json, -1, 0);
        }

        /// <summary>工具卡终态——原位替换既有先行卡（replaceSeq = 先行卡序号）</summary>
        /// <param name="json">载荷 JSON</param>
        /// <param name="cardSeq">先行卡序号</param>
        public void PushToolCardFinal(string json, long cardSeq)
        {
            if (_host == null)
            {
                return;
            }
            _host.PushView("toolcard", json, cardSeq, 0);
        }
    }
}
