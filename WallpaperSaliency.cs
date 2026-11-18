using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace SyLO;

/// <summary>
/// Membaca wallpaper yang sedang tampil (Wallpaper Engine, atau wallpaper Windows biasa)
/// dan membuat peta "bagian menonjol" (karakter / objek utama) supaya lirik bisa menghindarinya.
/// </summary>
public sealed class WallpaperSaliency
{
    public const int GW = 64, GH = 40;

    volatile float[,]? _map;
    volatile Color[,]? _colors;
    DateTime _lastUtc = DateTime.MinValue;
    Rectangle _lastScreen;
    volatile bool _busy;

    /// <summary>Jarak minimal antar pengecekan wallpaper.</summary>
    public double MinIntervalSeconds { get; set; } = 3;

    /// <summary>Dipanggil (dari thread background) saat isi wallpaper berubah cukup jauh.</summary>
    public event Action? Changed;

    /// <summary>Peta 0..1 (1 = paling menonjol), atau null jika belum ada.</summary>
    public float[,]? Map => _map;

    // ---- Gambar wallpaper gabungan (diisi sedikit demi sedikit dari bagian desktop yang terlihat) ----
    const int CW = 640;
    Bitmap? _composite;
    bool[,]? _known;
    Rectangle _compositeScreen;

    readonly DepthEstimator _depth = new();
    volatile DepthMap? _depthMap;
    bool _depthDirty = true;

    /// <summary>Jalankan model AI kedalaman (untuk efek lirik lewat di belakang objek).</summary>
    public bool DepthEnabled { get; set; }

    /// <summary>Peta kedalaman wallpaper terakhir (null jika belum ada / model tidak aktif).</summary>
    public DepthMap? DepthMap => _depthMap;

    volatile PerspectiveInfo? _perspective;
    bool _perspectiveDirty = true;

    /// <summary>Cari titik hilang & garis perspektif di wallpaper (untuk gaya "Garis perspektif").</summary>
    public bool PerspectiveEnabled { get; set; }

    /// <summary>Titik hilang & garis perspektif terakhir (null jika tidak ditemukan).</summary>
    public PerspectiveInfo? Perspective => _perspective;

    readonly object _compLock = new();
    volatile System.Windows.Media.Imaging.BitmapSource? _snapshot;

    /// <summary>Gambar wallpaper gabungan terakhir (untuk latar editor perspektif), atau null.</summary>
    public System.Windows.Media.Imaging.BitmapSource? Snapshot => _snapshot;

    /// <summary>Jalankan deteksi titik hilang sekarang juga pada wallpaper yang sudah terbaca.</summary>
    public PerspectiveInfo? AnalyzePerspectiveNow()
    {
        lock (_compLock)
        {
            if (_composite == null) return _perspective;
            var p = PerspectiveAnalyzer.Analyze(_composite);
            if (p != null) _perspective = p;
            return p;
        }
    }

    static System.Windows.Media.Imaging.BitmapSource ToBitmapSource(Bitmap b)
    {
        var data = b.LockBits(new Rectangle(0, 0, b.Width, b.Height), System.Drawing.Imaging.ImageLockMode.ReadOnly,
            System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            var bs = System.Windows.Media.Imaging.BitmapSource.Create(b.Width, b.Height, 96, 96,
                System.Windows.Media.PixelFormats.Bgra32, null, data.Scan0, data.Stride * b.Height, data.Stride);
            bs.Freeze(); // supaya bisa dipakai di thread UI
            return bs;
        }
        finally { b.UnlockBits(data); }
    }

    /// <summary>Perbarui peta & warna di background.</summary>
    public void Refresh(Rectangle screen, bool force = false)
    {
        if (_busy) return;
        if (!force && screen == _lastScreen && (DateTime.UtcNow - _lastUtc).TotalSeconds < MinIntervalSeconds) return;
        _busy = true;
        _lastScreen = screen;
        bool wantDepth = DepthEnabled;
        Task.Run(() =>
        {
            try
            {
                lock (_compLock) { RefreshLocked(screen, wantDepth); }
            }
            catch { }
            finally
            {
                _lastUtc = DateTime.UtcNow;
                _busy = false;
            }
        });
    }

    void RefreshLocked(Rectangle screen, bool wantDepth)
    {
        {
            {
                if (!UpdateComposite(screen)) return;
                _snapshot = ToBitmapSource(_composite!);
                var (map, colors) = Compute(_composite!);
                var known = _known!;
                for (int y = 0; y < GH; y++)
                for (int x = 0; x < GW; x++)
                    if (!known[x, y]) map[x, y] = 0; // bagian yang belum pernah terlihat dianggap tidak menonjol

                var old = _colors;
                _map = map;
                _colors = colors;
                bool changed = old == null || ColorDistance(old, colors) > 10;
                if (changed) { _depthDirty = true; _perspectiveDirty = true; }

                if (PerspectiveEnabled && _perspectiveDirty && KnownFraction() > 0.3)
                {
                    _perspective = PerspectiveAnalyzer.Analyze(_composite!);
                    _perspectiveDirty = false;
                    changed = true;
                }

                if (wantDepth && (_depthDirty || _depthMap == null) && KnownFraction() > 0.3)
                {
                    _depthMap = _depth.Estimate(_composite!);
                    _depthDirty = false;
                    changed = true;
                }
                if (changed) Changed?.Invoke();
            }
        }
    }

    double KnownFraction()
    {
        var k = _known;
        if (k == null) return 0;
        int n = 0;
        foreach (var v in k) if (v) n++;
        return n / (double)(GW * GH);
    }

    /// <summary>
    /// Ambil gambar wallpaper terbaru lalu gabungkan ke <see cref="_composite"/>.
    /// 1) Wallpaper Engine lewat PrintWindow (berhasil untuk wallpaper video),
    /// 2) kalau gagal (wallpaper DirectX/scene): potret layar, tapi hanya sel yang benar-benar menampilkan desktop,
    /// 3) kalau Wallpaper Engine tidak jalan: file wallpaper Windows biasa.
    /// </summary>
    bool UpdateComposite(Rectangle screen)
    {
        int ch = Math.Max(1, CW * screen.Height / Math.Max(1, screen.Width));
        bool[,] visible;
        Bitmap? frame = null;
        try
        {
            using (var we = CaptureWallpaperEngine(screen))
                if (we != null && LooksValid(we)) frame = Resize(we, CW, ch);

            if (frame != null) visible = AllTrue();
            else if (WallpaperEngineRunning())
            {
                visible = VisibleDesktopCells(screen);
                if (!Any(visible)) return _composite != null; // desktop tertutup semua: pakai data lama
                frame = CaptureScreen(screen, CW, ch);
            }
            else
            {
                using var st = LoadStaticWallpaper(screen);
                if (st == null) return _composite != null;
                frame = Resize(st, CW, ch);
                visible = AllTrue();
            }

            if (_composite == null || _compositeScreen != screen || _composite.Height != ch)
            {
                // mulai dari gambar hitam; hanya bagian yang benar-benar menampilkan desktop yang disalin
                // (jendela lain / taskbar tidak boleh ikut terbaca sebagai wallpaper)
                _composite?.Dispose();
                _composite = new Bitmap(CW, ch, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                using (var g0 = Graphics.FromImage(_composite)) g0.Clear(Color.Black);
                _known = new bool[GW, GH];
                _compositeScreen = screen;
            }

            using (var g = Graphics.FromImage(_composite))
            {
                for (int y = 0; y < GH; y++)
                for (int x = 0; x < GW; x++)
                {
                    if (!visible[x, y]) continue;
                    var cell = CellRect(x, y, CW, ch);
                    g.DrawImage(frame, cell, cell, GraphicsUnit.Pixel);
                    _known![x, y] = true;
                }
            }
            return true;
        }
        finally
        {
            frame?.Dispose();
        }
    }

    static Rectangle CellRect(int x, int y, int w, int h)
    {
        int x0 = x * w / GW, x1 = (x + 1) * w / GW, y0 = y * h / GH, y1 = (y + 1) * h / GH;
        return new Rectangle(x0, y0, Math.Max(1, x1 - x0), Math.Max(1, y1 - y0));
    }

    static bool[,] AllTrue()
    {
        var a = new bool[GW, GH];
        for (int y = 0; y < GH; y++) for (int x = 0; x < GW; x++) a[x, y] = true;
        return a;
    }

    static bool Any(bool[,] a)
    {
        foreach (var v in a) if (v) return true;
        return false;
    }

    static Bitmap Resize(Bitmap src, int w, int h)
    {
        var b = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(b);
        g.InterpolationMode = InterpolationMode.HighQualityBilinear;
        g.DrawImage(src, 0, 0, w, h);
        return b;
    }

    /// <summary>PrintWindow pada wallpaper DirectX kadang menghasilkan gambar kosong/transparan.</summary>
    static bool LooksValid(Bitmap b)
    {
        int opaque = 0;
        Color? first = null;
        bool varied = false;
        for (int i = 0; i < 64; i++)
        {
            var p = b.GetPixel((i % 8 * 2 + 1) * b.Width / 16, (i / 8 * 2 + 1) * b.Height / 16);
            if (p.A > 0) opaque++;
            first ??= p;
            if (Math.Abs(p.R - first.Value.R) + Math.Abs(p.G - first.Value.G) + Math.Abs(p.B - first.Value.B) > 12) varied = true;
        }
        return opaque > 48 && varied;
    }

    static bool WallpaperEngineRunning()
    {
        foreach (var p in Process.GetProcesses())
        {
            try { if (p.ProcessName.StartsWith("wallpaper", StringComparison.OrdinalIgnoreCase)) return true; }
            catch { }
            finally { p.Dispose(); }
        }
        return false;
    }

    /// <summary>Potret layar (hasil akhir DWM, termasuk wallpaper DirectX). Jendela layered seperti overlay SyLO tidak ikut.</summary>
    static Bitmap CaptureScreen(Rectangle screen, int w, int h)
    {
        var bmp = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        var dst = g.GetHdc();
        var src = GetDC(IntPtr.Zero);
        try
        {
            SetStretchBltMode(dst, HALFTONE);
            StretchBlt(dst, 0, 0, w, h, src, screen.X, screen.Y, screen.Width, screen.Height, SRCCOPY);
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, src);
            g.ReleaseHdc(dst);
        }
        return bmp;
    }

    /// <summary>Sel mana di layar yang saat ini benar-benar menampilkan desktop (tidak tertutup jendela/taskbar).</summary>
    static bool[,] VisibleDesktopCells(Rectangle screen)
    {
        var v = new bool[GW, GH];
        var cache = new Dictionary<IntPtr, bool>();
        for (int y = 0; y < GH; y++)
        for (int x = 0; x < GW; x++)
        {
            var pt = new POINT
            {
                X = screen.X + (int)((x + 0.5) * screen.Width / GW),
                Y = screen.Y + (int)((y + 0.5) * screen.Height / GH)
            };
            v[x, y] = IsDesktopAt(pt, cache);
        }
        return v;
    }

    /// <summary>True jika di area ini (dalam piksel layar) yang terlihat adalah desktop/wallpaper.</summary>
    public static bool IsDesktopVisible(Rectangle r)
    {
        var cache = new Dictionary<IntPtr, bool>();
        int ok = 0;
        for (int i = 0; i < 9; i++)
        {
            var pt = new POINT { X = r.X + (i % 3 * 2 + 1) * r.Width / 6, Y = r.Y + (i / 3 * 2 + 1) * r.Height / 6 };
            if (IsDesktopAt(pt, cache)) ok++;
        }
        return ok >= 8;
    }

    static bool IsDesktopAt(POINT pt, Dictionary<IntPtr, bool> cache)
    {
        var h = WindowFromPoint(pt); // jendela click-through (seperti overlay SyLO) dilewati otomatis
        if (h == IntPtr.Zero) return false;
        var root = GetAncestor(h, GA_ROOT);
        if (!cache.TryGetValue(root, out var isDesk))
        {
            var cls = ClassName(root);
            isDesk = cls is "Progman" or "WorkerW";
            cache[root] = isDesk;
        }
        return isDesk;
    }

    /// <summary>Rata-rata "kemenonjolan" di area teks (fraksi layar 0..1).</summary>
    public static double Cost(float[,] map, double left, double top, double width, double height)
    {
        int x0 = Math.Clamp((int)(left * GW), 0, GW - 1), x1 = Math.Clamp((int)Math.Ceiling((left + width) * GW), x0 + 1, GW);
        int y0 = Math.Clamp((int)(top * GH), 0, GH - 1), y1 = Math.Clamp((int)Math.Ceiling((top + height) * GH), y0 + 1, GH);
        double sum = 0;
        for (int y = y0; y < y1; y++)
        for (int x = x0; x < x1; x++) sum += map[x, y];
        return sum / ((x1 - x0) * (y1 - y0));
    }

    /// <summary>
    /// Warna dominan wallpaper di sekitar area teks (diperluas sedikit), atau seluruh layar jika area itu tidak berwarna.
    /// </summary>
    public System.Windows.Media.Color? DominantAround(double left, double top, double width, double height)
    {
        var colors = _colors;
        if (colors == null) return null;

        double mx = width * 0.5, my = height * 0.8; // ambil juga sekelilingnya
        int x0 = Math.Clamp((int)((left - mx) * GW), 0, GW - 1), x1 = Math.Clamp((int)Math.Ceiling((left + width + mx) * GW), x0 + 1, GW);
        int y0 = Math.Clamp((int)((top - my) * GH), 0, GH - 1), y1 = Math.Clamp((int)Math.Ceiling((top + height + my) * GH), y0 + 1, GH);

        var local = new List<Color>();
        for (int y = y0; y < y1; y++)
        for (int x = x0; x < x1; x++) local.Add(colors[x, y]);

        return AlbumColor.FromPixels(local, 0.06, 0.12) ?? AlbumColor.FromPixels(All(colors), 0.06, 0.12);
    }

    static IEnumerable<Color> All(Color[,] c)
    {
        for (int y = 0; y < GH; y++)
        for (int x = 0; x < GW; x++) yield return c[x, y];
    }

    static double ColorDistance(Color[,] a, Color[,] b)
    {
        double sum = 0;
        for (int y = 0; y < GH; y++)
        for (int x = 0; x < GW; x++)
            sum += Math.Abs(a[x, y].R - b[x, y].R) + Math.Abs(a[x, y].G - b[x, y].G) + Math.Abs(a[x, y].B - b[x, y].B);
        return sum / (GW * GH * 3);
    }

    // ---------------- Analisis gambar ----------------

    static (float[,] Map, Color[,] Colors) Compute(Bitmap src)
    {
        using var small = new Bitmap(GW, GH);
        using (var g = Graphics.FromImage(small))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
            g.DrawImage(src, 0, 0, GW, GH);
        }

        var colors = new Color[GW, GH];
        var lab = new (double L, double A, double B)[GW, GH];
        double mL = 0, mA = 0, mB = 0;
        for (int y = 0; y < GH; y++)
        for (int x = 0; x < GW; x++)
        {
            var p = small.GetPixel(x, y);
            colors[x, y] = p;
            var c = ToLab(p.R, p.G, p.B);
            lab[x, y] = c;
            mL += c.L; mA += c.A; mB += c.B;
        }
        int n = GW * GH;
        mL /= n; mA /= n; mB /= n;

        // Saliency = beda warna dari rata-rata gambar + tepi (kontras lokal)
        var raw = new double[GW, GH];
        double max = 1e-6;
        for (int y = 0; y < GH; y++)
        for (int x = 0; x < GW; x++)
        {
            var c = lab[x, y];
            double colorDiff = Math.Sqrt(Sq(c.L - mL) * 0.6 + Sq(c.A - mA) + Sq(c.B - mB));
            double edge = 0;
            if (x > 0 && x < GW - 1 && y > 0 && y < GH - 1)
                edge = Math.Abs(lab[x + 1, y].L - lab[x - 1, y].L) + Math.Abs(lab[x, y + 1].L - lab[x, y - 1].L);
            raw[x, y] = colorDiff + edge * 0.5;
            max = Math.Max(max, raw[x, y]);
        }

        // haluskan 3x3 lalu normalisasi; pangkat 1.5 supaya area tenang benar-benar "murah"
        var map = new float[GW, GH];
        for (int y = 0; y < GH; y++)
        for (int x = 0; x < GW; x++)
        {
            double s = 0; int k = 0;
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int xx = x + dx, yy = y + dy;
                if (xx < 0 || yy < 0 || xx >= GW || yy >= GH) continue;
                s += raw[xx, yy]; k++;
            }
            map[x, y] = (float)Math.Pow(s / k / max, 1.5);
        }
        return (map, colors);
    }

    static double Sq(double v) => v * v;

    static (double L, double A, double B) ToLab(byte r8, byte g8, byte b8)
    {
        static double Lin(byte c) { double v = c / 255.0; return v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4); }
        double r = Lin(r8), g = Lin(g8), b = Lin(b8);
        double X = (r * 0.4124 + g * 0.3576 + b * 0.1805) / 0.95047;
        double Y = r * 0.2126 + g * 0.7152 + b * 0.0722;
        double Z = (r * 0.0193 + g * 0.1192 + b * 0.9505) / 1.08883;
        static double F(double t) => t > 0.008856 ? Math.Cbrt(t) : 7.787 * t + 16.0 / 116;
        double fx = F(X), fy = F(Y), fz = F(Z);
        return (116 * fy - 16, 500 * (fx - fy), 200 * (fy - fz));
    }

    // ---------------- Ambil gambar wallpaper ----------------

    /// <summary>Ambil gambar wallpaper yang sedang tampil di layar ini (Wallpaper Engine, lalu wallpaper biasa).</summary>
    public static Bitmap? Capture(Rectangle screen) => CaptureWallpaperEngine(screen) ?? LoadStaticWallpaper(screen);

    /// <summary>Wallpaper Engine merender di jendela miliknya di belakang ikon desktop (di bawah Progman/WorkerW).</summary>
    static Bitmap? CaptureWallpaperEngine(Rectangle screen)
    {
        var weIds = new HashSet<uint>();
        foreach (var p in Process.GetProcesses())
        {
            try { if (p.ProcessName.StartsWith("wallpaper", StringComparison.OrdinalIgnoreCase)) weIds.Add((uint)p.Id); }
            catch { }
            finally { p.Dispose(); }
        }
        if (weIds.Count == 0) return null;

        IntPtr found = IntPtr.Zero;
        var hosts = new List<IntPtr>();
        EnumWindows((h, _) =>
        {
            var cls = ClassName(h);
            if (cls is "Progman" or "WorkerW") hosts.Add(h);
            return true;
        }, IntPtr.Zero);

        foreach (var host in hosts)
        {
            EnumChildWindows(host, (h, _) =>
            {
                GetWindowThreadProcessId(h, out var pid);
                if (weIds.Contains(pid) && IsWindowVisible(h) && GetWindowRect(h, out var r) && r.R - r.L > 200)
                {
                    found = h;
                    return false;
                }
                return true;
            }, IntPtr.Zero);
            if (found != IntPtr.Zero) break;
        }
        if (found == IntPtr.Zero) return null;

        GetWindowRect(found, out var wr);
        int w = wr.R - wr.L, hgt = wr.B - wr.T;
        if (w <= 0 || hgt <= 0) return null;

        using var full = new Bitmap(w, hgt);
        using (var g = Graphics.FromImage(full))
        {
            var dc = g.GetHdc();
            try { if (!PrintWindow(found, dc, PW_RENDERFULLCONTENT)) return null; }
            finally { g.ReleaseHdc(dc); }
        }

        // potong bagian monitor target (jendela WE bisa membentang beberapa monitor)
        var crop = Rectangle.Intersect(new Rectangle(screen.X - wr.L, screen.Y - wr.T, screen.Width, screen.Height),
            new Rectangle(0, 0, w, hgt));
        if (crop.Width < 50 || crop.Height < 50) return null;
        return full.Clone(crop, full.PixelFormat);
    }

    /// <summary>Wallpaper Windows biasa (diasumsikan mode "Fill": diperbesar lalu dipotong di tengah).</summary>
    static Bitmap? LoadStaticWallpaper(Rectangle screen)
    {
        var sb = new StringBuilder(520);
        if (!SystemParametersInfo(SPI_GETDESKWALLPAPER, sb.Capacity, sb, 0)) return null;
        var path = sb.ToString();
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;

        using var img = Image.FromFile(path);
        double scale = Math.Max((double)screen.Width / img.Width, (double)screen.Height / img.Height);
        int sw = (int)(screen.Width / scale), sh = (int)(screen.Height / scale);
        var srcRect = new Rectangle((img.Width - sw) / 2, (img.Height - sh) / 2, sw, sh);
        var bmp = new Bitmap(GW * 4, GH * 4);
        using var g = Graphics.FromImage(bmp);
        g.DrawImage(img, new Rectangle(0, 0, bmp.Width, bmp.Height), srcRect, GraphicsUnit.Pixel);
        return bmp;
    }

    // ---------------- Win32 ----------------

    const uint PW_RENDERFULLCONTENT = 2;
    const int SRCCOPY = 0x00CC0020;
    const int HALFTONE = 4;
    const uint GA_ROOT = 2;

    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }

    [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(POINT p);
    [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr h, uint flags);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
    [DllImport("gdi32.dll")] static extern int SetStretchBltMode(IntPtr hdc, int mode);
    [DllImport("gdi32.dll")]
    static extern bool StretchBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, int sw, int sh, int rop);
    const uint SPI_GETDESKWALLPAPER = 0x0073;

    delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }

    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr parent, EnumProc cb, IntPtr lParam);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr hWnd, IntPtr hdc, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hWnd, StringBuilder sb, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern bool SystemParametersInfo(uint action, int param, StringBuilder sb, uint winIni);

    static string ClassName(IntPtr h)
    {
        var sb = new StringBuilder(64);
        GetClassName(h, sb, sb.Capacity);
        return sb.ToString();
    }
}
