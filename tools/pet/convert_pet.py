"""pet 资产转换：GIF（500x500，1-bit 透明，20ms/帧）→ 统一画布裁剪 → 边缘色外扩 → 缩放 → 边缘羽化 → 动画 WebP。

边缘处理（莎 2026-09-17 定）——**圆角矩形 SDF**（CORNER_R=0 时自然退化为矩形）：
- EDGE_PX   四边羽化宽度（输出尺寸口径）——四边内缩这么多做平滑淡出
- CORNER_R  四角圆角半径——0 = 不圆角（保留素材自带外形）
- 不透明区 = 内缩 EDGE_PX 的矩形；衰减 = 1 - smoothstep(距边界距离 / EDGE_PX)
- 内部恒为 1 → 主体零影响

⚠ 历史：曾把圆角半径叠加进边距（内缩 d+r ≈ 68px），不透明区只剩 26%——已废弃。
⚠ 合并帧：WebP 会合并完全相同的帧并把时长累加到保留帧上（总时长不变）——
   故「帧数 × 每帧时长 ≠ 总时长」，测总时长必须解 ANMF（见 webp_total_ms）。

产物两写：仓库源码区（git 资产）+ 仓库产物区（public/app/html——防下次部署前出现清单/素材错配）。
运行区（MauOut）不再直写——统一由 `mau-setup mode=sync-html` 同步。源素材只读不动。

用法：
    python tools/pet/convert_pet.py                          # 处理默认皮肤（greencat）全部素材
    python tools/pet/convert_pet.py loop-think before-idle   # 只处理指定素材
    python tools/pet/convert_pet.py --skin deepdeep          # 换皮肤烘焙（写现有平铺布局）
"""
import os
import json
import time
import shutil
import argparse
import numpy as np
from PIL import Image, ImageSequence
from scipy import ndimage

HERE = os.path.dirname(os.path.abspath(__file__))        # tools/pet
REPO = os.path.dirname(os.path.dirname(HERE))            # tools/pet → tools → 仓库根

BOX = (0, 53, 500, 500)      # 全局联合 bbox（所有帧 alpha>0 并集）——统一画布保证切换不跳
                             # 素材画布不同则改这里（暂全局；多皮肤落地时可提为 per-skin）
OUT_H = 256                  # 输出高度（显示高度约 160px 的 1.6 倍）
DURATION = 20                # 每帧 20ms（源 50fps）
QUALITY = 78                 # 有损 WebP 质量
METHOD = 4                   # 编码方法——4 与 6 体积持平（实测 0.44 vs 0.43MB）但快 100 倍
EDGE_PX = 4                  # 四边羽化宽度
CORNER_R = 0                 # 四角圆角半径（0 = 不圆角）
# 接缝截断——砍掉 before 尾部（与后续 loop 首帧姿态不接的部分）；帧号由 match_pet_seam.py 算出。
# ⚠ 换素材后帧号会失效，须重跑 match_pet_seam.py 复算。
TRIM = {}   # 接缝截断——{素材名: 帧号}；当前为空（before-tool 素材已废弃，其余 pair 走溶解）

_FACTOR_CACHE = {}


def parse_args():
    """命令行——素材名（位置参数）+ --skin / --src / --repo。"""
    ap = argparse.ArgumentParser(description="pet 资产转换：GIF → 动画 WebP + 清单")
    ap.add_argument("names", nargs="*", help="素材名（缺省 = 全部 loop-*/before-*/after-*）")
    ap.add_argument("--skin", default="greencat", help="皮肤名（src 下子目录；默认 greencat）")
    ap.add_argument("--src", default=os.path.join(HERE, "src"), help="源素材根目录（默认 tools/pet/src）")
    ap.add_argument("--repo", default=REPO, help="仓库根（默认 = 脚本上三级）")
    return ap.parse_args()


ARGS = parse_args()
SRC = os.path.join(ARGS.src, ARGS.skin)                              # 源素材（按皮肤分子目录）
DST_SRC = os.path.join(ARGS.repo, "CatHome4", "html", "pet")         # 仓库源码区（git 资产）
DST_JS = os.path.join(ARGS.repo, "CatHome4", "html", "js")
DST_PUB = os.path.join(ARGS.repo, "public", "app", "html", "pet")    # 仓库产物区
DST_PUB_JS = os.path.join(ARGS.repo, "public", "app", "html", "js")


def edge_factor(w, h, edge, r):
    """边缘衰减系数——内缩 edge 的圆角矩形（圆角半径 r）；内部恒为 1，边界处为 0。"""
    key = (w, h, edge, r)
    if key in _FACTOR_CACHE:
        return _FACTOR_CACHE[key]
    ys = np.arange(h).reshape(h, 1).astype(np.float32)
    xs = np.arange(w).reshape(1, w).astype(np.float32)
    bx = (w - 1) / 2.0 - edge
    by = (h - 1) / 2.0 - edge
    qx = np.abs(xs - (w - 1) / 2.0) - (bx - r)
    qy = np.abs(ys - (h - 1) / 2.0) - (by - r)
    sd = np.hypot(np.maximum(qx, 0.0), np.maximum(qy, 0.0)) + np.minimum(np.maximum(qx, qy), 0.0) - r
    t = np.clip(sd / float(edge), 0.0, 1.0)
    f = 1.0 - (t * t * (3.0 - 2.0 * t))
    _FACTOR_CACHE[key] = f
    return f


def apply_feather(img):
    """按边缘衰减系数淡出 alpha（RGB 不动——主体与内部内容零变化）。"""
    arr = np.array(img).astype(np.float32)
    arr[:, :, 3] = arr[:, :, 3] * edge_factor(arr.shape[1], arr.shape[0], EDGE_PX, CORNER_R)
    return Image.fromarray(arr.astype(np.uint8), "RGBA")


def convert(name):
    t0 = time.time()
    im = Image.open(os.path.join(SRC, name))
    frames = []
    for page in ImageSequence.Iterator(im):
        fr = np.array(page.convert("RGBA"))[BOX[1]:BOX[3], BOX[0]:BOX[2]]
        alpha = fr[:, :, 3]
        rgb = fr[:, :, :3].copy()
        mask = alpha == 0
        if mask.any() and not mask.all():
            # 透明区取最近可见像素颜色——防缩放时黑边（RGB 在透明区为 0）
            idx = ndimage.distance_transform_edt(mask, return_distances=False, return_indices=True)
            rgb = rgb[idx[0], idx[1]]
        frames.append(np.dstack([rgb, alpha]))
    key = os.path.splitext(name)[0]
    if key in TRIM:
        # 接缝截断——保留 [0..TRIM[key]]，砍掉尾部（姿态与后续 loop 不接的部分）
        frames = frames[:TRIM[key] + 1]
    h, w = frames[0].shape[:2]
    tw = int(round(w * (OUT_H / float(h))))
    out = [apply_feather(Image.fromarray(fr, "RGBA").resize((tw, OUT_H), Image.LANCZOS)) for fr in frames]
    dst = os.path.join(DST_SRC, os.path.splitext(name)[0] + ".webp")
    out[0].save(dst, format="WEBP", save_all=True, append_images=out[1:],
                duration=DURATION, loop=0, quality=QUALITY, method=METHOD)
    # 只写源码区 + 产物区（运行区交 `mau-setup mode=sync-html`）
    shutil.copyfile(dst, os.path.join(DST_PUB, os.path.basename(dst)))
    print("%-24s %3d frames  %dx%d  %5.2f MB  %4.1fs" % (
        name, len(out), tw, OUT_H, os.path.getsize(dst) / 1048576.0, time.time() - t0))


def webp_total_ms(path):
    """解 RIFF/ANMF 取真实总时长。

    关键：WebP 会合并重复帧，并把被合并帧的时长**累加到保留帧上**（总时长不变、帧数变少）。
    故总时长 ≠ 帧数 × DURATION——before-idle-sleep 即 75 帧 / 1980ms（源 GIF 99 帧 / 1980ms）。
    """
    import struct
    with open(path, "rb") as fh:
        data = fh.read()
    if data[:4] != b"RIFF" or data[8:12] != b"WEBP":
        return 0
    total = 0
    i = 12
    while i + 8 <= len(data):
        tag = data[i:i + 4]
        size = struct.unpack("<I", data[i + 4:i + 8])[0]
        if tag == b"ANMF":
            payload = data[i + 8:i + 8 + size]
            total = total + (payload[12] | (payload[13] << 8) | (payload[14] << 16))
        i = i + 8 + size + (size & 1)
    return total


def write_manifest():
    """素材清单——每张产物取 md5 前 8 位（前端缓存失效键：内容或名字一变，URL 即变）。
    产物落 js/pet-manifest.js（全局变量）——走 /js 路由拿 application/javascript；
    不用 .json 是为了不被 /pet 路由标成 image/webp（MIME 与实际不符）。"""
    import hashlib
    items = {}
    for f in sorted(os.listdir(DST_SRC)):
        if not f.lower().endswith(".webp"):
            continue
        p = os.path.join(DST_SRC, f)
        with open(p, "rb") as fh:
            h = hashlib.md5(fh.read()).hexdigest()[:8]
        items[os.path.splitext(f)[0]] = {"h": h, "ms": webp_total_ms(p)}
    body = json.dumps(items, ensure_ascii=False, indent=4, sort_keys=True)
    text = "// 自动生成（tools/pet/convert_pet.py）——勿手改\nvar chatPetManifestData = " + body + ";\n"
    for dst in (DST_JS, DST_PUB_JS):
        with open(os.path.join(dst, "pet-manifest.js"), "w", encoding="utf-8") as fh:
            fh.write(text)
    print("manifest %d 项 → js/pet-manifest.js" % len(items))


def main():
    os.makedirs(DST_SRC, exist_ok=True)
    os.makedirs(DST_PUB, exist_ok=True)
    os.makedirs(DST_PUB_JS, exist_ok=True)
    names = ARGS.names or sorted(
        f for f in os.listdir(SRC)
        if f.lower().endswith(".gif") and f.startswith(("loop-", "before-", "after-")))
    for n in names:
        convert(n)
    write_manifest()


if __name__ == "__main__":
    main()
