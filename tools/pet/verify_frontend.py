"""前端资源核销——仓库源码区 ↔ 运行区逐文件字节比对（含 js/css/html/pet）。

只读。用法：
    python tools/pet/verify_frontend.py --run <运行区html目录>
    （--repo 默认 = 仓库 CatHome4/html；--run 无默认——运行区在仓库之外，须显式指定）
"""
import os
import hashlib
import argparse

HERE = os.path.dirname(os.path.abspath(__file__))        # tools/pet
REPO_ROOT = os.path.dirname(os.path.dirname(HERE))       # 仓库根

DEFAULT_REPO_HTML = os.path.join(REPO_ROOT, "CatHome4", "html")

SKIP_DIRS = {"tests", "node_modules"}


def parse_args():
    """命令行——仓库 html 目录（--repo）与运行区 html 目录（--run，必填）。"""
    ap = argparse.ArgumentParser(description="前端资源核销：仓库源码区 ↔ 运行区逐字节比对（只读）")
    ap.add_argument("--repo", default=DEFAULT_REPO_HTML, help="仓库 html 目录（默认 CatHome4/html）")
    ap.add_argument("--run", required=True, help="运行区 html 目录（如 <部署目录>/html）")
    return ap.parse_args()


ARGS = parse_args()
REPO = ARGS.repo
RUN = ARGS.run


def scan(root, sub):
    """返回 {相对路径: (字节数, md5)}。"""
    base = os.path.join(root, sub) if sub else root
    out = {}
    for dirpath, dirnames, filenames in os.walk(base):
        dirnames[:] = [d for d in dirnames if d not in SKIP_DIRS]
        for f in filenames:
            p = os.path.join(dirpath, f)
            rel = os.path.relpath(p, root)
            with open(p, "rb") as fh:
                data = fh.read()
            out[rel] = (len(data), hashlib.md5(data).hexdigest())
    return out


def main():
    left = scan(REPO, "")
    right = scan(RUN, "")

    only_left = sorted(set(left) - set(right))
    only_right = sorted(set(right) - set(left))
    mismatch = [f for f in sorted(set(left) & set(right)) if left[f] != right[f]]

    print("仓库 %d 件 / 运行 %d 件" % (len(left), len(right)))
    print("仅在仓库: " + (", ".join(only_left) if only_left else "无"))
    print("仅在运行: " + (", ".join(only_right) if only_right else "无"))
    if mismatch:
        for f in mismatch:
            print("字节不一致 %s: 仓库 %d / 运行 %d" % (f, left[f][0], right[f][0]))
            with open(os.path.join(REPO, f), "rb") as fh:
                lh = fh.read(3)
            with open(os.path.join(RUN, f), "rb") as fh:
                rh = fh.read(3)
            print("   头字节 仓库 %s / 运行 %s" % (lh.hex(), rh.hex()))
    else:
        print("字节比对 mismatch=0")


if __name__ == "__main__":
    main()
