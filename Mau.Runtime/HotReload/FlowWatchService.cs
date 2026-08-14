using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Mau.Runtime
{
    /// <summary>
    /// 产物目录热感知服务（D5-D10）——监听 Flows/*.dll 指纹轮询，检测变化标记 pending（按 FL dll 粒度）。
    /// 与 FlowHost 同层（Mau.Runtime）；宿主接入三行：StartWatch → 查 Pending → ReloadFlows。
    /// 检测自动（帧计数节流轮询）、重载主动（宿主显式调用）——失败回滚保留旧 dll。
    /// </summary>
    public sealed class FlowWatchService
    {
        /// <summary>
        /// 监听目录——实例自身运行区（public/Flows），不感知源层结构（D5）
        /// </summary>
        private string _watchDir;

        /// <summary>
        /// 轮询节流帧数——每 N 帧查一次（D9 帧驱动一致，不引新线程）
        /// </summary>
        private readonly int _pollEveryFrames;

        /// <summary>
        /// 上次轮询帧号
        /// </summary>
        private long _lastPollFrame;

        /// <summary>
        /// 指纹表——dll 文件名 → 指纹（大小|修改时间|SHA256）
        /// </summary>
        private readonly Dictionary<string, string> _fingerprints;

        /// <summary>
        /// pending 清单——检测到变化的 dll 文件名（按 FL dll 粒度，D7）
        /// </summary>
        private readonly List<string> _pending;

        /// <summary>
        /// 服务锁
        /// </summary>
        private readonly object _gate;

        /// <summary>
        /// 构造热感知服务
        /// </summary>
        /// <param name="pollEveryFrames">轮询节流帧数（默认 60 帧）</param>
        public FlowWatchService(int pollEveryFrames = 60)
        {
            _watchDir = "";
            _pollEveryFrames = pollEveryFrames;
            if (_pollEveryFrames <= 0)
            {
                _pollEveryFrames = 60;
            }
            _lastPollFrame = -1;
            _fingerprints = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _pending = new List<string>();
            _gate = new object();
        }

        /// <summary>
        /// 启动监听——绑定产物目录并建立初始指纹（首次轮询全量标记为新增）
        /// </summary>
        /// <param name="watchDir">产物目录（Flows/*.dll）</param>
        public void StartWatch(string watchDir)
        {
            if (string.IsNullOrWhiteSpace(watchDir))
            {
                throw new ArgumentException("监听目录为空", "watchDir");
            }
            lock (_gate)
            {
                _watchDir = Path.GetFullPath(watchDir);
                _fingerprints.Clear();
                _pending.Clear();
                _lastPollFrame = -1;
            }
        }

        /// <summary>
        /// 帧驱动轮询——宿主每帧调用；按帧计数节流（D9），节流帧内不扫描
        /// </summary>
        /// <param name="frame">当前帧号</param>
        public void Tick(long frame)
        {
            lock (_gate)
            {
                if (_watchDir.Length == 0)
                {
                    return;
                }
                if (_lastPollFrame >= 0 && frame - _lastPollFrame < _pollEveryFrames)
                {
                    return;
                }
                _lastPollFrame = frame;
                PollLocked();
            }
        }

        /// <summary>
        /// 立即轮询（忽略节流）——测试/启动时调用
        /// </summary>
        public void PollNow()
        {
            lock (_gate)
            {
                // 立即轮询视为当前帧——更新节流基准（Tick 节流窗口从此刻起算）
                _lastPollFrame = 0;
                PollLocked();
            }
        }

        /// <summary>
        /// pending dll 文件名清单——只读快照
        /// </summary>
        public string[] Pending
        {
            get
            {
                lock (_gate)
                {
                    return _pending.ToArray();
                }
            }
        }

        /// <summary>
        /// 消费 pending——取走并清空（重载完成后调用）
        /// </summary>
        /// <returns>pending 清单</returns>
        public string[] ConsumePending()
        {
            lock (_gate)
            {
                string[] result = _pending.ToArray();
                _pending.Clear();
                return result;
            }
        }

        /// <summary>
        /// 重置——清空监听/指纹/pending（D26 统一 Reset 契约；宿主切换/测试隔离调用）
        /// </summary>
        public void Reset()
        {
            lock (_gate)
            {
                _watchDir = "";
                _fingerprints.Clear();
                _pending.Clear();
                _lastPollFrame = -1;
            }
        }

        /// <summary>
        /// 扫描目录——指纹对比标记变化（新增/修改/删除）
        /// </summary>
        private void PollLocked()
        {
            // [段1] 当前文件集
            Dictionary<string, string> current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (Directory.Exists(_watchDir))
            {
                string[] dlls = Directory.GetFiles(_watchDir, "*.dll", SearchOption.TopDirectoryOnly);
                for (int i = 0; i < dlls.Length; i = i + 1)
                {
                    string name = Path.GetFileName(dlls[i]);
                    string fp = ComputeFingerprint(dlls[i]);
                    if (fp.Length > 0)
                    {
                        current[name] = fp;
                    }
                }
            }

            // [段2] 对比——新增/修改 → pending（完整路径）
            foreach (KeyValuePair<string, string> pair in current)
            {
                string? oldFp;
                if (_fingerprints.TryGetValue(pair.Key, out oldFp))
                {
                    if (oldFp != pair.Value && !_pending.Contains(pair.Key))
                    {
                        _pending.Add(Path.Combine(_watchDir, pair.Key));
                    }
                }
                else if (!_pending.Contains(pair.Key))
                {
                    _pending.Add(Path.Combine(_watchDir, pair.Key));
                }
            }

            // [段3] 删除 → pending（宿主按文件名找不到旧 handle 时忽略）
            List<string> removed = new List<string>();
            foreach (KeyValuePair<string, string> pair in _fingerprints)
            {
                if (!current.ContainsKey(pair.Key) && !_pending.Contains(pair.Key))
                {
                    _pending.Add(Path.Combine(_watchDir, pair.Key));
                }
            }

            // [段4] 更新指纹表
            _fingerprints.Clear();
            foreach (KeyValuePair<string, string> pair in current)
            {
                _fingerprints[pair.Key] = pair.Value;
            }
        }

        /// <summary>
        /// 计算 dll 指纹——大小|修改时间|SHA256（D6 内容哈希成本可忽略）
        /// </summary>
        /// <param name="dllPath">dll 路径</param>
        /// <returns>指纹或空串（读取失败）</returns>
        private static string ComputeFingerprint(string dllPath)
        {
            try
            {
                FileInfo info = new FileInfo(dllPath);
                byte[] bytes = File.ReadAllBytes(dllPath);
                byte[] hash = SHA256.HashData(bytes);
                StringBuilder hex = new StringBuilder();
                for (int i = 0; i < hash.Length; i++)
                {
                    hex.Append(hash[i].ToString("X2"));
                }
                return info.Length.ToString() + "|" + info.LastWriteTimeUtc.Ticks.ToString() + "|" + hex.ToString();
            }
            catch
            {
                return "";
            }
        }
    }
}
