#!/usr/bin/env python3
"""AgentEye 应用图标生成器(dev-only)。

设计:几何风瓦片——托盘蓝圆角方底(#285A96)+ 白色圆头笔画 Λ A 字 +
胶囊横杠,两腿负空间里一颗亮蓝瞳孔(#8FC4F2)。三个色与
TrayIconRenderer 的 SrcBody/SrcPupil 染色契约同源:瓦片 → 紧张度色,
白 A / 瞳孔 → 提亮色,托盘上任何状态都读得清。
坐标在 1024×1024 逻辑画布上定义,绘制时 4× 超采样再 LANCZOS 缩放,
边缘平滑且不依赖任何字体。瞳孔位置不手调:自动取"内三角"(两腿内边
+ 横杠上沿围成)的内心,光学上永远居中。

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

# 几何瓦片的骨架(逻辑画布 1024):
#   瓦片 = 圆角方底(托盘蓝),A = 白色圆头笔画 Λ + 胶囊横杠,
#   瞳孔 = 内三角(两腿内边 + 横杠上沿)内心处的亮蓝圆点。
# 定稿比例:字面约占瓦片 60%,重心略高于几何中心;≤24px 用 small 变体
# (笔画更粗、瞳孔更大,字面不糊)。
VARIANTS = {
    "final": dict(
        name="AgentEye 几何瓦片(蓝底白 A + 瞳孔)",
        tile_radius=216.0,   # 瓦片圆角(约 21%,Win11 应用图标观感)
        apex=(512.0, 219.0),
        foot_l=(270.0, 829.0),
        foot_r=(754.0, 829.0),
        stroke=128.0,        # 笔画厚度
        bar_y0=614.0,        # 横杠上沿
        bar_y1=726.0,        # 横杠下沿
        pupil_ratio=0.62,    # 瞳孔半径 = 内三角内切圆半径的比例
        body="#285A96",      # 瓦片(TrayIconRenderer.SrcBody 同源)
        glyph="#FFFFFF",     # A 字笔画
        pupil_color="#8FC4F2",   # 瞳孔(TrayIconRenderer.SrcPupil 同源)
    ),
    # 小尺寸专用:笔画更粗、瞳孔更大,≤24px 时字面不至于糊成一团
    "small": dict(
        name="AgentEye 几何瓦片(小尺寸版)",
        tile_radius=216.0,
        apex=(512.0, 214.0),
        foot_l=(262.0, 836.0),
        foot_r=(762.0, 836.0),
        stroke=158.0,
        bar_y0=632.0,
        bar_y1=744.0,
        pupil_ratio=0.74,
        body="#285A96",
        glyph="#FFFFFF",
        pupil_color="#8FC4F2",
    ),
}

# ≤ 该尺寸用 small 变体(光学尺寸)
SMALL_UNTIL = 24


def rgb(h):
    h = h.lstrip("#")
    return (int(h[0:2], 16), int(h[2:4], 16), int(h[4:6], 16), 255)


def leg_center_x(v, y):
    """腿中线在高度 y 处的 x(左右各一个)。"""
    (ax, ay) = v["apex"]
    (lx, ly), (rx, ry) = v["foot_l"], v["foot_r"]
    t = (y - ay) / (ly - ay)
    return ax + (lx - ax) * t, ax + (rx - ax) * t


def upper_counter_incenter(v):
    """内三角(两腿内边 + 横杠上沿围成)的内心与内切半径(逻辑坐标)。"""
    (ax, ay) = v["apex"]
    (lx, ly), (rx, ry) = v["foot_l"], v["foot_r"]
    stroke = v["stroke"]
    # 腿是斜的:竖直切一刀,水平厚度 = 垂直厚度 / sin(倾角)
    length = math.hypot(lx - ax, ly - ay)
    sin_phi = abs(ly - ay) / length
    hh = stroke / 2.0 / sin_phi
    # 内边与横杠上沿的交点
    y0 = v["bar_y0"]
    t = (y0 - ay) / (ly - ay)
    clx = ax + (lx - ax) * t           # 左腿中线在该高度的 x
    inner_l = clx + hh
    inner_r = 1024.0 - inner_l         # 左右对称
    # 左内边向上延伸到 x=512 处的 y(内顶点)
    slope = (ax - lx) / (ly - ay)      # 每降 1px,x 变化量(取正)
    apex_y = y0 - (512.0 - inner_l) / slope
    tri = ((512.0, apex_y), (inner_l, y0), (inner_r, y0))
    (a1, b1, c1) = tri
    side_a = inner_r - inner_l                      # 顶点对边(底)
    side_b = math.hypot(a1[0] - c1[0], a1[1] - c1[1])
    side_c = math.hypot(a1[0] - b1[0], a1[1] - b1[1])
    p = side_a + side_b + side_c
    cx = (side_a * a1[0] + side_b * b1[0] + side_c * c1[0]) / p
    cy = (side_a * a1[1] + side_b * b1[1] + side_c * c1[1]) / p
    area = abs((b1[0] - a1[0]) * (c1[1] - a1[1]) - (c1[0] - a1[0]) * (b1[1] - a1[1])) / 2.0
    r_in = area / (p / 2.0)
    return (cx, cy), r_in


def build(v, size=LOGICAL, canvas=LOGICAL * SS):
    """按超采样画布绘制几何瓦片,返回 (大图, 缩到 size 的图)。"""
    k = canvas / LOGICAL
    img = Image.new("RGBA", (canvas, canvas), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    body = rgb(v["body"])
    glyph = rgb(v["glyph"])

    # 瓦片:圆角方底(托盘紧张度染色的载体)
    tr = v["tile_radius"] * k
    d.rounded_rectangle([0, 0, canvas - 1, canvas - 1], radius=tr, fill=body)

    # Λ 两腿:白色粗笔画 + 端点圆(峰顶共用一个圆 → 圆角峰,脚端圆头)
    lw = int(round(v["stroke"] * k))
    apex = (v["apex"][0] * k, v["apex"][1] * k)
    foot_l = (v["foot_l"][0] * k, v["foot_l"][1] * k)
    foot_r = (v["foot_r"][0] * k, v["foot_r"][1] * k)
    d.line([apex, foot_l], fill=glyph, width=lw)
    d.line([apex, foot_r], fill=glyph, width=lw)
    r_cap = lw / 2.0
    for (cx, cy) in (v["apex"], v["foot_l"], v["foot_r"]):
        d.ellipse([(cx * k - r_cap, cy * k - r_cap),
                   (cx * k + r_cap, cy * k + r_cap)], fill=glyph)

    # 横杠:白色胶囊,两端落在腿中线上(与腿融为一体,构成 A 的字面)
    y0, y1 = v["bar_y0"], v["bar_y1"]
    lx, rx = leg_center_x(v, (y0 + y1) / 2.0)
    d.rounded_rectangle([lx * k, y0 * k, rx * k, y1 * k],
                        radius=(y1 - y0) / 2.0 * k, fill=glyph)

    # 瞳孔:内三角内心处的亮蓝圆点("眼"),光学上自动居中
    (pcx, pcy), r_in = upper_counter_incenter(v)
    pr = r_in * v["pupil_ratio"]
    d.ellipse([(pcx - pr) * k, (pcy - pr) * k, (pcx + pr) * k, (pcy + pr) * k],
              fill=rgb(v["pupil_color"]))

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
