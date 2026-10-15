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
from misaki import zh

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


def prepare_gpu():
    """挂载 NVIDIA dll 搜索路径；CUDA 可用时钉死 EP（避开 TensorRT 探测失败）。"""
    if hasattr(ort, "preload_dlls"):
        ort.preload_dlls()
    if "CUDAExecutionProvider" in ort.get_available_providers():
        os.environ["ONNX_PROVIDER"] = "CUDAExecutionProvider"


def load_engine(model_name=None):
    """加载 G2P 与模型，返回 (g2p, kokoro, model_name)。"""
    name = model_name or os.environ.get("KOKORO_MODEL", DEFAULT_MODEL)
    folder = models_dir()
    config = os.path.join(folder, CONFIG_NAME)
    prepare_gpu()
    g2p = zh.ZHG2P(version="1.1")
    kokoro = Kokoro(
        os.path.join(folder, name),
        os.path.join(folder, VOICES_NAME),
        vocab_config=config if os.path.exists(config) else None,
    )
    return g2p, kokoro, name
