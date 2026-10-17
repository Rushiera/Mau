"""Kokoro 单次合成（调试用）——文本 -> WAV。

用法：
    python synth.py "要合成的文本" 输出.wav [音色名] [--home <Kokoro 环境目录>]

输出：stdout 一行 JSON（加载耗时 / 合成耗时 / 音频时长 / RTF / 采样率）。
🔴 stdout 独占——第三方库日志一律走 stderr，stdout 只留结果行。
"""

import json
import os
import sys
import time

_PROTOCOL = sys.stdout
_PROTOCOL.reconfigure(encoding="utf-8", newline="\n")
sys.stdout = sys.stderr

import soundfile as sf  # noqa: E402  （须在 stdout 置换后导入）

import kokoro_env  # noqa: E402


def synthesize(text, out_path, voice):
    """合成一段文本并写 WAV，返回计时摘要。"""
    started = time.perf_counter()
    g2p, kokoro, model = kokoro_env.load_engine()
    load_ms = (time.perf_counter() - started) * 1000.0

    phonemes, _ = g2p(text)
    t1 = time.perf_counter()
    samples, rate = kokoro.create(phonemes, voice=voice, speed=1.0, is_phonemes=True)
    synth_ms = (time.perf_counter() - t1) * 1000.0

    sf.write(out_path, samples, rate)
    audio_s = len(samples) / float(rate)

    return {
        "model": model,
        "voice": voice,
        "textChars": len(text),
        "phonemeChars": len(phonemes),
        "loadMs": round(load_ms, 1),
        "synthMs": round(synth_ms, 1),
        "audioSeconds": round(audio_s, 3),
        "rtf": round(synth_ms / 1000.0 / audio_s, 3) if audio_s > 0 else None,
        "sampleRate": rate,
        "samples": int(len(samples)),
        "out": out_path,
    }


def main():
    """命令行入口。"""
    argv = sys.argv[1:]
    if "--home" in argv:
        index = argv.index("--home")
        home = argv[index + 1] if index + 1 < len(argv) else ""
        del argv[index:index + 2]
        if home:
            os.environ["KOKORO_HOME"] = home

    text = argv[0] if len(argv) > 0 else "千里之行，始于足下。"
    out_path = argv[1] if len(argv) > 1 else "audio.wav"
    voice = argv[2] if len(argv) > 2 else "zf_001"

    summary = synthesize(text, out_path, voice)
    _PROTOCOL.write(json.dumps(summary, ensure_ascii=False) + "\n")
    _PROTOCOL.flush()
    return 0


if __name__ == "__main__":
    sys.exit(main())
