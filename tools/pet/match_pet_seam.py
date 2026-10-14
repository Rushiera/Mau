"""before→loop 接缝匹配——找 before 中与 loop 首帧最接近的一帧（截断点候选）。

原理：before 播到第 k 帧后瞬切 loop[0]。若 before[k] ≈ loop[0]，视觉连续；
否则跳帧。逐帧算与 loop[0] 的差异，取最小者作候选截断点。

度量：RGB（透明区先做边缘色外扩，避免黑区干扰）+ alpha 各占一半。
输出：数值曲线 + 三联对照图（候选帧 | loop 首帧 | 差异热力）→ CatTemp。

只读，不改素材。用法：
    python tools/pet/match_pet_seam.py
    python tools/pet/match_pet_seam.py --pet <webp目录> --out <对照图目录>
"""
import os
import argparse
import numpy as np
from PIL import Image, ImageSequence
from scipy import ndimage

HERE = os.path.dirname(os.path.abspath(__file__))        # tools/pet
REPO = os.path.dirname(os.path.dirname(HERE))            # 仓库根

DEFAULT_PET = os.path.join(REPO, "CatHome4", "html", "pet")
DEFAULT_OUT = os.path.join(REPO, "CatTemp")

PAIRS = [
    ("before-idle-sleep", "loop-idle-sleep"),
    ("before-idle-hook", "loop-idle-hook"),
    ("before-idle", "loop-idle1"),
]


def parse_args():
    """命令行——webp 目录与对照图输出目录。"""
    ap = argparse.ArgumentParser(description="before→loop 接缝帧匹配（只读）")
    ap.add_argument("--pet", default=DEFAULT_PET, help="webp 素材目录（默认 CatHome4/html/pet）")
    ap.add_argument("--out", default=DEFAULT_OUT, help="对照图输出目录（默认 CatTemp）")
    return ap.parse_args()


ARGS = parse_args()
PET = ARGS.pet
OUT = ARGS.out


def load_frames(name):
    """读 webp 全部帧 → RGBA float32 列表。"""
    im = Image.open(os.path.join(PET, name + ".webp"))
    out = []
    for page in ImageSequence.Iterator(im):
        out.append(np.array(page.convert("RGBA")).astype(np.float32))
    return out


def edge_extend(rgb, alpha01):
    """透明区取最近可见像素色——差异计算不被黑区放大。"""
    mask = alpha01 < 0.03
    if mask.any() and not mask.all():
        idx = ndimage.distance_transform_edt(mask, return_distances=False, return_indices=True)
        rgb = rgb[idx[0], idx[1]]
    return rgb


def frame_diff(a, b):
    """两帧差异——RGB（外扩后）与 alpha 各半。"""
    aa = a[:, :, 3] / 255.0
    ab = b[:, :, 3] / 255.0
    ra = edge_extend(a[:, :, :3], aa)
    rb = edge_extend(b[:, :, :3], ab)
    rgb_d = float(np.abs(ra - rb).mean() / 255.0)
    a_d = float(np.abs(aa - ab).mean())
    return (rgb_d + a_d) / 2.0


def save_compare(fa, fb, out_path):
    """三联图——候选帧 | loop 首帧 | 差异热力。"""
    ha = Image.fromarray(fa.astype(np.uint8), "RGBA")
    hb = Image.fromarray(fb.astype(np.uint8), "RGBA")
    d = np.clip(np.abs(fa[:, :, :3] - fb[:, :, :3]).mean(axis=2) * 3.0, 0, 255).astype(np.uint8)
    hd = Image.fromarray(np.dstack([d, d, d, np.full_like(d, 255)]), "RGBA")
    w = ha.width + hb.width + hd.width
    h = max(ha.height, hb.height)
    canvas = Image.new("RGBA", (w, h), (18, 18, 18, 255))
    canvas.paste(ha, (0, 0), ha)
    canvas.paste(hb, (ha.width, 0), hb)
    canvas.paste(hd, (ha.width + hb.width, 0), hd)
    canvas.save(out_path)


def main():
    os.makedirs(OUT, exist_ok=True)
    for before, loop in PAIRS:
        bf = load_frames(before)
        lf = load_frames(loop)
        target = lf[0]
        scores = [frame_diff(f, target) for f in bf]
        best = int(np.argmin(scores))
        print("== %s（%d 帧）→ %s 首帧（%d 帧）" % (before, len(bf), loop, len(lf)))
        print("   现状末帧（第 %d 帧）差异 = %.5f" % (len(bf), scores[-1]))
        print("   最优候选（第 %d 帧，%.0f ms）差异 = %.5f  ← 截断点" % (best + 1, (best + 1) * 20, scores[best]))
        print("   改善幅度 = %.1f%%" % ((1 - scores[best] / scores[-1]) * 100))
        step = max(1, len(scores) // 16)
        print("   曲线：" + "  ".join("%d:%.4f" % (i + 1, scores[i]) for i in range(0, len(scores), step)))
        png = os.path.join(OUT, "seam_%s.png" % before)
        save_compare(bf[best], target, png)
        print("   对照图 → %s" % png)
        print("")


if __name__ == "__main__":
    main()
