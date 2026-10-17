"""批量音色样本生成——同一段文本 × 多音色，产物落指定目录。

用法：
    <venv>/Scripts/python.exe tools/kokoro/make_samples.py --list --home <KOKORO_HOME>
    <venv>/Scripts/python.exe tools/kokoro/make_samples.py --out <目录> --home <KOKORO_HOME>
    <venv>/Scripts/python.exe tools/kokoro/make_samples.py --out <目录> --voices zf_001,zm_001 --home <KOKORO_HOME>
    <venv>/Scripts/python.exe tools/kokoro/make_samples.py --out <目录> --all --home <KOKORO_HOME>

说明：
    --list          只列音色名（不合成）
    --out <目录>    产物目录（缺省则退化为 --list 行为）
    --voices a,b,c  指定音色（缺省 = 全部中文音色：zf_* / zm_*）
    --all           全部音色
    --text "…"      合成文本（缺省为中英混排样例）
"""

import os
import sys

import kokoro_env

DEFAULT_TEXT = "任务完成了。Now loading the model, 用时 1.2 秒。"


def arg_value(argv, flag, fallback=None):
    """取命令行参数值——无则回落。"""
    if flag in argv:
        index = argv.index(flag)
        if index + 1 < len(argv):
            return argv[index + 1]
    return fallback


def main():
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    argv = sys.argv[1:]

    os.environ["KOKORO_HOME"] = arg_value(argv, "--home", os.environ.get("KOKORO_HOME", ""))
    text = arg_value(argv, "--text", DEFAULT_TEXT)
    out_dir = arg_value(argv, "--out")

    g2p, kokoro, model = kokoro_env.load_engine()
    voices = kokoro.get_voices()
    print(f"模型: {model} | 音色总数: {len(voices)}")

    if "--list" in argv or out_dir is None:
        for name in voices:
            print(name)
        return

    if "--all" in argv:
        selected = list(voices)
    else:
        raw = arg_value(argv, "--voices")
        if raw:
            selected = [item.strip() for item in raw.split(",") if item.strip()]
        else:
            selected = [name for name in voices if name.startswith(("zf_", "zm_"))]

    if not selected:
        print("没有可合成的音色——检查 --voices 取值")
        return

    import soundfile as sf

    phonemes, _ = g2p(text)

    os.makedirs(out_dir, exist_ok=True)
    print(f"文本: {text}")
    print(f"产物目录: {out_dir}")

    for name in selected:
        path = os.path.join(out_dir, f"{name}.wav")
        try:
            samples, rate = kokoro.create(phonemes, voice=name, speed=1.0, is_phonemes=True)
        except Exception as error:
            print(f"{name}\t失败: {error}")
            continue
        sf.write(path, samples, rate)
        print(f"{name}\t{len(samples) / float(rate):.2f}s\t{path}")


if __name__ == "__main__":
    main()
