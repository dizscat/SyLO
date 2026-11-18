using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace SyLO;

/// <summary>
/// Teks ber-outline yang muncul huruf demi huruf (fade + naik pelan, ease-out).
/// Posisi tiap huruf diambil dari layout teks utuh, jadi spasi & kerning tetap rapi.
/// </summary>
public sealed class RevealText : FrameworkElement
{
    public static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(
        nameof(Progress), typeof(double), typeof(RevealText),
        new FrameworkPropertyMetadata(double.MaxValue, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Jumlah huruf yang sudah "dibuka" (boleh pecahan). double.MaxValue = tampil semua.</summary>
    public double Progress { get => (double)GetValue(ProgressProperty); set => SetValue(ProgressProperty, value); }

    public string Text { get; init; } = "";
    public FontFamily TextFontFamily { get; init; } = new("Segoe UI");
    public double TextFontSize { get; init; } = 32;
    public FontWeight TextFontWeight { get; init; } = FontWeights.Bold;
    public Brush Fill { get; init; } = Brushes.White;
    public Brush Stroke { get; init; } = Brushes.Black;
    public double StrokeThickness { get; init; } = 2;
    public double MaxLineWidth { get; init; } = 1000;
    /// <summary>Seberapa jauh huruf naik saat muncul (px).</summary>
    public double Rise { get; init; } = 14;
    /// <summary>Berapa huruf yang sedang memudar bersamaan (makin besar makin halus).</summary>
    public double Spread { get; init; } = 3;
    /// <summary>Cahaya di sekitar huruf ("menyala"). Strength 0 = mati.</summary>
    /// <summary>Warna cahaya bagian luar.</summary>
    public Color GlowColor { get; init; } = Colors.White;
    /// <summary>Warna cahaya bagian dalam (null = sama dengan <see cref="GlowColor"/>).</summary>
    public Color? GlowInnerColor { get; init; }
    public double GlowStrength { get; init; } = 0;
    public double GlowSize { get; init; } = 0;

    /// <summary>
    /// Tata letak khusus (mis. mengikuti garis perspektif). Menerima kotak tiap huruf pada tata letak lurus + posisi
    /// baseline, lalu mengembalikan matriks per huruf dan ukuran total. Null = teks lurus biasa.
    /// </summary>
    public Func<IReadOnlyList<Rect>, double, (Matrix[] Mats, Size Size)>? Layout { get; init; }

    Matrix[]? _mats;

    readonly List<Geometry> _glyphs = new();
    Size _size;
    Pen? _pen;
    readonly List<Pen> _glowPens = new();
    Pen? _rimPen;

    public int GlyphCount => _glyphs.Count;

    FormattedText Make(string s, double maxWidth)
    {
        var ft = new FormattedText(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(TextFontFamily, FontStyles.Normal, TextFontWeight, FontStretches.Normal),
            Math.Max(1, TextFontSize), Fill, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        if (maxWidth > 0) ft.MaxTextWidth = maxWidth;
        return ft;
    }

    void Prepare()
    {
        _glyphs.Clear();
        bool glow = GlowStrength > 0.01 && GlowSize > 0.5;
        double pad = StrokeThickness + (glow ? GlowSize : 0);
        var text = string.IsNullOrWhiteSpace(Text) ? "♪" : Text;
        var full = Make(text, Math.Max(50, MaxLineWidth));
        var origin = new Point(pad, pad + Rise);

        var e = StringInfo.GetTextElementEnumerator(text);
        while (e.MoveNext())
        {
            var el = e.GetTextElement();
            if (string.IsNullOrWhiteSpace(el)) continue;
            var hl = full.BuildHighlightGeometry(origin, e.ElementIndex, el.Length);
            if (hl == null) continue;
            var b = hl.Bounds;
            var geo = Make(el, 0).BuildGeometry(new Point(b.Left, b.Top));
            geo.Freeze();
            _glyphs.Add(geo);
        }

        _size = new Size(full.WidthIncludingTrailingWhitespace + pad * 2, full.Height + pad * 2 + Rise);
        _pen = StrokeThickness > 0 ? new Pen(Stroke, StrokeThickness * 2) { LineJoin = PenLineJoin.Round } : null;
        _pen?.Freeze();

        _inner = GlowInnerColor ?? GlowColor;
        _outer = GlowColor;
        BuildGlowPens();
        BuildSprites();

        _mats = null;
        if (Layout != null && _sprites.Count > 0)
        {
            // baseline akhir (setelah animasi naik selesai) = pad + baseline font
            var (mats, size) = Layout(_sprites.Select(s => s.Rect).ToList(), pad + full.Baseline);
            _mats = mats;
            _size = size;
        }
    }

    Color _inner, _outer;

    /// <summary>Ganti warna cahaya tanpa membangun ulang teks (dipakai saat wallpaper berubah).</summary>
    public void SetGlow(Color inner, Color outer)
    {
        if (inner == _inner && outer == _outer) return;
        _inner = inner;
        _outer = outer;
        BuildGlowPens();
        BuildSprites();
        InvalidateVisual();
    }

    readonly List<(BitmapSource Image, Rect Rect)> _sprites = new();

    /// <summary>
    /// Gambar tiap huruf (cahaya + outline + isi) sekali saja ke bitmap. Saat animasi, cukup bitmap-nya yang
    /// digambar dengan transparansi berbeda — jauh lebih ringan daripada menggambar ulang semua lapisan cahaya tiap frame.
    /// </summary>
    void BuildSprites()
    {
        _sprites.Clear();
        double dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        double margin = StrokeThickness + (_glowPens.Count > 0 ? GlowSize : 0) + 2;

        foreach (var g in _glyphs)
        {
            var b = g.Bounds;
            if (b.IsEmpty) { _sprites.Add((new WriteableBitmap(1, 1, 96, 96, PixelFormats.Pbgra32, null), Rect.Empty)); continue; }
            b.Inflate(margin, margin);

            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.PushTransform(new TranslateTransform(-b.X, -b.Y));
                foreach (var gp in _glowPens) dc.DrawGeometry(null, gp, g);
                if (_pen != null) dc.DrawGeometry(null, _pen, g);
                // outline ikut terkena cahaya, jadi tidak ada cincin gelap di antara isi huruf dan cahayanya
                if (_rimPen != null) dc.DrawGeometry(null, _rimPen, g);
                dc.DrawGeometry(Fill, null, g);
                dc.Pop();
            }

            var rtb = new RenderTargetBitmap(Math.Max(1, (int)Math.Ceiling(b.Width * dpi)), Math.Max(1, (int)Math.Ceiling(b.Height * dpi)),
                96 * dpi, 96 * dpi, PixelFormats.Pbgra32);
            rtb.Render(dv);
            rtb.Freeze();
            _sprites.Add((rtb, b));
        }
    }

    /// <summary>
    /// Cahaya = beberapa lapis garis lebar semi-transparan (lebih ringan daripada efek blur).
    /// Lapisan dalam berwarna <see cref="_inner"/> lalu bertransisi ke <see cref="_outer"/> di lapisan terluar.
    /// </summary>
    void BuildGlowPens()
    {
        _glowPens.Clear();
        _rimPen = null;
        if (!(GlowStrength > 0.01 && GlowSize > 0.5)) return;

        // lapisan paling dalam menimpa outline, jadi huruf terlihat menyala dari dalam
        var rimBrush = new SolidColorBrush(Color.FromArgb((byte)Math.Clamp(255 * GlowStrength * 0.75, 0, 255),
            _inner.R, _inner.G, _inner.B));
        rimBrush.Freeze();
        _rimPen = new Pen(rimBrush, Math.Max(1.5, StrokeThickness * 2)) { LineJoin = PenLineJoin.Round };
        _rimPen.Freeze();

        const int layers = 9;
        for (int k = layers; k >= 1; k--)
        {
            double t = (double)k / layers;               // 1 = lapisan terluar
            var c = Mix(_inner, _outer, Math.Pow(t, 0.8)); // transisi warna dalam -> luar
            byte alpha = (byte)Math.Clamp(255 * GlowStrength * 0.2 * (1.15 - t), 0, 255);
            var brush = new SolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B));
            brush.Freeze();
            var p = new Pen(brush, StrokeThickness * 2 + GlowSize * 2 * t) { LineJoin = PenLineJoin.Round };
            p.Freeze();
            _glowPens.Add(p);
        }
    }

    static Color Mix(Color a, Color b, double t) => Color.FromRgb(
        (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

    protected override Size MeasureOverride(Size availableSize)
    {
        Prepare();
        return _size;
    }

    protected override void OnRender(DrawingContext dc)
    {
        double progress = Progress;
        for (int i = 0; i < _glyphs.Count; i++)
        {
            double t = Math.Clamp((progress - i) / Spread, 0, 1);
            if (t <= 0) continue;
            double eased = 1 - Math.Pow(1 - t, 3); // ease-out
            bool partial = eased < 1;
            bool placed = _mats != null && i < _mats.Length;
            if (placed) dc.PushTransform(new MatrixTransform(_mats![i]));
            if (partial)
            {
                dc.PushTransform(new TranslateTransform(0, -Rise * eased));
                dc.PushOpacity(eased);
            }
            else dc.PushTransform(new TranslateTransform(0, -Rise));

            if (i < _sprites.Count) dc.DrawImage(_sprites[i].Image, _sprites[i].Rect);

            dc.Pop();
            if (partial) dc.Pop();
            if (placed) dc.Pop();
        }
    }

    /// <summary>Mulai animasi muncul: satu huruf tiap <paramref name="perCharMs"/> ms.</summary>
    public void StartReveal(double perCharMs)
    {
        // mulai dari setengah 'Spread' supaya huruf pertama langsung terlihat (tidak terasa telat)
        double start = Spread * 0.5;
        double end = _glyphs.Count + Spread;
        var anim = new DoubleAnimation(start, end, TimeSpan.FromMilliseconds((end - start) * perCharMs));
        Timeline.SetDesiredFrameRate(anim, 30);
        BeginAnimation(ProgressProperty, anim);
    }
}
