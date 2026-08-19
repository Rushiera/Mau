using System;
using System.IO;

namespace Mau.Runtime
{
    /// <summary>
    /// 快照帧流——连续快照落盘（design-ch4-observe §三 frame.jsonl / §七 O3）。
    /// 主线程快照泵每 250ms Append 一行紧凑 JSON——运行回放/复盘数据源；默认开启（拍板）。
    /// </summary>
    public static class FrameStore
    {
        /// <summary>
        /// 帧流写者——null=未配置
        /// </summary>
        private static StreamWriter? _writer;

        /// <summary>
        /// 写锁——Append 串行化
        /// </summary>
        private static readonly object _gate = new object();

        /// <summary>
        /// 上次 flush 时间——1s 定时刷
        /// </summary>
        private static DateTime _lastFlush = DateTime.MinValue;

        /// <summary>
        /// 已配置路径——空=未配置
        /// </summary>
        private static string _path = "";

        /// <summary>
        /// 已写帧数——帧流统计（Close 时头部结算用）
        /// </summary>
        private static long _frameCount;

        /// <summary>
        /// 配置帧流文件——Data/runs/&lt;ts&gt;/frame.jsonl；幂等（路径变化重开）
        /// </summary>
        /// <param name="path">帧流文件路径，空=禁用</param>
        public static void Configure(string path)
        {
            lock (_gate)
            {
                Close();
                if (path == null || path.Length == 0)
                {
                    _path = "";
                    return;
                }
                string? dir = Path.GetDirectoryName(path);
                if (dir != null && dir.Length > 0)
                {
                    Directory.CreateDirectory(dir);
                }
                _path = path;
                _frameCount = 0;
                _writer = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite), new System.Text.UTF8Encoding(false));
                // AutoFlush——帧流尾部即时可见（强杀丢尾同断论——LogStore.OpenWriter 同修）
                _writer.AutoFlush = true;
            }
        }

        /// <summary>
        /// 追加一帧——一行紧凑 JSON（快照泵主线程调用；末尾换行）
        /// </summary>
        /// <param name="json">帧 JSON 文本</param>
        public static void Append(string json)
        {
            if (json == null || json.Length == 0)
            {
                return;
            }
            lock (_gate)
            {
                if (_writer == null)
                {
                    return;
                }
                try
                {
                    _writer.WriteLine(json);
                    _frameCount = _frameCount + 1;
                    DateTime now = DateTime.Now;
                    if ((now - _lastFlush).TotalSeconds >= 1.0)
                    {
                        _writer.Flush();
                        _lastFlush = now;
                    }
                }
                catch
                {
                    // 写失败静默——帧流是观测缓存非关键路径
                }
            }
        }

        /// <summary>
        /// 关闭——flush + 释放（宿主退出统一调用）
        /// </summary>
        public static void Close()
        {
            lock (_gate)
            {
                if (_writer != null)
                {
                    try
                    {
                        _writer.Flush();
                    }
                    catch
                    {
                    }
                    _writer.Dispose();
                    _writer = null;
                }
            }
        }

        /// <summary>
        /// 已写帧数——观测统计
        /// </summary>
        public static long FrameCount
        {
            get
            {
                lock (_gate)
                {
                    return _frameCount;
                }
            }
        }
    }
}