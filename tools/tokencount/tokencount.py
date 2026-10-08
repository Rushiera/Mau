#!/usr/bin/env python3
# -*- coding: utf-8 -*-
r"""
tokencount.py —— 无依赖 token 统计（HuggingFace tokenizer.json 口径 · byte-level BPE）

用法:
  python tools/tokencount/tokencount.py <文本文件>
  python tools/tokencount/tokencount.py <文本文件> --tokenizer <tokenizer.json 路径>
  python tools/tokencount/tokencount.py --text "Hello, world!"
  python tools/tokencount/tokencount.py <文本文件> --ids            # 附加 id 列表
  python tools/tokencount/tokencount.py <文本文件> --pieces         # 附加 id:token 文本（看分词边界）
  type x.txt | python tools/tokencount/tokencount.py -

词表查找: --tokenizer > 环境变量 TOKENIZER_JSON > 脚本同级 tokenizer.json > 当前目录 tokenizer.json
输出: 一个整数（token 数）；--ids / --pieces 为可选附加行

依赖: 仅 Python 3 标准库（argparse / heapq / json / os / re / sys）
口径: byte-level BPE —— 空归一化 → 三段 Split 预切分 + ByteLevel 字节映射 → 按 merges 顺序贪心合并 → 查表出 id
偏差: 正则中的 \p{L}\p{M}\p{P}\p{S} 用 Python re 近似 —— 拉丁 / 中文无差，带变音符号的文字可能偏离
"""
import argparse
import heapq
import json
import os
import re
import sys

if hasattr(sys.stdout, "reconfigure"):     # Windows 控制台代码页会撞坏非 ASCII 输出——统一钉 UTF-8
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    sys.stderr.reconfigure(encoding="utf-8", errors="replace")


def _bytes_to_unicode():
    """GPT-2 byte-level 映射: 256 个字节值 → 可见 unicode 字符（可逆）"""
    bs = list(range(ord("!"), ord("~") + 1))
    bs += list(range(ord("\u00a1"), ord("\u00ac") + 1))
    bs += list(range(ord("\u00ae"), ord("\u00ff") + 1))
    cs = bs[:]
    n = 0
    for b in range(256):
        if b not in bs:
            bs.append(b)
            cs.append(256 + n)
            n += 1
    return dict(zip(bs, [chr(c) for c in cs]))


BYTE_TO_UNI = _bytes_to_unicode()
UNI_TO_BYTE = {v: k for k, v in BYTE_TO_UNI.items()}


def _piece_text(piece):
    """token 内部形式（字节映射字符）→ 可读文本；跨 token 的半个多字节字符显示为 U+FFFD"""
    try:
        bs = bytes(UNI_TO_BYTE[c] for c in piece)
    except KeyError:
        return piece                      # special token 等非映射形态，原样返回
    return bs.decode("utf-8", "replace")


def _visible(text):
    """把空白字符换成可显示符号，避免拼接歧义（空格 token 的文本就是真空格）"""
    return (text.replace("\r", "\u240d").replace("\n", "\u240a")
                .replace("\t", "\u2409").replace(" ", "\u2423"))


# pre_tokenizer 三段 Split（Isolated）——第 1 / 2 段照抄 tokenizer.json，第 3 段是 \p{..} 的 Python 近似
RE_NUM = re.compile(r"\d{1,3}")
RE_CJK = re.compile(r"[\u4e00-\u9fa5\u3040-\u309f\u30a0-\u30ff]+")
RE_MAIN = re.compile(
    r"[\"!#$%&'()*+,\-./:;<=>?@\[\\\]^_`{|}~][A-Za-z]+"
    r"|[\s_]?[^\W\d_]+"
    r"| ?[^\w\s]+[\r\n]*"
    r"|\s*[\r\n]+"
    r"|\s+(?!\S)"
    r"|\s+"
)


class Tokenizer:
    """HuggingFace tokenizer.json（byte-level BPE）只读实现"""

    def __init__(self, path):
        with open(path, "r", encoding="utf-8") as fh:
            obj = json.load(fh)

        model = obj.get("model") or {}
        if model.get("type") != "BPE":
            raise SystemExit("ERR|UNSUPPORTED_MODEL|model.type=%s —— 本脚本只实现 BPE" % model.get("type"))

        self.vocab = dict(model.get("vocab") or {})
        self.ranks = {}
        for i, pair in enumerate(model.get("merges") or []):
            self.ranks[pair] = i

        added = obj.get("added_tokens") or []
        for tok in added:                      # 128000+ 段不在 vocab 主表里，并入统一查找表
            self.vocab.setdefault(tok["content"], tok["id"])

        specials = sorted((t["content"] for t in added if t.get("special")), key=len, reverse=True)
        self.re_special = re.compile("|".join(re.escape(s) for s in specials)) if specials else None

        self.cache = {}

    def encode(self, text, want_pieces=False):
        """文本 → id 列表；want_pieces=True 时返回 (ids, syms)"""
        ids = []
        pieces = []
        pos = 0
        if self.re_special is not None:
            for m in self.re_special.finditer(text):
                if m.start() > pos:
                    self._emit_plain(text[pos:m.start()], ids, pieces, want_pieces)
                ids.append(self.vocab[m.group()])
                pieces.append(m.group())
                pos = m.end()
        if pos < len(text):
            self._emit_plain(text[pos:], ids, pieces, want_pieces)
        return (ids, pieces) if want_pieces else ids

    def _emit_plain(self, text, ids, pieces, want_pieces):
        for a in self._split_isolated(RE_NUM, text):
            for b in self._split_isolated(RE_CJK, a):
                for chunk in self._split_isolated(RE_MAIN, b):
                    syms, chunk_ids = self._bpe_ids(chunk)
                    ids.extend(chunk_ids)
                    if want_pieces:
                        pieces.extend(syms)

    @staticmethod
    def _split_isolated(pattern, text):
        """Split(behavior=Isolated): 命中段与未命中段各自成块，顺序保持"""
        pieces = []
        pos = 0
        for m in pattern.finditer(text):
            if m.end() == m.start():
                continue
            if m.start() > pos:
                pieces.append(text[pos:m.start()])
            pieces.append(m.group())
            pos = m.end()
        if pos < len(text):
            pieces.append(text[pos:])
        return pieces

    def _bpe_ids(self, chunk):
        """单个预切分块 → (符号列表, id 列表)（带缓存）"""
        hit = self.cache.get(chunk)
        if hit is not None:
            return hit
        syms = self._bpe([BYTE_TO_UNI[b] for b in chunk.encode("utf-8")])
        ids = []
        for s in syms:
            tid = self.vocab.get(s)
            if tid is None:
                raise SystemExit("ERR|TOKEN_NOT_IN_VOCAB|%r —— 词表缺该符号，分词结果不可信" % s)
            ids.append(tid)
        hit = (syms, tuple(ids))
        self.cache[chunk] = hit
        return hit

    def _bpe(self, syms):
        """贪心 BPE: 每轮合并 ranks 最小的相邻对（链表 + 堆，O(n log n)）"""
        n = len(syms)
        if n < 2:
            return syms
        ranks = self.ranks
        prev = list(range(-1, n - 1))
        nxt = list(range(1, n + 1))
        nxt[n - 1] = -1
        alive = [True] * n
        heap = []
        for i in range(n - 1):
            r = ranks.get(syms[i] + " " + syms[i + 1])
            if r is not None:
                heap.append((r, i))
        heapq.heapify(heap)

        while heap:
            r, i = heapq.heappop(heap)
            if not alive[i]:
                continue
            j = nxt[i]
            if j < 0 or not alive[j]:
                continue
            if ranks.get(syms[i] + " " + syms[j]) != r:
                continue
            syms[i] = syms[i] + syms[j]
            alive[j] = False
            k = nxt[j]
            nxt[i] = k
            if k >= 0:
                prev[k] = i
            p = prev[i]
            if p >= 0:
                rr = ranks.get(syms[p] + " " + syms[i])
                if rr is not None:
                    heapq.heappush(heap, (rr, p))
            if k >= 0:
                rr = ranks.get(syms[i] + " " + syms[k])
                if rr is not None:
                    heapq.heappush(heap, (rr, i))

        out = []
        i = 0
        while i >= 0:
            out.append(syms[i])
            i = nxt[i]
        return out


def _find_tokenizer(explicit):
    if explicit:
        return explicit
    env = os.environ.get("TOKENIZER_JSON")
    if env:
        return env
    here = os.path.dirname(os.path.abspath(__file__))
    for cand in (os.path.join(here, "tokenizer.json"), os.path.join(os.getcwd(), "tokenizer.json")):
        if os.path.isfile(cand):
            return cand
    return None


def main(argv=None):
    ap = argparse.ArgumentParser(description="无依赖 token 统计（byte-level BPE）")
    ap.add_argument("file", nargs="?", help="文本文件路径；- 表示标准输入")
    ap.add_argument("--text", help="直接给文本（与 file 二选一）")
    ap.add_argument("--tokenizer", help="tokenizer.json 路径")
    ap.add_argument("--ids", action="store_true", help="附加输出 id 列表")
    ap.add_argument("--pieces", action="store_true", help="附加输出 id:token 文本")
    args = ap.parse_args(argv)

    if args.text is None and not args.file:
        ap.error("需要给出文本文件路径 或 --text")

    path = _find_tokenizer(args.tokenizer)
    if not path or not os.path.isfile(path):
        sys.stderr.write("ERR|TOKENIZER_NOT_FOUND|用 --tokenizer 指定 tokenizer.json，或设环境变量 TOKENIZER_JSON\n")
        return 2

    if args.text is not None:
        text = args.text
    elif args.file == "-":
        text = sys.stdin.read()
    else:
        with open(args.file, "r", encoding="utf-8-sig", errors="replace") as fh:
            text = fh.read()

    tok = Tokenizer(path)
    if args.ids or args.pieces:
        ids, syms = tok.encode(text, want_pieces=True)
    else:
        ids, syms = tok.encode(text), []
    print(len(ids))
    if args.ids:
        print(ids)
    if args.pieces:
        print(" ".join("%d:%s" % (i, _visible(_piece_text(s))) for i, s in zip(ids, syms)))
    return 0


if __name__ == "__main__":
    sys.exit(main())
