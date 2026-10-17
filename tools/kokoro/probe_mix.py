"""中英混排 G2P 探针——只出音素串，不合成音频。

用途：确认三段事实——① 现状（en_callable=None）下英文段的产出 ② 改挂
espeak 英文 G2P 后的产出 ③ 两者与模型 vocab 的越界字符差异。

用法：
    <venv>/Scripts/python.exe tools/kokoro/probe_mix.py "文本" --home <KOKORO_HOME>
"""

import json
import os
import sys

import kokoro_env
from misaki import espeak

DEFAULT_TEXT = "任务完成了。Now loading the model, 用时 1.2 秒。"


def load_vocab():
    """读模型 vocab 字符集——用于越界检测。"""
    folder = kokoro_env.models_dir()
    path = os.path.join(folder, kokoro_env.CONFIG_NAME)
    with open(path, encoding="utf-8") as handle:
        config = json.load(handle)
    return set(config["vocab"].keys())


def show(name, phonemes, vocab):
    """打印一条形态的产出与越界字符。"""
    missing = sorted({c for c in phonemes if c not in vocab and not c.isspace()})
    print(f"[{name}]")
    print(f"  音素: {phonemes}")
    print(f"  长度: {len(phonemes)} | 越界字符: {''.join(missing) if missing else '（无）'}")
    print()


def main():
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    argv = sys.argv[1:]
    if "--home" in argv:
        os.environ["KOKORO_HOME"] = argv[argv.index("--home") + 1]
    text = argv[0] if argv and not argv[0].startswith("--") else DEFAULT_TEXT

    kokoro_env.home()
    vocab = load_vocab()
    print(f"文本: {text}")
    print()

    baseline = kokoro_env.zh.ZHG2P(version="1.1")
    show("现状 en_callable=None", baseline(text)[0], vocab)

    english = espeak.EspeakG2P("en-us")
    hooked = kokoro_env.zh.ZHG2P(version="1.1", en_callable=lambda value: english(value)[0])
    show("改挂 EspeakG2P(en-us)", hooked(text)[0], vocab)


if __name__ == "__main__":
    main()
