"""GPU 性能测试——精确 10 / 30 / 100 字，各 5 轮（区分首次与稳态）。

用法：
    python perf_test.py [模型文件名] [--home <Kokoro 环境目录>]

输出：stdout 逐行明细 + 末尾汇总表。
"""

import os
import statistics
import sys
import time

# 管道捕获时 Python 默认用 locale 编码（Windows = GBK）——中文输出会乱码
if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8")

import kokoro_env  # noqa: E402  （须在 stdout 编码固定后导入）

ROUNDS = 5
VOICE = "zf_001"
LENGTHS = [10, 30, 100]

BASE = (
    "播放一段用于性能测试的文本，用来测量不同长度下的合成速度。"
    "语音播报只做招呼，细节留在文本面，因此播报稿的规格是一句结论加一句指向，长度通常不长。"
)


def say(message):
    """逐行输出并立即刷新。"""
    sys.stdout.write(message + "\n")
    sys.stdout.flush()


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
    """性能测试入口。"""
    argv = sys.argv[1:]
    take_home(argv)
    model_name = argv[0] if len(argv) > 0 else kokoro_env.DEFAULT_MODEL

    started = time.perf_counter()
    g2p, kokoro, model = kokoro_env.load_engine(model_name)
    load_ms = (time.perf_counter() - started) * 1000.0

    say(f"model={model}")
    say(f"providers={list(kokoro.sess.get_providers())}")
    say(f"loadMs={load_ms:.1f}")
    say("")

    source = BASE * 4
    summary = []
    for target in LENGTHS:
        text = source[:target]
        phonemes, _ = g2p(text)
        say(f"--- {target} chars (actual={len(text)}, phonemes={len(phonemes)})")
        rows = []
        for index in range(ROUNDS):
            started = time.perf_counter()
            samples, rate = kokoro.create(
                phonemes, voice=VOICE, speed=1.0, is_phonemes=True
            )
            ms = (time.perf_counter() - started) * 1000.0
            audio_s = len(samples) / float(rate)
            rows.append({"ms": ms, "audio": audio_s, "rtf": ms / 1000.0 / audio_s})
            say(
                f"    #{index + 1}  {ms:8.1f} ms | audio {audio_s:6.2f} s | "
                f"RTF {ms / 1000.0 / audio_s:.3f}"
            )

        steady = [row["ms"] for row in rows[1:]]
        summary.append(
            {
                "chars": len(text),
                "audio": rows[0]["audio"],
                "first": rows[0]["ms"],
                "steadyMean": statistics.mean(steady),
                "steadyMin": min(steady),
                "steadyMax": max(steady),
                "rtf": statistics.mean(steady) / 1000.0 / rows[0]["audio"],
            }
        )
        say("")

    say("=== summary (first round includes CUDA graph first-run; steady = rounds 2-5) ===")
    say(f"{'chars':>6}{'audio(s)':>10}{'first(ms)':>11}{'steady(ms)':>12}{'range':>16}{'RTF':>8}")
    for item in summary:
        span = f"{item['steadyMin']:.0f}-{item['steadyMax']:.0f}"
        say(
            f"{item['chars']:>6}{item['audio']:>10.2f}{item['first']:>11.0f}"
            f"{item['steadyMean']:>12.1f}{span:>16}{item['rtf']:>8.3f}"
        )
    return 0


if __name__ == "__main__":
    sys.exit(main())
