#!/usr/bin/env python3
"""AgentEye 应用图标生成器(dev-only)。

设计:几何风字母 A——圆头粗笔画的 Λ(峰顶/脚端全圆角)+ 胶囊形横杠,
两腿负空间里一颗瞳孔圆点。只用两个色:笔画托盘蓝 #285A96、瞳孔亮蓝
#8FC4F2(与 TrayIconRenderer 的 SrcBody/SrcPupil 染色契约同源)。
坐标在 1024×1024 逻辑画布上定义,绘制时 4× 超采样再 LANCZOS 缩放,
边缘平滑且不依赖任何字体。

用法:
  python tools/make_icon.py --preview            # 出多尺寸预览拼版 PNG
  python tools/make_icon.py --variant final      # 生成正式 .ico
  python tools/make_icon.py --variant final --out path.ico
"""

import argparse
import os

from PIL import Image, ImageDraw

SS = 4                      # 超采样倍数
LOGICAL = 1024              # 逻辑画布边长
ICO_SIZES = [16, 20, 24, 32, 40, 48, 64, 128, 256]

# 几何 A 的骨架(逻辑画布 1024):
#   Λ = 顶点 apex 到两脚 foot_l / foot_r 的粗笔画(圆头,峰顶圆角),
#   横杠 = 两腿之间的胶囊形(两端落在腿中线上,与腿融为一体),
#   瞳孔 = 两腿负空间、横杠上方的一颗圆点。
# 定稿 = 窄高 Λ + 亮蓝瞳孔;≤24px 用 small 变体(笔画更粗,字面不糊)。
VARIANTS = {
    "final": dict(
        name="AgentEye 几何 A(圆头笔画 + 瞳孔)",
        apex=(512.0, 118.0),
        foot_l=(152.0, 902.0),
        foot_r=(872.0, 902.0),
        stroke=150.0,        # 笔画厚度
        bar_y0=652.0,        # 横杠上沿
        bar_y1=782.0,        # 横杠下沿(胶囊高 130)
        pupil=(512.0, 478.0),
        pupil_r=62.0,
        body="#285A96",      # 托盘蓝(TrayIconRenderer.SrcBody 同源)
        pupil_color="#8FC4F2",
    ),
    # 小尺寸专用:笔画更粗、横杠更靠下,≤24px 时字面不至于糊成一团
    "small": dict(
        name="AgentEye 几何 A(小尺寸版)",
        apex=(512.0, 110.0),
        foot_l=(138.0, 910.0),
        foot_r=(886.0, 910.0),
        stroke=182.0,
        bar_y0=672.0,
        bar_y1=798.0,
        pupil=(512.0, 482.0),
        pupil_r=76.0,
        body="#285A96",
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


def build(v, size=LOGICAL, canvas=LOGICAL * SS):
    """按超采样画布绘制几何 A,返回 (大图, 缩到 size 的图)。"""
    k = canvas / LOGICAL
    img = Image.new("RGBA", (canvas, canvas), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    body = rgb(v["body"])

    # Λ 两腿:粗笔画 + 端点圆(峰顶共用一个圆 → 圆角峰,脚端圆头)
    lw = int(round(v["stroke"] * k))
    apex = (v["apex"][0] * k, v["apex"][1] * k)
    foot_l = (v["foot_l"][0] * k, v["foot_l"][1] * k)
    foot_r = (v["foot_r"][0] * k, v["foot_r"][1] * k)
    d.line([apex, foot_l], fill=body, width=lw)
    d.line([apex, foot_r], fill=body, width=lw)
    r_cap = lw / 2.0
    for (cx, cy) in (v["apex"], v["foot_l"], v["foot_r"]):
        d.ellipse([(cx * k - r_cap, cy * k - r_cap),
                   (cx * k + r_cap, cy * k + r_cap)], fill=body)

    # 横杠:胶囊形,两端落在腿中线上(与腿融为一体,构成 A 的字面)
    y0, y1 = v["bar_y0"], v["bar_y1"]
    lx, rx = leg_center_x(v, (y0 + y1) / 2.0)
    d.rounded_rectangle([lx * k, y0 * k, rx * k, y1 * k],
                        radius=(y1 - y0) / 2.0 * k, fill=body)

    # 瞳孔:两腿负空间、横杠上方的圆点("眼")
    (pcx, pcy), pr = v["pupil"], v["pupil_r"]
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
