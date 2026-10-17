# kokoro —— TTS 服务胶水（仓库侧）

Kokoro 中文语音合成的**胶水代码**——stdio 驱动 / 单次合成 / 基准与自测。
设计规格 → `Project/CH4/design-ch4-pytools.md`。

---

## 一、边界（重要）

| 面 | 位置 | 说明 |
|:--|:--|:--|
| **胶水代码**（本目录） | `mau:tools/kokoro/`——**进 git** | 协议与驱动必须版本可控 |
| **运行环境**（venv / models） | 仓库外**纯净拉取目录** | 由 `KOKORO_HOME` 指向；**不含任何自写代码** |

外部目录的重建与升级都是**纯拉取操作**——自写物全在本目录，不涉及迁移。

> 文档分工：**操作面**（协议 / 启动开关 / 文件清单 / 依赖 / 部署步骤 / 用法）在本文件；**知识台账**（选型论证 / 本体与许可 / 性能实测 / 装机实录 / 宿主集成契约）→ `KNOWLEDGE.md`。

## 二、文件

| 文件 | 用途 |
|:--|:--|
| `kokoro_env.py` | 环境定位（`KOKORO_HOME`）+ GPU 预载 + 引擎加载 + 启动开关解析（`apply_cli`）——所有脚本共用 |
| `stdio_server.py` | **stdio 驱动（主通道）**——严格协议，常驻；认 `--model` / `--device` 启动开关（→ §四·B） |
| `synth.py` | 单次合成（调试 / 低频调用） |
| `selftest_stdio.py` | stdio 协议自测——起真实子进程跑完整往返 |
| `bench.py` | 性能基准——三档文本多轮 + RTF 汇总 |
| `perf_test.py` | GPU 性能测试——精确 10/30/100 字 × 5 轮，区分首次与稳态 |
| `probe_mix.py` | **中英混排 G2P 探针**——只打音素串（不合成音频）：对比 `en_callable` 接线前后的产出与 vocab 越界字符 |
| `make_samples.py` | **音色样本批量生成**——同一段文本 × 多音色（`--list` 列清单 / `--voices a,b` 指定 / `--all` 全量） |
| `make_variants.py` | **标点变体探针**——同一句话 × 不同标点写法，输出停顿段分布 / 基频走向 / 时长对照表 |

## 三、环境变量

| 变量 | 说明 |
|:--|:--|
| `KOKORO_HOME` | 环境目录（含 `models/`）——**必填**，未设置即报错（不回落探测路径） |
| `KOKORO_MODEL` | 模型文件名；默认 `kokoro-v1.1-zh.onnx`（**fp32 + CUDA**） |

所有脚本支持 `--home <目录>` 覆盖 `KOKORO_HOME`（便于单条命令行调用）。

**服务侧另有启动开关**（`--model` / `--device`）——由宿主 `py.service.kokoro.args` 原样传入，详见 §四·B。

## 四、stdio 协议（严格）

一行 JSON 进 / 一行 JSON 出：

```
→ {"id":"1","op":"speak","text":"任务完成了。","voice":"zf_001","speed":1.0,"out":"<wav 路径>"}
← {"id":"1","ok":true,"out":"<wav 路径>","ms":276.0,"audioSeconds":1.46,"sampleRate":24000}

→ {"id":"2","op":"ping"}
← {"id":"2","ok":true,"model":"kokoro-v1.1-zh.onnx","providers":["CUDAExecutionProvider","CPUExecutionProvider"]}

→ {"id":"3","op":"quit"}
← {"id":"3","ok":true,"quit":true}
```

启动先出一行 ready：

```
← {"ok":true,"event":"ready","loadMs":2600.0}
```

**op 面**：`speak`（默认，text 空即拒）· `ping` · `quit`。

🔴 **三条严格性铁律：**

1. **stdout 只出协议行**——驱动启动即把 `sys.stdout` 改指 stderr，第三方库的 print / 警告不会污染协议面。
2. **协议面编码固定 UTF-8**——`stdin` / `stdout` 双 `reconfigure`，stdout 另固定 `newline="\n"`。
3. **解析失败出声**——错误以 `{"ok":false,"error":"…"}` 回执，不静默跳过（规格 §5.1）。

## 四·B 启动开关（服务侧）

`stdio_server.py` 认三个开关——宿主 `py.service.kokoro.args` 写的就是这里（原样拼进命令行）：

| 开关 | 取值 | 作用 |
|:--|:--|:--|
| `--home <目录>` | 环境目录 | 覆盖 `KOKORO_HOME`（宿主按配置自动拼，无需写进 args） |
| `--model <文件名>` | `models/` 下的文件名 | 覆盖 `KOKORO_MODEL`——**换模型档位**（fp32 / fp16 / int8，须文件已存在） |
| `--device cpu\|cuda` | `cpu` / `cuda` | 强制执行后端——**跨机差异用**（弱机走 CPU、强机走 CUDA）；取值非法即报错；`cuda` 而环境不支持 CUDA 同样报错（不静默回落 CPU） |

**args 示例**：`--device cuda`（显式钉死 GPU，本机默认档）· `--model kokoro-v1.1-zh-fp16.onnx --device cpu`（弱机兜底档）

> 🔴 开关是**启动期**参数（进程拉起时消费一次），改 args 后需重启服务生效；**每次合成**要调的东西（音色 / 语速）走工具参数，不在这里。

## 五、引用的包

| 包 | 作用 | 备注 |
|:--|:--|:--|
| `kokoro-onnx` | 推理引擎封装 | 权重加载 + `create()` |
| `misaki-fork[zh]` | 中文 G2P（字 → 音素） | `zh.ZHG2P(version="1.1")` |
| `espeakng-loader` | espeak-ng 原生库定位 | G2P 的非专用语言回退 |
| `soundfile` | WAV 写出 | 底层 libsndfile |
| `onnxruntime-gpu` | 推理运行时（CUDA EP） | **版本 1.31.0** |
| `nvidia-cublas` / `nvidia-cuda-runtime` / `nvidia-cudnn-cu13` | CUDA 运行库（pip 分发） | 约 1.1 GB |
| `fastapi` / `uvicorn` | （仅旧 HTTP 调试面） | stdio 主通道**不需要** |

**许分级**：权重 Apache-2.0（可商用）· 推理代码 MIT/Apache-2.0 · espeak-ng + phonemizer **GPL-3.0**（仅分发触发，本机自用不触发）。

## 六、新机部署（重建环境）

```bat
:: 1. 建环境（目标目录即 KOKORO_HOME，例如 C:\...\WorkSpace\Kokoro）
python -m venv .venv

:: 2. 基础依赖
.venv\Scripts\python -m pip install kokoro-onnx "misaki-fork[zh]" soundfile

:: 3. GPU 支持（约 1.1 GB）
.venv\Scripts\python -m pip install onnxruntime-gpu==1.31.0
.venv\Scripts\python -m pip install nvidia-cublas nvidia-cuda-runtime
.venv\Scripts\python -m pip install nvidia-cudnn-cu13 --extra-index-url https://pypi.nvidia.com
```

> ⚠️ **两个包名坑（命名规则相反）**：`nvidia-cudnn` 在 PyPI 是**占位包**，真实包名是 `nvidia-cudnn-cu13`；而 `nvidia-cublas-cu13` 反而报废弃，要用**无后缀**的 `nvidia-cublas`。

🔴 **顺序铁律——GPU 版必须最后装**：`kokoro-onnx` 的依赖写的是 `onnxruntime>=1.20.1`，而 `onnxruntime-gpu` 的发行名**不满足**该 requirement——只要它后装，就会把 CPU 版装回来并**覆盖同名模块文件**（两发行包写同一目录）。判据 = `ping` 回执的 `providers` 含 `CUDAExecutionProvider`；被覆盖时 `--force-reinstall onnxruntime-gpu==1.31.0` 扳回。**卸载 / 重装前先停服务**——服务进程持有 `capi/*.dll` 句柄，会留下 `~api` / `~umpy*` 残留目录。

**源（国内网络实测 2026-10-17）**

| 面 | 可用通道 |
|:--|:--|
| PyPI | 清华源——`-i https://pypi.tuna.tsinghua.edu.cn/simple`（15–22 MB/s） |
| nvidia 包 | 追加 `--extra-index-url https://pypi.nvidia.com` |
| 模型（GitHub release） | 直连失败 → 代理前缀 `https://ghfast.top/` + 原地址；备选 ModelScope（30–60 MB/s） |
| `config.json`（HF） | 直连失败 → `https://hf-mirror.com/hexgrad/Kokoro-82M-v1.1-zh/raw/main/config.json` |

> 装机过程实况（含包版本清单、验证数据、冷启差异）→ `KNOWLEDGE.md` §四。

**4. 模型文件**（放入 `models/`）：

| 文件 | 说明 |
|:--|:--|
| `kokoro-v1.1-zh.onnx` | **fp32 · 310 MB——推荐** |
| `voices-v1.1-zh.bin` | 音色库（103 个：`zf_*` 女声 / `zm_*` 男声） |
| `kokoro-v1.1-zh-config.json` | 词表配置 |

**5. 验证**：

```bat
:: 协议自测（结论看末行 JSON 的 pass 字段）
.venv\Scripts\python.exe <仓库>\tools\kokoro\selftest_stdio.py --home <KOKORO_HOME>
```

**磁盘占用约 2.5 GB**（venv 含 CUDA 运行库 ~1.5 GB + 模型 575 MB）。

### 模型档位（只两种组合可用）

| 组合 | 模型 | 后端 | 状态 |
|:--|:--|:--|:--|
| **推荐** | fp32（310 MB） | **CUDA** | ✅ 长短文本全稳 |
| 兜底 | fp16（156 MB） | CPU | ✅ 稳，但慢 4–5× |
| ❌ 禁 | fp16 | CUDA | **崩**——fp16 图含 CPU 无 kernel 的节点 |
| ❌ 弃 | int8（109 MB） | CPU | 最慢（x86 缺优化内核） |

> 反直觉但实测确凿：**int8 < fp16 < fp32**（CPU 侧），而 **GPU 侧只吃 fp32**。

## 七、踩坑记录

| # | 坑 | 现象 | 处置 |
|:--:|:--|:--|:--|
| 1 | **GPU 预载缺失** | 静默回落 int8 + CPU，RTF **1.44**（比实时还慢） | 代码侧须 `ort.preload_dlls()`（挂 nvidia pip 包的 dll 路径）——已入 `kokoro_env.prepare_gpu` |
| 2 | **协议面编码** | 中文以 GBK 写出、父侧按 UTF-8 读 → `UnicodeDecodeError: 0xbf` | 两侧同设 UTF-8（见 §四 铁律 2） |
| 3 | **stdout 噪声** | 第三方库 print / 警告混入协议面 → 解析崩 | 驱动启动即独占 stdout（见 §四 铁律 1） |
| 4 | **TensorRT 探测失败** | `Failed to create TensorrtExecutionProvider` 噪声刷屏 | `prepare_gpu` 显式钉死 `CUDAExecutionProvider` |
| 5 | **参数名撞 PS 拦截** | `--out-dir` 含 `dir` → `PS_LIST_FORBIDDEN` | 改用 `--out`；宿主调用走 `Process.Start` 不受影响（A255 起拦截改段首命令位判定，`--out-dir` 已不再误拦） |
| 6 | **中英混排英文无声** | 英文段整段不出声——`en_callable=None` 时 misaki 把英文段替换为 `unk`（`❓`），该字符不在模型 vocab 内被静默丢弃 | `kokoro_env.load_engine` 挂 `EspeakG2P("en-us")` 作 `en_callable`（取元组 `[0]` 适配签名）；核对脚本 `probe_mix.py`（改后 0 越界） |
| 7 | **args 静默忽略（假绿）** | 宿主 `py.service.*.args` 会原样拼进命令行，但服务脚本原先只解析 `--home` → 配了参数**不生效也不报错** | 补 `--model` / `--device` 两个启动开关，解析统一归 `kokoro_env.apply_cli`；取值非法即抛错、`cuda` 不可用亦抛错（不静默回落 CPU）——§四·B |

## 八、用法

```bat
:: 协议自测（起子进程跑完整往返——结论看末行 JSON 的 pass 字段）
<venv>\Scripts\python.exe tools\kokoro\selftest_stdio.py --home <KOKORO_HOME>

:: 单次合成
<venv>\Scripts\python.exe tools\kokoro\synth.py "要合成的文本" out.wav zf_001 --home <KOKORO_HOME>

:: 性能基准
<venv>\Scripts\python.exe tools\kokoro\bench.py zf_001 --home <KOKORO_HOME>

:: 中英混排探针（只打音素串，不合成）
<venv>\Scripts\python.exe tools\kokoro\probe_mix.py "任务完成了。Now loading the model, 用时 1.2 秒。" --home <KOKORO_HOME>

:: 音色样本批量生成（同文本 × 多音色）
<venv>\Scripts\python.exe tools\kokoro\make_samples.py --list --home <KOKORO_HOME>
<venv>\Scripts\python.exe tools\kokoro\make_samples.py --out <目录> --voices zf_001,zm_013 --home <KOKORO_HOME>
<venv>\Scripts\python.exe tools\kokoro\make_samples.py --out <目录> --all --home <KOKORO_HOME>

:: 常驻驱动（由宿主 IPyService 拉起——配置里的 args 就拼在这条命令后面）
<venv>\Scripts\python.exe tools\kokoro\stdio_server.py --home <KOKORO_HOME>

:: 常驻驱动 + 启动开关（--model / --device 用法见 §四·B）
<venv>\Scripts\python.exe tools\kokoro\stdio_server.py --device cuda --home <KOKORO_HOME>
<venv>\Scripts\python.exe tools\kokoro\stdio_server.py --model kokoro-v1.1-zh-fp16.onnx --device cpu --home <KOKORO_HOME>
```

> 解释器用**外部 venv** 的 python（依赖 kokoro-onnx / onnxruntime-gpu / soundfile），
> **不是系统 Python**——系统 Python 无 CUDA 栈。

## 九、实测基线（RTX 3080 / i7-12700F / 32 GB · 2026-10-15）

| 项 | 数值 |
|:--|:--|
| 冷启动（加载模型 + G2P） | **2.52 s** |
| 常驻内存 | 约 1.2 GB |
| 首次合成（含 CUDA 图首跑） | 1.17 s |
| 稳态 RTF（fp32 + CUDA） | **0.066**（4.5 s 音频 → 0.30 s） |
| 协议自测端到端 | 5.68 s（ready + ping + 2 次合成 + 负例 + quit） |
