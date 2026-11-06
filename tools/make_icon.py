#!/usr/bin/env python3
"""AgentEye 应用图标生成器(dev-only)。

设计:只要字母 A——实心窄高 A(托盘蓝 #285A96),无瞳孔、无底板。
字形 = 外三角实心 + 同底相似内三角挖空负形 + 横杠;坐标在 1024×1024
逻辑画布上定义,绘制时 4× 超采样再 LANCZOS 缩放,边缘平滑且不依赖
任何字体。托盘染色:整字都是 SrcBody 色,随额度紧张度整体换色。

用法:
  python tools/make_icon.py --preview            # 出多尺寸预览拼版 PNG
  python tools/make_icon.py --variant final      # 生成正式 .ico
  python tools/make_icon.py --variant final --out path.ico
"""

import argparse
import math
import os

from PIL import Image, ImageDraw

SS = 4                      # 超采样倍数
LOGICAL = 1024              # 逻辑画布边长
ICO_SIZES = [16, 20, 24, 32, 40, 48, 64, 128, 256]

# 字母 A 的骨架(逻辑画布 1024)——只要 A:
#   外三角实心,同底相似内三角挖出中负形,横杠补回字面;
#   定稿 = 窄高 A(腿薄、字面瘦),≤24px 用 small 变体(腿更粗、横杠更靠下)。
VARIANTS = {
    "final": dict(
        name="AgentEye 字母 A(托盘蓝)",
        apex_y=95.0,
        base_y=925.0,
        base_l=205.0,
        base_r=819.0,
        leg=104.0,           # 腿部水平厚度
        bar_top=622.0,       # 横杠上沿
        bar_bottom=756.0,    # 横杠下沿
        body="#285A96",      # 托盘蓝(TrayIconRenderer.SrcBody 同源)
    ),
    # 小尺寸专用:腿更粗、横杠更靠下,≤24px 时字面不至于糊成一团
    "small": dict(
        name="AgentEye 字母 A(小尺寸版)",
        apex_y=95.0,
        base_y=925.0,
        base_l=185.0,
        base_r=839.0,
        leg=126.0,
        bar_top=632.0,
        bar_bottom=762.0,
        body="#285A96",
    ),
}

# ≤ 该尺寸用 small 变体(光学尺寸)
SMALL_UNTIL = 24


def rgb(h):
    h = h.lstrip("#")
    return (int(h[0:2], 16), int(h[2:4], 16), int(h[4:6], 16), 255)


def triangles(v):
    """返回 (外三角形, 内三角形) 的点集;内三角形与外三角形边平行(同底相似形)。"""
    apex = (512.0, v["apex_y"])
    base_l, base_r, base_y = v["base_l"], v["base_r"], v["base_y"]
    half = (base_r - 512.0)
    s = 1.0 - v["leg"] / half
    cy = (apex[1] + base_y + base_y) / 3.0
    inner_apex = (512.0, cy + s * (apex[1] - cy))
    inner_l = (512.0 + s * (base_l - 512.0), base_y)
    inner_r = (512.0 + s * (base_r - 512.0), base_y)
    outer = [apex, (base_l, base_y), (base_r, base_y)]
    inner = [inner_apex, inner_l, inner_r]
    return outer, inner


def edge_x(y, inner, side):
    """内三角形左右边在高度 y 处的 x。"""
    (ax, ay), (lx, ly), (rx, ry) = inner
    if side == "l":
        t = (ly - y) / (ly - ay)
        return lx + (ax - lx) * t
    t = (ry - y) / (ry - ay)
    return rx + (ax - rx) * t


def build(v, size=LOGICAL, canvas=LOGICAL * SS):
    """按超采样画布绘制字母 A,返回 (大图, 缩到 size 的图)。"""
    outer, inner = triangles(v)
    img = Image.new("RGBA", (canvas, canvas), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)

    def sc(pts):
        k = canvas / LOGICAL
        return [(x * k, y * k) for (x, y) in pts]

    body = rgb(v["body"])
    d.polygon(sc(outer), fill=body)                  # 整块实心
    d.polygon(sc(inner), fill=(0, 0, 0, 0))          # 挖出内三角(A 的中空)

    bt, bb = v["bar_top"], v["bar_bottom"]
    bar = [
        (edge_x(bt, inner, "l"), bt),
        (edge_x(bt, inner, "r"), bt),
        (edge_x(bb, inner, "r"), bb),
        (edge_x(bb, inner, "l"), bb),
    ]
    d.polygon(sc(bar), fill=body)                    # 横杠

    return img, img.resize((size, size), Image.LANCZOS)


def make_ico(v, out_path, small_v=None):
    big, _ = build(v)
    small_big = build(small_v)[0] if small_v else None
    frames = []
    for s in ICO_SIZES:
        src = small_big if (small_big is not None and s <= SMALL_UNTIL) else big
        frames.append(src.resize((s, s), Image.LANCZOS))
    os.makedirs(os.path.dirname(os.path.abspath(out_path)), exist_ok=True)
    frames[-1].save(out_path, format="ICO", sizes=[(s, s) for s in ICO_SIZES],
                    append_images=frames[:-1], bitmap_format="bmp")
    return out_path


def make_preview(variants, out_path):
    """拼版预览:上排各尺寸原大,下排小尺寸放大 4 倍(看任务栏/标题栏效果)。"""
    pad = 16
    zoom = 4
    width = 1300
    rows = []
    for key in variants:
        v = VARIANTS[key]
        big, master = build(v)
        small_big = build(VARIANTS["small"])[0] if "small" in VARIANTS else None
        strip = Image.new("RGBA", (width, 300), (245, 245, 245, 255))
        x = pad
        for s in ICO_SIZES:
            src = small_big if (small_big is not None and s <= SMALL_UNTIL) else big
            f = src.resize((s, s), Image.LANCZOS)
            strip.alpha_composite(f, (x, pad))
            x += s + pad
        y = 200
        for s in (16, 32, 48):
            src = small_big if (small_big is not None and s <= SMALL_UNTIL) else big
            f = src.resize((s, s), Image.LANCZOS).resize((s * zoom, s * zoom), Image.NEAREST)
            strip.alpha_composite(f, (x, y))
            x += s * zoom + pad
        rows.append((v["name"], strip))

    total_h = sum(r[1].height + pad for r in rows) + pad
    sheet = Image.new("RGBA", (width, total_h), (255, 255, 255, 255))
    yy = pad
    for _, strip in rows:
        sheet.alpha_composite(strip, (0, yy))
        yy += strip.height + pad
    sheet.convert("RGB").save(out_path)
    return out_path


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--variant", default="final", choices=sorted(VARIANTS))
    ap.add_argument("--out", default=os.path.join("src", "AgentEye.WinUI", "Assets", "AgentEye.ico"))
    ap.add_argument("--preview", action="store_true", help="只出预览拼版")
    ap.add_argument("--preview-out", default="icon_preview.png")
    args = ap.parse_args()

    if args.preview:
        path = make_preview(sorted(VARIANTS), args.preview_out)
        print("preview:", path)
        return

    path = make_ico(VARIANTS[args.variant], args.out,
                    small_v=VARIANTS.get("small"))
    print("ico:", path, os.path.getsize(path), "bytes")


if __name__ == "__main__":
    main()
