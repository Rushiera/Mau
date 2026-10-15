"""Kokoro stdio 驱动——严格协议的主通道。

协议：一行 JSON 进 / 一行 JSON 出。

    → {"id":"1","op":"speak","text":"任务完成了。","voice":"zf_001","speed":1.0,"out":"<wav 路径>"}
    ← {"id":"1","ok":true,"out":"<wav 路径>","ms":276.0,"audioSeconds":1.46,"sampleRate":24000}
    → {"id":"2","op":"ping"}
    ← {"id":"2","ok":true,"model":"...","providers":["CUDAExecutionProvider",...]}
    → {"id":"3","op":"quit"}
    ← {"id":"3","ok":true,"quit":true}

启动时先输出一行 ready：

    ← {"ok":true,"event":"ready","loadMs":2600.0}

🔴 stdout 独占——启动即把 sys.stdout 改指 stderr，协议行只走 _PROTOCOL。
   任何第三方库的 print / 警告都不会污染协议面（严格协议，见
   design-ch4-pytools.md §5.1）。

用法：
    python stdio_server.py [--home <Kokoro 环境目录>]
"""

import json
import os
import sys
import time

# 🔴 协议面编码固定 UTF-8——管道捕获时 Python 默认用 locale 编码（Windows = GBK）
if hasattr(sys.stdin, "reconfigure"):
    # 🔴 utf-8-sig——读侧自动剥 BOM（.NET StreamWriter 首次写入会带 BOM：EncoderShouldEmitUTF8Identifier）
    sys.stdin.reconfigure(encoding="utf-8-sig", errors="replace")

_PROTOCOL = sys.stdout
_PROTOCOL.reconfigure(encoding="utf-8", newline="\n")

# 🔴 stdout 独占：协议行走 _PROTOCOL，其余（含 print）一律 stderr
sys.stdout = sys.stderr

import soundfile as sf  # noqa: E402  （须在 stdout 置换后导入）

import kokoro_env  # noqa: E402


def emit(payload):
    """输出一行协议 JSON 并立即刷新。"""
    _PROTOCOL.write(json.dumps(payload, ensure_ascii=False) + "\n")
    _PROTOCOL.flush()


def handle(request, engine):
    """处理一条请求，返回回执 dict。"""
    op = request.get("op", "speak")

    if op == "ping":
        _, kokoro, model = engine
        return {"ok": True, "model": model, "providers": list(kokoro.sess.get_providers())}

    if op == "quit":
        return {"ok": True, "quit": True}

    if op != "speak":
        return {"ok": False, "error": f"未知 op：{op}"}

    text = (request.get("text") or "").strip()
    if not text:
        return {"ok": False, "error": "text 为空"}

    out_path = (request.get("out") or "").strip()
    if not out_path:
        return {"ok": False, "error": "out 为空"}

    voice = request.get("voice") or "zf_001"
    speed = float(request.get("speed", 1.0))

    g2p, kokoro, _ = engine
    started = time.perf_counter()
    phonemes, _ = g2p(text)
    samples, rate = kokoro.create(phonemes, voice=voice, speed=speed, is_phonemes=True)
    sf.write(out_path, samples, rate)
    ms = (time.perf_counter() - started) * 1000.0

    return {
        "ok": True,
        "out": out_path,
        "ms": round(ms, 1),
        "audioSeconds": round(len(samples) / float(rate), 3),
        "sampleRate": rate,
    }


def main():
    """主循环——读到 EOF 或 quit 即退出。"""
    argv = sys.argv[1:]
    if "--home" in argv:
        os.environ["KOKORO_HOME"] = argv[argv.index("--home") + 1]

    started = time.perf_counter()
    engine = kokoro_env.load_engine()
    emit(
        {
            "ok": True,
            "event": "ready",
            "loadMs": round((time.perf_counter() - started) * 1000.0, 1),
        }
    )

    for line in sys.stdin:
        line = line.strip()
        if not line:
            continue

        try:
            request = json.loads(line)
        except ValueError as error:
            emit({"ok": False, "error": f"协议解析失败：{error}"})
            continue

        request_id = request.get("id")
        try:
            result = handle(request, engine)
        except Exception as error:  # 第三方库异常面宽，统一转回执
            emit({"id": request_id, "ok": False, "error": f"{type(error).__name__}: {error}"})
            continue

        result["id"] = request_id
        emit(result)
        if result.get("quit"):
            break

    return 0


if __name__ == "__main__":
    sys.exit(main())
