"""标点变体探针——同一句话 × 不同标点写法，比较音素产出、停顿分布与音频时长。

用法：
    <venv>/Scripts/python.exe tools/kokoro/make_variants.py --out <目录> [--voice zf_001] --home <KOKORO_HOME>

产物：<目录>/<序号>_<标签>.wav + 控制台对照表（停顿段分布 / 时长 / 合成耗时 / 音素）。
"""

import json
import os
import sys
import time

import kokoro_env

DEFAULT_VOICE = "zf_001"

# 同一句话的标点变体——内容不变，只改标点写法
VARIANTS = [
    ("comma", "emoji 无法强化 Kokoro 情绪，本机实测中文前端会将其丢弃，真正可用的只是标点、音色与语速。"),
    ("period", "emoji 无法强化 Kokoro 情绪。本机实测中文前端会将其丢弃。真正可用的只是标点、音色与语速。"),
    ("semicolon", "emoji 无法强化 Kokoro 情绪；本机实测中文前端会将其丢弃；真正可用的只是标点、音色与语速。"),
    ("colon", "emoji 无法强化 Kokoro 情绪：本机实测中文前端会将其丢弃：真正可用的只是标点、音色与语速。"),
    ("dash", "emoji 无法强化 Kokoro 情绪——本机实测中文前端会将其丢弃——真正可用的只是标点、音色与语速。"),
    ("ellipsis", "emoji 无法强化 Kokoro 情绪……本机实测中文前端会将其丢弃……真正可用的只是标点、音色与语速。"),
    ("exclaim", "emoji 无法强化 Kokoro 情绪！本机实测中文前端会将其丢弃！真正可用的只是标点、音色与语速！"),
    ("question", "emoji 无法强化 Kokoro 情绪？本机实测中文前端会将其丢弃？真正可用的只是标点、音色与语速？"),
    ("newline", "emoji 无法强化 Kokoro 情绪\n本机实测中文前端会将其丢弃\n真正可用的只是标点、音色与语速。"),
    ("blank（无标点）", "emoji 无法强化 Kokoro 情绪 本机实测中文前端会将其丢弃 真正可用的只是标点 音色与语速"),
]


def arg_value(argv, flag, fallback=None):
    """取命令行参数值——无则回落。"""
    if flag in argv:
        index = argv.index(flag)
        if index + 1 < len(argv):
            return argv[index + 1]
    return fallback


def silence_spans(samples, rate, gate_ratio=0.02, min_seconds=0.08):
    """按 20ms 窗 RMS 找静音段——返回 [(起点秒, 时长秒)]。"""
    import numpy as np

    frame = max(1, int(rate * 0.02))
    count = len(samples) // frame
    if count == 0:
        return []
    blocks = np.asarray(samples[:count * frame], dtype=np.float32).reshape(count, frame)
    rms = np.sqrt((blocks ** 2).mean(axis=1))
    gate = float(rms.max()) * gate_ratio
    spans = []
    begin = None
    for index, value in enumerate(rms):
        quiet = value < gate
        if quiet and begin is None:
            begin = index
        elif not quiet and begin is not None:
            spans.append((begin, index))
            begin = None
    if begin is not None:
        spans.append((begin, count))
    result = []
    for start, end in spans:
        seconds = (end - start) * frame / float(rate)
        if seconds >= min_seconds:
            result.append((start * frame / float(rate), seconds))
    return result


def pitch_stats(samples, rate, gate_ratio=0.02, tail_seconds=1.2):
    """粗略基频统计——40ms 帧自相关，返回 (全句均值Hz, 尾段均值Hz)。"""
    import numpy as np

    frame = int(rate * 0.04)
    count = len(samples) // frame
    low_lag = int(rate / 400.0)
    high_lag = int(rate / 75.0)
    values = []
    for index in range(count):
        block = np.asarray(samples[index * frame:(index + 1) * frame], dtype=np.float64)
        if np.sqrt((block ** 2).mean()) < gate_ratio:
            continue
        block = block - block.mean()
        correlation = np.correlate(block, block, mode="full")[frame - 1:]
        window = correlation[low_lag:high_lag]
        if len(window) == 0:
            continue
        peak = int(np.argmax(window)) + low_lag
        if correlation[peak] <= 0:
            continue
        values.append((index * frame / float(rate), rate / float(peak)))
    if not values:
        return 0.0, 0.0
    overall = sum(item for _, item in values) / len(values)
    total = len(samples) / float(rate)
    tail = [item for moment, item in values if moment >= total - tail_seconds]
    tail_mean = sum(tail) / len(tail) if tail else 0.0
    return overall, tail_mean


def main():
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    argv = sys.argv[1:]
    os.environ["KOKORO_HOME"] = arg_value(argv, "--home", os.environ.get("KOKORO_HOME", ""))
    out_dir = arg_value(argv, "--out")
    voice = arg_value(argv, "--voice", DEFAULT_VOICE)

    import soundfile as sf

    folder = kokoro_env.models_dir()
    with open(os.path.join(folder, kokoro_env.CONFIG_NAME), encoding="utf-8") as handle:
        vocab = set(json.load(handle)["vocab"].keys())

    g2p, kokoro, model = kokoro_env.load_engine()
    if out_dir:
        os.makedirs(out_dir, exist_ok=True)

    print(f"模型: {model} | 音色: {voice}")
    print(f"产物目录: {out_dir or '（未落盘）'}")
    print()
    print(f"{'变体':<18}{'时长s':>7}{'合成ms':>8}  {'基频 全句→尾段':<18}{'停顿段（起点+时长）':<44} 越界")
    print("-" * 130)

    for index, (label, text) in enumerate(VARIANTS, start=1):
        phonemes, _ = g2p(text)
        missing = "".join(sorted({c for c in phonemes if c not in vocab and not c.isspace()})) or "-"
        started = time.perf_counter()
        samples, rate = kokoro.create(phonemes, voice=voice, speed=1.0, is_phonemes=True)
        elapsed = (time.perf_counter() - started) * 1000.0
        seconds = len(samples) / float(rate)
        spans = silence_spans(samples, rate)
        pause_text = " · ".join(f"{start:.2f}s+{length:.2f}s" for start, length in spans) or "（无）"
        total_pause = sum(length for _, length in spans)
        if out_dir:
            path = os.path.join(out_dir, f"{index:02d}_{label.split('（')[0]}.wav")
            sf.write(path, samples, rate)
        overall, tail = pitch_stats(samples, rate)
        print(f"{label:<18}{seconds:>7.2f}{elapsed:>8.0f}  {overall:>6.1f}→{tail:>6.1f}Hz    {pause_text:<44} {missing}")
        print(f"{'':<18}{'':>7}{'':>8}  停顿合计 {total_pause:.2f}s / {len(spans)} 段")


if __name__ == "__main__":
    main()
