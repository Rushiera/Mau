"""Kokoro 环境定位与引擎加载——所有胶水脚本共用。

环境变量：
    KOKORO_HOME    环境目录（含 models/）——未配置即报错，不回落探测路径
    KOKORO_MODEL   模型文件名（默认 kokoro-v1.1-zh.onnx = fp32 + CUDA）

说明：胶水代码归仓库（本目录进 git），运行环境（venv / models）在仓库外的
纯净拉取目录——由 KOKORO_HOME 指向。
"""

import os

import espeakng_loader
import onnxruntime as ort
from kokoro_onnx import Kokoro
from misaki import espeak, zh

os.environ["PHONEMIZER_ESPEAK_LIBRARY"] = str(espeakng_loader.get_library_path())
os.environ["ESPEAK_DATA_PATH"] = str(espeakng_loader.get_data_path())

# onnxruntime 默认把图优化警告刷到 stderr——压到只报错
ort.set_default_logger_severity(3)

# 必须 fp32——fp16 图与 CUDA EP 不兼容，长文本必崩（见 README 模型选择）
DEFAULT_MODEL = "kokoro-v1.1-zh.onnx"
VOICES_NAME = "voices-v1.1-zh.bin"
CONFIG_NAME = "kokoro-v1.1-zh-config.json"


def home():
    """返回环境目录——未配置或不存在即抛错（不静默回落）。"""
    value = os.environ.get("KOKORO_HOME", "").strip()
    if not value:
        raise RuntimeError("KOKORO_HOME 未配置——指向 Kokoro 环境目录（含 models/）")
    if not os.path.isdir(value):
        raise RuntimeError(f"KOKORO_HOME 不存在：{value}")
    return value


def models_dir():
    """返回模型目录。"""
    return os.path.join(home(), "models")


def apply_cli(argv):
    """解析命令行开关——`--home` / `--model` / `--device`，返回 device（空=自动）。

    失败可见：`--device` 取值非法即抛错，不静默回落。
    """
    def value_of(flag):
        index = argv.index(flag)
        return argv[index + 1] if index + 1 < len(argv) else ""

    if "--home" in argv:
        os.environ["KOKORO_HOME"] = value_of("--home")
    if "--model" in argv:
        os.environ["KOKORO_MODEL"] = value_of("--model")

    device = value_of("--device") if "--device" in argv else ""
    if device and device not in ("cpu", "cuda"):
        raise RuntimeError("--device 取值非法：" + device + "（仅支持 cpu / cuda）")
    return device


def prepare_gpu(device=None):
    """挂载 NVIDIA dll 搜索路径并钉死 EP（避开 TensorRT 探测失败）。

    device：空=自动（CUDA 可用即用）· `cuda`=强制 CUDA（不可用即抛错）· `cpu`=强制 CPU。
    """
    if device == "cpu":
        os.environ["ONNX_PROVIDER"] = "CPUExecutionProvider"
        return
    if hasattr(ort, "preload_dlls"):
        ort.preload_dlls()
    if "CUDAExecutionProvider" in ort.get_available_providers():
        os.environ["ONNX_PROVIDER"] = "CUDAExecutionProvider"
        return
    if device == "cuda":
        raise RuntimeError(
            "--device cuda 已指定，但本环境未提供 CUDAExecutionProvider"
            "（检查 onnxruntime-gpu 与 CUDA 运行库）")


def load_engine(model_name=None, device=None):
    """加载 G2P 与模型，返回 (g2p, kokoro, model_name)。"""
    name = model_name or os.environ.get("KOKORO_MODEL", DEFAULT_MODEL)
    folder = models_dir()
    config = os.path.join(folder, CONFIG_NAME)
    prepare_gpu(device)
    # 英文段走 espeak G2P——无 en_callable 时 misaki 把英文整段替换成 unk（'❓'），
    # 该字符不在模型 vocab 内 → tokenizer 静默丢弃 → 英文无声（判例 2026-10-15）。
    # EspeakG2P 返回 (音素, None) 元组，取 [0] 适配 zh.ZHG2P 的 en_callable 签名。
    english = espeak.EspeakG2P("en-us")
    g2p = zh.ZHG2P(version="1.1", en_callable=lambda value: english(value)[0])
    kokoro = Kokoro(
        os.path.join(folder, name),
        os.path.join(folder, VOICES_NAME),
        vocab_config=config if os.path.exists(config) else None,
    )
    return g2p, kokoro, name
