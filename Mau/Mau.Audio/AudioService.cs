using System;
using System.Collections.Generic;
using System.IO;
using Mau.Contracts;
using NAudio.Wave;

namespace Mau.Audio
{
    /// <summary>
    /// 音频播放服务——per-Key 串行 + 组间并发（投递即返回，只进不出；design-mau-audio §四）。
    /// 调度全在本类内部自洽：主线程只做「入队」一个原子动作，播完由 NAudio 回调驱动队列前进——
    /// 不碰 OA / CommandBus / FlowRunner（主线程守卫面）。
    /// </summary>
    public sealed class AudioService : IAudioService
    {
        /// <summary>
        /// 默认 Key——空 Key 归一（不带 Key 的所有音频自动串行）
        /// </summary>
        public const string DefaultKey = "_default";

        /// <summary>
        /// 调度锁——入队与回调（队列前进）共用的临界区
        /// </summary>
        private readonly object _gate = new object();

        /// <summary>
        /// 活跃 Key 集合——某 Key 在集合中即表示有音频正在播（同 Key 后续入队）
        /// </summary>
        private readonly HashSet<string> _playing = new HashSet<string>();

        /// <summary>
        /// 待播队列——Key → 同 Key 未起播的条目（先来后到）
        /// </summary>
        private readonly Dictionary<string, Queue<AudioItem>> _queues = new Dictionary<string, Queue<AudioItem>>();

        /// <summary>
        /// 活跃播放器表——Key → 当前播放器（防 GC；串联 reader 释放）
        /// </summary>
        private readonly Dictionary<string, WasapiPlayer> _players = new Dictionary<string, WasapiPlayer>();

        /// <summary>
        /// 活跃读取器表——Key → 当前 WAV 读取器
        /// </summary>
        private readonly Dictionary<string, WaveStream> _readers = new Dictionary<string, WaveStream>();

        /// <summary>
        /// 入队——主线程调用（积木转调）；起播或排队，立即返回。
        /// </summary>
        /// <param name="key">串行分组键（空 = 默认 Key）</param>
        /// <param name="path">WAV 文件路径</param>
        /// <param name="volume">会话音量（&lt; 0 = 用全局配置 audio.volume；≥ 0 = 覆盖）</param>
        /// <returns>""=已受理；"ERR|CODE|说明"=失败（可见）</returns>
        public string Enqueue(string key, string path, float volume)
        {
            if (path == null || path.Length == 0)
            {
                return "ERR|BAD_ARGS|path 为空";
            }
            if (!File.Exists(path))
            {
                return "ERR|NOT_FOUND|文件不存在: " + path;
            }
            string k = (key == null || key.Length == 0) ? DefaultKey : key;
            lock (_gate)
            {
                if (_playing.Contains(k))
                {
                    Queue<AudioItem> q;
                    if (!_queues.TryGetValue(k, out q))
                    {
                        q = new Queue<AudioItem>();
                        _queues[k] = q;
                    }
                    q.Enqueue(new AudioItem(path, volume));
                    return "";
                }
                _playing.Add(k);
                StartPlay(k, path, volume);
            }
            return "";
        }

        /// <summary>
        /// 活跃 Key 数——诊断哨兵
        /// </summary>
        /// <returns>当前有音频在播的 Key 数</returns>
        public int ActiveKeyCount()
        {
            lock (_gate)
            {
                return _playing.Count;
            }
        }

        /// <summary>
        /// 起播——构造读取器 + 播放器，注册停止回调；须在锁内调用（调用方持 _gate）
        /// </summary>
        /// <param name="key">分组键</param>
        /// <param name="path">WAV 路径</param>
        /// <param name="volume">会话音量</param>
        private void StartPlay(string key, string path, float volume)
        {
            WaveStream reader = null;
            WasapiPlayer player = null;
            try
            {
                reader = new WaveFileReader(path);
                player = new WasapiPlayerBuilder().Build();
                player.Init(reader);
                player.Volume = ResolveVolume(volume);
                player.PlaybackStopped += delegate (object sender, StoppedEventArgs e)
                {
                    OnPlaybackStopped(key);
                };
                _readers[key] = reader;
                _players[key] = player;
                player.Play();
            }
            catch (Exception ex)
            {
                // 起播失败——释放半成品 + 出声；该 Key 复位并尝试下一个（失败可见，不静默）
                LogFail(key, "起播失败: " + ex.Message);
                DisposeCurrent(key);
                _playing.Remove(key);
                Queue<AudioItem> q;
                if (_queues.TryGetValue(key, out q) && q.Count > 0)
                {
                    AudioItem next = q.Dequeue();
                    if (q.Count == 0)
                    {
                        _queues.Remove(key);
                    }
                    _playing.Add(key);
                    StartPlay(key, next.Path, next.Volume);
                }
            }
        }

        /// <summary>
        /// 播放停止回调——队列前进的驱动信号（非业务回执）；锁内取下一个或复位
        /// </summary>
        /// <param name="key">分组键</param>
        private void OnPlaybackStopped(string key)
        {
            lock (_gate)
            {
                DisposeCurrent(key);
                Queue<AudioItem> q;
                if (_queues.TryGetValue(key, out q) && q.Count > 0)
                {
                    AudioItem next = q.Dequeue();
                    if (q.Count == 0)
                    {
                        _queues.Remove(key);
                    }
                    StartPlay(key, next.Path, next.Volume);
                }
                else
                {
                    _queues.Remove(key);
                    _playing.Remove(key);
                }
            }
        }

        /// <summary>
        /// 释放该 Key 当前读取器与播放器——须在锁内调用
        /// </summary>
        /// <param name="key">分组键</param>
        private void DisposeCurrent(string key)
        {
            WasapiPlayer player;
            if (_players.TryGetValue(key, out player))
            {
                _players.Remove(key);
                try
                {
                    player.Dispose();
                }
                catch (Exception ex)
                {
                    LogFail(key, "播放器释放异常: " + ex.Message);
                }
            }
            WaveStream reader;
            if (_readers.TryGetValue(key, out reader))
            {
                _readers.Remove(key);
                try
                {
                    reader.Dispose();
                }
                catch (Exception ex)
                {
                    LogFail(key, "读取器释放异常: " + ex.Message);
                }
            }
        }

        /// <summary>
        /// 解析音量——requested &lt; 0 表示未指定，采用全局配置 audio.volume（百分比→0~1 系数）；否则用传入值。
        /// 全局配置由宿主注入（DataBox.Bind&lt;ConfigStore&gt;）；未注入 / 读取失败回落 100%。
        /// </summary>
        /// <param name="requested">调用方音量（&lt; 0 = 用全局配置；≥ 0 = 覆盖）</param>
        /// <returns>会话音量系数（0~2）</returns>
        private static float ResolveVolume(float requested)
        {
            if (requested >= 0f)
            {
                return requested;
            }
            float pct = 100f;
            Mau.Runtime.ConfigStore store = null;
            if (Mau.Runtime.DataBox.TryResolve<Mau.Runtime.ConfigStore>(out store) && store != null)
            {
                string text = store.Get("audio.volume", "100");
                float parsed;
                if (float.TryParse(text, out parsed))
                {
                    pct = parsed;
                }
            }
            if (pct < 0f)
            {
                pct = 0f;
            }
            if (pct > 200f)
            {
                pct = 200f;
            }
            return pct / 100f;
        }

        /// <summary>
        /// 失败出声——失败必须可见（AGENTS §一-4）
        /// </summary>
        /// <param name="key">分组键</param>
        /// <param name="message">失败说明</param>
        private static void LogFail(string key, string message)
        {
            Mau.Runtime.LogStore.Add("Audio", 3, "[" + key + "] " + message, "AUDIO");
        }

        /// <summary>
        /// 待播条目——路径 + 音量
        /// </summary>
        private sealed class AudioItem
        {
            /// <summary>
            /// WAV 文件路径
            /// </summary>
            public readonly string Path;

            /// <summary>
            /// 会话音量（0~1）
            /// </summary>
            public readonly float Volume;

            /// <summary>
            /// 构造待播条目
            /// </summary>
            /// <param name="path">WAV 路径</param>
            /// <param name="volume">会话音量</param>
            public AudioItem(string path, float volume)
            {
                Path = path;
                Volume = volume;
            }
        }
    }
}
