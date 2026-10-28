using System.Runtime.InteropServices;

namespace AgentEye.WinUI.Tray;

/// <summary>
/// 托盘图标渲染:应用 A 标(Assets\AgentEye.ico,按额度紧张度染色)+ 数字牌。
///
/// 纯 Win32/GDI,不依赖 WinUI(可单独编译做预览)。
/// 状态语义沿用旧托盘:蓝=宽松 绿=中 橙=高 红=烧完,无数据走灰色;
/// 数字牌=白底深字,保证任何底色上都读得清。
///
/// 托盘是增强面:任何一步失败静默降级(退到 LegacyPlate 或空图标),不许崩主流程。
/// </summary>
public static class TrayIconRenderer
{
    public enum Layout
    {
        /// <summary>A 满幅 + 右下角数字牌(默认)。</summary>
        IconBadgeBottomRight = 0,
        /// <summary>只画 A(数字只进 tooltip)。</summary>
        IconOnly = 1,
        /// <summary>旧样式:彩色圆角底 + 大数字(没有 ico 资源时的兜底)。</summary>
        LegacyPlate = 2,
    }

    /// <summary>默认版式:换版式只改这一处。</summary>
    public const Layout Default = Layout.IconBadgeBottomRight;

    private const int Size = 32;

    private const uint IMAGE_ICON = 1;
    private const uint LR_LOADFROMFILE = 0x0000_0010;
    private const uint LR_DEFAULTCOLOR = 0x0000_0020;
    private const uint DIB_RGB_COLORS = 0;
    private const uint DI_NORMAL = 3;
    private const int TRANSPARENT = 1;
    private const int DT_CENTER = 1, DT_VCENTER = 4, DT_SINGLELINE = 0x20;
    private const int FW_BOLD = 700;
    private const uint SRCCOPY = 0x00CC_0020;

    // 紧张度配色(沿用旧托盘语义;RGB)
    private static readonly (int R, int G, int B) Blue = (40, 90, 150);
    private static readonly (int R, int G, int B) Green = (40, 100, 60);
    private static readonly (int R, int G, int B) Orange = (170, 80, 20);
    private static readonly (int R, int G, int B) Red = (150, 30, 30);
    private static readonly (int R, int G, int B) Gray = (90, 90, 90);

    // 源图色(应用 A 标):体色 + 瞳孔色,染色时按两色线性插值映射
    private static readonly (int R, int G, int B) SrcBody = (0x28, 0x5A, 0x96);
    private static readonly (int R, int G, int B) SrcPupil = (0x8F, 0xC4, 0xF2);

    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    private struct BITMAPINFOHEADER
    {
        public uint Size; public int Width, Height; public ushort Planes, BitCount;
        public uint Compression, SizeImage; public int XPpm, YPpm; public uint ClrUsed, ClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO
    {
        public BITMAPINFOHEADER Header;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public uint[] Colors;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ICONINFO { public bool Icon; public int X, Y; public nint hbmMask, hbmColor; }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int L, T, R, B; }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint LoadImageW(nint hInst, string name, uint type,
        int cx, int cy, uint flags);
    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(nint hIcon);
    [DllImport("user32.dll")]
    private static extern bool GetIconInfo(nint hIcon, out ICONINFO info);
    [DllImport("user32.dll")]
    private static extern nint CreateIconIndirect(ref ICONINFO info);
    [DllImport("gdi32.dll")]
    private static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(nint dc);
    [DllImport("gdi32.dll")]
    private static extern nint CreateDIBSection(nint dc, ref BITMAPINFO bmi, uint usage,
        out nint bits, nint section, uint offset);
    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(nint obj);
    [DllImport("gdi32.dll")]
    private static extern nint SelectObject(nint dc, nint obj);
    [DllImport("gdi32.dll")]
    private static extern nint CreateSolidBrush(int color);
    [DllImport("gdi32.dll")]
    private static extern nint CreatePen(uint style, uint width, int color);
    [DllImport("gdi32.dll")]
    private static extern bool RoundRect(nint dc, int l, int t, int r, int b, int w, int h);
    [DllImport("gdi32.dll")]
    private static extern bool SetTextColor(nint dc, int color);
    [DllImport("gdi32.dll")]
    private static extern bool SetBkMode(nint dc, int mode);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern nint CreateFontW(int height, int width, int esc, int orient,
        uint weight, uint italic, uint underline, uint strikeout, uint charset,
        uint outPrecision, uint clipPrecision, uint quality, uint pitchFamily, string face);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int DrawTextW(nint dc, string text, int len, ref RECT rect, uint format);
    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(nint dc, nint hbm, uint start, uint cLines,
        byte[] lpvBits, ref BITMAPINFO lpbmi, uint usage);
    [DllImport("gdi32.dll")]
    private static extern nint CreateBitmap(int w, int h, uint planes, uint bits, nint data);

    /// <summary>额度紧张度 → 配色(体色);瞳孔色由体色提亮得到。</summary>
    private static (int R, int G, int B) ColorsFor(string digits)
    {
        if (digits == "!" || digits == "··") return Gray;
        if (!int.TryParse(digits, out var used)) used = 0;
        return used >= 80 ? Red : used >= 60 ? Orange : used >= 30 ? Green : Blue;
    }

    private static (int R, int G, int B) Lighten((int R, int G, int B) c, double t)
        => ((int)Math.Round(c.R + (255 - c.R) * t),
            (int)Math.Round(c.G + (255 - c.G) * t),
            (int)Math.Round(c.B + (255 - c.B) * t));

    /// <summary>COLORREF 必须是 0x00BBGGRR——高字节非 0 时 GDI 直接拒画(不落任何像素)。</summary>
    private static int Rgb((int R, int G, int B) c) => (c.B << 16) | (c.G << 8) | c.R;

    /// <summary>读 ico 的 32×32 像素(BGRA,自上而下);读不到返回 null。</summary>
    private static byte[]? LoadSourcePixels(string? icoPath)
    {
        if (icoPath is null) return null;
        lock (_srcLock)
        {
            if (_srcCache is not null && string.Equals(_srcPath, icoPath, StringComparison.OrdinalIgnoreCase))
                return _srcCache;
            var px = ReadSourcePixels(icoPath);
            if (px is not null)
            {
                _srcCache = px;
                _srcPath = icoPath;
            }
            return px;
        }
    }

    private static readonly object _srcLock = new();
    private static byte[]? _srcCache;
    private static string? _srcPath;

    private static byte[]? ReadSourcePixels(string icoPath)
    {
        nint hIcon = nint.Zero;
        nint dc = nint.Zero, bmp = nint.Zero, old = nint.Zero;
        try
        {
            hIcon = LoadImageW(nint.Zero, icoPath, IMAGE_ICON, Size, Size,
                LR_LOADFROMFILE | LR_DEFAULTCOLOR);
            if (hIcon == nint.Zero) return null;
            if (!GetIconInfo(hIcon, out var ii)) return null;

            dc = CreateCompatibleDC(nint.Zero);
            if (dc == nint.Zero) return null;
            old = SelectObject(dc, ii.hbmColor);

            var bmi = new BITMAPINFO
            {
                Header = new BITMAPINFOHEADER
                {
                    Size = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                    Width = Size,
                    Height = -Size,   // top-down
                    Planes = 1,
                    BitCount = 32,
                    Compression = 0,
                },
                Colors = new uint[2],
            };
            var buf = new byte[Size * Size * 4];
            if (GetDIBits(dc, ii.hbmColor, 0, Size, buf, ref bmi, DIB_RGB_COLORS) == 0)
                return null;
            return buf;
        }
        catch { return null; }
        finally
        {
            if (old != nint.Zero && dc != nint.Zero) SelectObject(dc, old);
            if (dc != nint.Zero) DeleteDC(dc);
            if (hIcon != nint.Zero) DestroyIcon(hIcon);
        }
    }

    /// <summary>两色线性映射:把 A 标的"体色/瞳孔色"整体换成目标配色。</summary>
    private static void Tint(byte[] px,
        (int R, int G, int B) srcBody, (int R, int G, int B) srcPupil,
        (int R, int G, int B) dstBody, (int R, int G, int B) dstPupil)
    {
        double sdr = srcPupil.R - srcBody.R, sdg = srcPupil.G - srcBody.G, sdb = srcPupil.B - srcBody.B;
        double den = sdr * sdr + sdg * sdg + sdb * sdb;
        double ddr = dstPupil.R - dstBody.R, ddg = dstPupil.G - dstBody.G, ddb = dstPupil.B - dstBody.B;
        for (var i = 0; i < px.Length; i += 4)
        {
            if (px[i + 3] == 0) continue;
            int b = px[i], g = px[i + 1], r = px[i + 2];
            double t = den <= 0 ? 0 : ((r - srcBody.R) * sdr + (g - srcBody.G) * sdg + (b - srcBody.B) * sdb) / den;
            if (t < 0) t = 0; else if (t > 1) t = 1;
            px[i] = (byte)Math.Round(dstBody.B + t * ddb);
            px[i + 1] = (byte)Math.Round(dstBody.G + t * ddg);
            px[i + 2] = (byte)Math.Round(dstBody.R + t * ddr);
        }
    }


    /// <summary>圆角矩形内部判定(用于 alpha 修正:牌子范围内全不透明)。</summary>
    private static bool InsideRounded(int x, int y, int l, int t, int r, int b, int rad)
    {
        if (x < l || x > r || y < t || y > b) return false;
        var cx = x < l + rad ? l + rad : (x > r - rad ? r - rad : x);
        var cy = y < t + rad ? t + rad : (y > b - rad ? b - rad : y);
        var dx = x - cx;
        var dy = y - cy;
        return dx * dx + dy * dy <= rad * rad;
    }

    /// <summary>合成 32×32 的托盘图标像素(BGRA,自上而下)。</summary>
    public static byte[] ComposePixels(string digits, string? icoPath, Layout layout)
        => Compose(digits, icoPath, layout);

    private static byte[] Compose(string digits, string? icoPath, Layout layout)
    {
        var px = new byte[Size * Size * 4];
        var legacy = LegacyColors(digits);
        var body = ColorsFor(digits);
        var pupil = Lighten(body, 0.5);

        var src = icoPath is null ? null : LoadSourcePixels(icoPath);
        if (src is null || layout == Layout.LegacyPlate)
        {
            DrawLegacyPlate(px, digits, legacy.Bg, legacy.Fg);
            return px;
        }

        // 缓存的是原图,染色必须先拷贝(否则第二次渲染会叠色)
        var tinted = (byte[])src.Clone();
        Tint(tinted, SrcBody, SrcPupil, body, pupil);

        var iconSize = Size;
        var iconPx = tinted;
        BlitPixels(px, iconPx, iconSize, 0, 0);

        if (layout == Layout.IconOnly) return px;
        if (digits == "··") return px;   // 一个 provider 都没有:只留 A 标

        // 数字牌:宽度随位数,白底深字 + 状态色描边
        var fontPx = digits.Length >= 3 ? 8 : (digits.Length == 2 ? 9 : 10);
        var badgeW = digits.Length >= 3 ? 18 : (digits.Length == 2 ? 14 : 10);
        const int badgeH = 11, rad = 3;
        var l = 31 - badgeW;
        const int t = 31 - badgeH;
        const int r = 31, b = 31;
        DrawBadge(px, l, t, r, b, rad, digits, fontPx, body);
        return px;
    }

    private static void BlitPixels(byte[] dst, byte[] src, int size, int atX, int atY)
    {
        for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var s = (y * size + x) * 4;
                if (src[s + 3] == 0) continue;
                var d = ((atY + y) * Size + (atX + x)) * 4;
                dst[d] = src[s]; dst[d + 1] = src[s + 1];
                dst[d + 2] = src[s + 2]; dst[d + 3] = src[s + 3];
            }
    }

    /// <summary>白色圆角数字牌 + 深色数字(GDI 画,alpha 事后修正)。</summary>
    private static void DrawBadge(byte[] px, int l, int t, int r, int b, int rad,
        string digits, int fontPx, (int R, int G, int B) border)
    {
        var dc = CreateCompatibleDC(nint.Zero);
        if (dc == nint.Zero) return;
        nint bmp = nint.Zero, oldBmp = nint.Zero, brush = nint.Zero, pen = nint.Zero, font = nint.Zero;
        try
        {
            var bmi = new BITMAPINFO
            {
                Header = new BITMAPINFOHEADER
                {
                    Size = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                    Width = Size,
                    Height = -Size,
                    Planes = 1,
                    BitCount = 32,
                    Compression = 0,
                },
                Colors = new uint[2],
            };
            bmp = CreateDIBSection(dc, ref bmi, 0, out var bits, nint.Zero, 0);
            if (bmp == nint.Zero || bits == nint.Zero) return;
            for (var i = 0; i < Size * Size; i++) Marshal.WriteInt32(bits, i * 4, 0);
            oldBmp = SelectObject(dc, bmp);

            // 白底 + 状态色描边(COLORREF 必须 0x00BBGGRR:高字节非 0 时 GDI 拒画)
            brush = CreateSolidBrush(0x00FFFFFF);
            var oldBrush = SelectObject(dc, brush);
            pen = CreatePen(0, 1, Rgb(border));
            var oldPen = SelectObject(dc, pen);
            RoundRect(dc, l, t, r, b, rad * 2, rad * 2);
            SelectObject(dc, oldBrush);
            SelectObject(dc, oldPen);
            DeleteObject(brush); brush = nint.Zero;
            DeleteObject(pen); pen = nint.Zero;

            // 数字
            SetBkMode(dc, TRANSPARENT);
            SetTextColor(dc, 0x001A1A1A);
            font = CreateFontW(-fontPx, 0, 0, 0, FW_BOLD, 0, 0, 0, 1, 0, 0, 4, 0, "Segoe UI");
            var oldFont = SelectObject(dc, font);
            var rect = new RECT { L = l, T = t, R = r, B = b };
            DrawTextW(dc, digits, digits.Length, ref rect, DT_CENTER | DT_VCENTER | DT_SINGLELINE);
            SelectObject(dc, oldFont);
            DeleteObject(font); font = nint.Zero;

            // 回读 + alpha 修正(牌子范围内不透明)
            var buf = new byte[Size * Size * 4];
            Marshal.Copy(bits, buf, 0, buf.Length);
            for (var y = t; y <= b; y++)
            {
                for (var x = l; x <= r; x++)
                {
                    var idx = (y * Size + x) * 4;
                    if (!InsideRounded(x, y, l, t, r, b, rad)) continue;
                    px[idx] = buf[idx];
                    px[idx + 1] = buf[idx + 1];
                    px[idx + 2] = buf[idx + 2];
                    px[idx + 3] = 255;
                }
            }
            SelectObject(dc, oldBmp);
            DeleteObject(bmp); bmp = nint.Zero;
        }
        catch { /* 增强面:画不出来就留空牌 */ }
        finally
        {
            if (font != nint.Zero) DeleteObject(font);
            if (pen != nint.Zero) DeleteObject(pen);
            if (brush != nint.Zero) DeleteObject(brush);
            if (dc != nint.Zero) DeleteDC(dc);
        }
    }

    /// <summary>旧样式:彩色圆角底 + 大数字(没有 ico 资源时的兜底)。</summary>
    private static void DrawLegacyPlate(byte[] px, string digits,
        (int R, int G, int B) bg, (int R, int G, int B) fg)
    {
        var dc = CreateCompatibleDC(nint.Zero);
        if (dc == nint.Zero) return;
        nint bmp = nint.Zero, oldBmp = nint.Zero, brush = nint.Zero, pen = nint.Zero, font = nint.Zero;
        try
        {
            var bmi = new BITMAPINFO
            {
                Header = new BITMAPINFOHEADER
                {
                    Size = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                    Width = Size,
                    Height = -Size,
                    Planes = 1,
                    BitCount = 32,
                    Compression = 0,
                },
                Colors = new uint[2],
            };
            bmp = CreateDIBSection(dc, ref bmi, 0, out var bits, nint.Zero, 0);
            if (bmp == nint.Zero || bits == nint.Zero) return;
            for (var i = 0; i < Size * Size; i++) Marshal.WriteInt32(bits, i * 4, 0);
            oldBmp = SelectObject(dc, bmp);

            var bgAbgr = Rgb(bg);
            var fgAbgr = Rgb(fg);
            brush = CreateSolidBrush(bgAbgr);
            var oldBrush = SelectObject(dc, brush);
            pen = CreatePen(0, 1, bgAbgr);
            var oldPen = SelectObject(dc, pen);
            RoundRect(dc, 0, 0, Size, Size, 9, 9);
            SelectObject(dc, oldBrush);
            SelectObject(dc, oldPen);
            DeleteObject(brush); brush = nint.Zero;
            DeleteObject(pen); pen = nint.Zero;

            SetBkMode(dc, TRANSPARENT);
            SetTextColor(dc, fgAbgr);
            font = CreateFontW(digits.Length > 2 ? 15 : 19, 0, 0, 0, FW_BOLD,
                0, 0, 0, 1, 0, 0, 4, 0, "Segoe UI");
            var oldFont = SelectObject(dc, font);
            var rect = new RECT { L = 0, T = 0, R = Size, B = Size };
            DrawTextW(dc, digits, digits.Length, ref rect, DT_CENTER | DT_VCENTER | DT_SINGLELINE);
            SelectObject(dc, oldFont);
            DeleteObject(font); font = nint.Zero;

            var buf = new byte[Size * Size * 4];
            Marshal.Copy(bits, buf, 0, buf.Length);
            // 圆角底是不透明的:把非零像素的 alpha 补 255
            for (var i = 0; i < Size * Size; i++)
            {
                var idx = i * 4;
                var any = buf[idx] != 0 || buf[idx + 1] != 0 || buf[idx + 2] != 0;
                px[idx] = buf[idx]; px[idx + 1] = buf[idx + 1]; px[idx + 2] = buf[idx + 2];
                px[idx + 3] = any ? (byte)255 : (byte)0;
            }
            SelectObject(dc, oldBmp);
            DeleteObject(bmp); bmp = nint.Zero;
        }
        catch { /* 增强面 */ }
        finally
        {
            if (font != nint.Zero) DeleteObject(font);
            if (pen != nint.Zero) DeleteObject(pen);
            if (brush != nint.Zero) DeleteObject(brush);
            if (dc != nint.Zero) DeleteDC(dc);
        }
    }

    /// <summary>旧样式的配色(底色 + 字色)。</summary>
    private static ((int R, int G, int B) Bg, (int R, int G, int B) Fg) LegacyColors(string digits)
    {
        if (digits == "!" || digits == "··") return (Gray, (255, 255, 255));
        if (!int.TryParse(digits, out var used)) used = 0;
        return used >= 80 ? (Red, (255, 235, 235))
             : used >= 60 ? (Orange, (255, 240, 220))
             : used >= 30 ? (Green, (225, 250, 235))
             : (Blue, (225, 240, 255));
    }

    /// <summary>渲染成 HICON(托盘用)。失败返回 0。</summary>
    public static nint Render(string digits, string? icoPath, Layout layout)
    {
        var px = Compose(digits, icoPath, layout);
        var dc = CreateCompatibleDC(nint.Zero);
        if (dc == nint.Zero) return nint.Zero;
        nint bmp = nint.Zero, oldBmp = nint.Zero, mask = nint.Zero;
        try
        {
            var bmi = new BITMAPINFO
            {
                Header = new BITMAPINFOHEADER
                {
                    Size = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                    Width = Size,
                    Height = -Size,
                    Planes = 1,
                    BitCount = 32,
                    Compression = 0,
                },
                Colors = new uint[2],
            };
            bmp = CreateDIBSection(dc, ref bmi, 0, out var bits, nint.Zero, 0);
            if (bmp == nint.Zero || bits == nint.Zero) return nint.Zero;
            for (var i = 0; i < Size * Size; i++) Marshal.WriteInt32(bits, i * 4, 0);
            for (var i = 0; i < Size * Size; i++)
            {
                var idx = i * 4;
                Marshal.WriteByte(bits, idx, px[idx]);
                Marshal.WriteByte(bits, idx + 1, px[idx + 1]);
                Marshal.WriteByte(bits, idx + 2, px[idx + 2]);
                Marshal.WriteByte(bits, idx + 3, px[idx + 3]);
            }
            oldBmp = SelectObject(dc, bmp);
            mask = CreateBitmap(Size, Size, 1, 1, nint.Zero);
            if (mask == nint.Zero) return nint.Zero;
            var info = new ICONINFO { Icon = true, X = 0, Y = 0, hbmMask = mask, hbmColor = bmp };
            return CreateIconIndirect(ref info);
        }
        catch { return nint.Zero; }
        finally
        {
            if (oldBmp != nint.Zero) SelectObject(dc, oldBmp);
            if (dc != nint.Zero) DeleteDC(dc);
        }
    }

    /// <summary>导出 32×32 BMP(预览用)。</summary>
    public static void DumpBmp(string digits, string? icoPath, Layout layout, string outPath)
    {
        var px = Compose(digits, icoPath, layout);
        using var fs = new FileStream(outPath, FileMode.Create, FileAccess.Write);
        using var w = new BinaryWriter(fs);
        const int hdr = 14 + 40;
        w.Write((byte)'B'); w.Write((byte)'M');
        w.Write(hdr + px.Length);
        w.Write(0); w.Write(hdr);
        w.Write(40);
        w.Write(Size); w.Write(Size);
        w.Write((short)1); w.Write((short)32);
        w.Write(0); w.Write(px.Length);
        w.Write(2835); w.Write(2835);
        w.Write(0); w.Write(0);
        // 自上而下 → BMP 是自下而上
        for (var y = Size - 1; y >= 0; y--)
            w.Write(px, y * Size * 4, Size * 4);
    }
}
