"""Kokoro 性能基准——单进程内多次合成（分离 G2P 与推理耗时）。

用法：
    python bench.py [音色名] [模型文件名] [--home <Kokoro 环境目录>]

输出：stdout 逐行进度 + 末尾 JSON 汇总。
"""

import json
import os
import sys
import time

# 管道捕获时 Python 默认用 locale 编码（Windows = GBK）——中文输出会乱码
if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8")

import kokoro_env  # noqa: E402  （须在 stdout 编码固定后导入）

TEXTS = [
    ("short", "好的，收到。", 3),
    (
        "medium",
        "任务已经完成，桌面宠物皮肤的渲染问题也一并修好了，请查看。",
        2,
    ),
    (
        "long",
        "这是一段用于性能基准测试的较长文本，主要目的是测量在当前硬件条件下，模型处理长句时的合成速度。"
        "需要注意的地方在于，语音播报只做招呼，细节仍然留在文本面，因此播报稿的规格是一句结论加一句指向。",
        1,
    ),
]


def say(message):
    """逐行输出并立即刷新。"""
    sys.stdout.write(message + "\n")
    sys.stdout.flush()


def bench_one(g2p, kokoro, text, voice, rounds):
    """对一段文本跑 N 轮，返回均值。"""
    g2p_ms = []
    synth_ms = []
    audio_s = 0.0
    for _ in range(rounds):
        started = time.perf_counter()
        phonemes, _ = g2p(text)
        g2p_ms.append((time.perf_counter() - started) * 1000.0)

        started = time.perf_counter()
        samples, rate = kokoro.create(phonemes, voice=voice, speed=1.0, is_phonemes=True)
        synth_ms.append((time.perf_counter() - started) * 1000.0)
        audio_s = len(samples) / float(rate)

    avg_g2p = sum(g2p_ms) / len(g2p_ms)
    avg_synth = sum(synth_ms) / len(synth_ms)
    return {
        "chars": len(text),
        "rounds": rounds,
        "audioSeconds": round(audio_s, 3),
        "g2pMs": round(avg_g2p, 1),
        "synthMs": round(avg_synth, 1),
        "totalMs": round(avg_g2p + avg_synth, 1),
        "rtf": round((avg_g2p + avg_synth) / 1000.0 / audio_s, 3),
    }


def take_home(argv):
    """取 --home 值并设入环境变量，同时从 argv 剥离该项。"""
    if "--home" not in argv:
        return
    index = argv.index("--home")
    value = argv[index + 1] if index + 1 < len(argv) else ""
    del argv[index:index + 2]
    if value:
        os.environ["KOKORO_HOME"] = value


def main():
    """基准入口。"""
    argv = sys.argv[1:]
    take_home(argv)

    voice = argv[0] if len(argv) > 0 else "zf_001"
    model_name = argv[1] if len(argv) > 1 else kokoro_env.DEFAULT_MODEL

    started = time.perf_counter()
    g2p, kokoro, model = kokoro_env.load_engine(model_name)
    load_ms = (time.perf_counter() - started) * 1000.0
    providers = list(kokoro.sess.get_providers())
    say(f"model: {model} | providers: {providers} | loadMs: {load_ms:.1f}")

    warm_phonemes, _ = g2p("预热。")
    started = time.perf_counter()
    kokoro.create(warm_phonemes, voice=voice, speed=1.0, is_phonemes=True)
    warm_ms = (time.perf_counter() - started) * 1000.0
    say(f"warmupMs: {warm_ms:.1f}")

    results = {}
    for name, text, rounds in TEXTS:
        say(f"bench {name} ({rounds} rounds, {len(text)} chars) ...")
        results[name] = bench_one(g2p, kokoro, text, voice, rounds)
        item = results[name]
        say(
            f"  {name}: audio={item['audioSeconds']}s synth={item['synthMs']}ms "
            f"rtf={item['rtf']}"
        )

    summary = {
        "model": model,
        "providers": providers,
        "voice": voice,
        "loadMs": round(load_ms, 1),
        "warmupMs": round(warm_ms, 1),
        "results": results,
    }
    sys.stdout.write(json.dumps(summary, ensure_ascii=False, indent=2) + "\n")
    return 0


if __name__ == "__main__":
    sys.exit(main())
