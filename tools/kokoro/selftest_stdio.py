"""stdio 驱动自测——起真实子进程，按严格协议发请求，校验回执。

用法：
    python selftest_stdio.py --home <Kokoro 环境目录> [--out <WAV 输出目录>]

输出：stdout 一行 JSON 汇总（pass 字段即结论）；服务侧 stderr 落 out-dir。
"""

import json
import os
import queue
import subprocess
import sys
import tempfile
import threading
import time

# 管道捕获时 Python 默认用 locale 编码（Windows = GBK）——中文结论会乱码
if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8")

HERE = os.path.dirname(os.path.abspath(__file__))
DRIVER = os.path.join(HERE, "stdio_server.py")
READY_TIMEOUT_S = 120.0
REPLY_TIMEOUT_S = 180.0


class ProtocolReader:
    """协议面读线程——带超时，避免驱动卡死时无限阻塞。"""

    def __init__(self, stream):
        self._queue = queue.Queue()
        self._thread = threading.Thread(target=self._pump, args=(stream,), daemon=True)
        self._thread.start()

    def _pump(self, stream):
        for line in stream:
            self._queue.put(line)
        self._queue.put(None)

    def read(self, timeout):
        """收一行协议 JSON——空行跳过，EOF 抛错。"""
        line = self._queue.get(timeout=timeout)
        if line is None:
            raise RuntimeError("协议面 EOF——驱动已退出")
        line = line.strip()
        if not line:
            return self.read(timeout)
        return json.loads(line)


def send(process, payload):
    """发一行协议 JSON。"""
    process.stdin.write(json.dumps(payload, ensure_ascii=False) + "\n")
    process.stdin.flush()


def arg_value(argv, name, default=""):
    """取 --name 的值。"""
    if name in argv:
        index = argv.index(name)
        if index + 1 < len(argv):
            return argv[index + 1]
    return default


def main():
    """自测主体。"""
    argv = sys.argv[1:]
    home = arg_value(argv, "--home")
    out_dir = arg_value(argv, "--out") or tempfile.mkdtemp(prefix="kokoro-selftest-")
    os.makedirs(out_dir, exist_ok=True)

    report = {"outDir": out_dir, "home": home}
    started = time.perf_counter()

    command = [sys.executable, DRIVER]
    if home:
        command += ["--home", home]

    log_path = os.path.join(out_dir, "server_stderr.log")
    with open(log_path, "w", encoding="utf-8") as log:
        process = subprocess.Popen(
            command,
            stdin=subprocess.PIPE,
            stdout=subprocess.PIPE,
            stderr=log,
            text=True,
            encoding="utf-8",
            errors="replace",
        )
        reader = ProtocolReader(process.stdout)

        try:
            report["ready"] = reader.read(READY_TIMEOUT_S)
            report["bootMs"] = round((time.perf_counter() - started) * 1000.0, 1)

            send(process, {"id": "1", "op": "ping"})
            report["ping"] = reader.read(REPLY_TIMEOUT_S)

            texts = ["任务完成了。", "轮末总结已经写好，请查看对话右侧的统计块。"]
            speech = []
            for index, text in enumerate(texts, start=2):
                out_path = os.path.join(out_dir, f"speech_{index}.wav")
                send(process, {"id": str(index), "op": "speak", "text": text, "out": out_path})
                result = reader.read(REPLY_TIMEOUT_S)
                result["exists"] = os.path.exists(out_path)
                result["bytes"] = os.path.getsize(out_path) if result["exists"] else 0
                speech.append(result)
            report["speech"] = speech

            bad_path = os.path.join(out_dir, "bad.wav")
            send(process, {"id": "9", "op": "speak", "text": "  ", "out": bad_path})
            report["emptyText"] = reader.read(REPLY_TIMEOUT_S)

            send(process, {"id": "10", "op": "quit"})
            report["quit"] = reader.read(REPLY_TIMEOUT_S)

            process.wait(timeout=10)
            report["exitCode"] = process.returncode
        except Exception as error:
            report["error"] = f"{type(error).__name__}: {error}"
            process.kill()
            report["exitCode"] = None

    report["elapsedMs"] = round((time.perf_counter() - started) * 1000.0, 1)
    report["stderrLog"] = log_path

    ok = (
        report.get("ready", {}).get("ok") is True
        and report.get("ping", {}).get("ok") is True
        and len(report.get("speech", [])) == 2
        and all(item.get("ok") and item.get("exists") for item in report.get("speech", []))
        and report.get("emptyText", {}).get("ok") is False
        and report.get("quit", {}).get("ok") is True
    )
    report["pass"] = bool(ok)

    sys.stdout.write(json.dumps(report, ensure_ascii=False, indent=2) + "\n")
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
