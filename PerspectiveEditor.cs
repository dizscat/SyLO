using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SyLO;

/// <summary>
/// Editor titik hilang untuk gaya "Garis perspektif": seret titik di atas gambar wallpaper,
/// pilih preset 1–4 titik, tambah/hapus titik, atur jumlah garis, dan lihat contoh lirik langsung.
/// </summary>
public sealed class PerspectiveEditor : Window
{
    const double ViewScale = 0.62;   // area layar digambar 62% supaya titik di luar layar tetap terlihat
    const int MaxPoints = 6;
    static readonly Color[] Palette =
    {
        Color.FromRgb(0xFF, 0x5A, 0x5F), Color.FromRgb(0x4F, 0xC3, 0xF7), Color.FromRgb(0xFF, 0xD5, 0x4F),
        Color.FromRgb(0x81, 0xC7, 0x84), Color.FromRgb(0xBA, 0x68, 0xC8), Color.FromRgb(0xFF, 0x8A, 0x65)
    };

    readonly AppSettings _s;
    readonly WallpaperSaliency _sal;
    readonly Canvas _canvas = new() { ClipToBounds = true, Background = Brushes.Transparent };
    readonly Canvas _textLayer = new() { IsHitTestVisible = false };
    readonly TextBlock _status = new() { Foreground = Brushes.Gainsboro, FontSize = 12, Margin = new Thickness(0, 6, 0, 0) };
    readonly TextBlock _rayValue = new() { Foreground = Brushes.White, Width = 28, VerticalAlignment = VerticalAlignment.Center };

    List<Point> _pts;
    int _rays;
    bool _steep;
    int _seed = 7;
    int _drag = -1;
    Rect _r; // area layar di dalam kanvas

    double MinAbsX => _steep ? 0.12 : 0.35;

    public PerspectiveEditor(AppSettings settings, WallpaperSaliency saliency)
    {
        _s = settings;
        _sal = saliency;
        _pts = PerspectiveLayout.ParsePoints(settings.PerspectivePoints);
        if (_pts.Count == 0) _pts = PerspectiveLayout.Preset(1);
        _rays = settings.PerspectiveRays;
        _steep = settings.PerspectiveSteep;

        Title = Loc.T("Editor perspektif — SyLO");
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        Background = new SolidColorBrush(Color.FromRgb(0x0E, 0x0E, 0x12));
        FontFamily = new FontFamily("Segoe UI");

        var root = new Grid();
        root.Children.Add(_canvas);
        root.Children.Add(_textLayer);
        root.Children.Add(BuildToolbar());
        root.Children.Add(BuildHelp());
        Content = root;

        Loaded += (_, _) => WindowState = WindowState.Maximized;
        SizeChanged += (_, _) => Redraw(true);
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Close();
            else if (e.Key == Key.Enter) Save();
        };

        _canvas.MouseLeftButtonDown += Canvas_MouseDown;
        _canvas.MouseMove += Canvas_MouseMove;
        _canvas.MouseLeftButtonUp += (_, _) => { if (_drag >= 0) { _drag = -1; _canvas.ReleaseMouseCapture(); Redraw(true); } };
        _canvas.MouseRightButtonDown += Canvas_RightDown;

        // gambar wallpaper bisa datang belakangan (dibaca di background)
        Action onChanged = () => Dispatcher.BeginInvoke(() => Redraw(true));
        _sal.Changed += onChanged;
        Closed += (_, _) => _sal.Changed -= onChanged;
    }

    // ---------------- UI ----------------

    FrameworkElement BuildToolbar()
    {
        var bar = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center };
        foreach (var n in new[] { 1, 2, 3, 4 })
        {
            int count = n;
            bar.Children.Add(Btn(Loc.F("{0} titik", n), () => { _pts = PerspectiveLayout.Preset(count); Status(Loc.F("Preset perspektif {0} titik.", count)); Redraw(true); }));
        }
        bar.Children.Add(Btn(Loc.T("Deteksi otomatis"), AutoDetect));
        bar.Children.Add(new TextBlock { Text = Loc.T("Garis per titik"), Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 6, 0) });
        var slider = new Slider { Minimum = 4, Maximum = 36, Value = _rays, Width = 130, IsSnapToTickEnabled = true, TickFrequency = 1, VerticalAlignment = VerticalAlignment.Center };
        slider.ValueChanged += (_, e) => { _rays = (int)e.NewValue; _rayValue.Text = _rays.ToString(); Redraw(true); };
        _rayValue.Text = _rays.ToString();
        bar.Children.Add(slider);
        bar.Children.Add(_rayValue);
        var steep = new CheckBox { Content = Loc.T("Izinkan garis tegak"), IsChecked = _steep, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 10, 0) };
        steep.Checked += (_, _) => { _steep = true; Redraw(true); };
        steep.Unchecked += (_, _) => { _steep = false; Redraw(true); };
        bar.Children.Add(steep);
        bar.Children.Add(Btn(Loc.T("Contoh lain"), () => { _seed += 2; Redraw(true); }));
        var save = Btn(Loc.T("Simpan & pakai"), Save);
        save.FontWeight = FontWeights.SemiBold;
        save.Background = new SolidColorBrush(Color.FromRgb(0x7C, 0x5C, 0xFF));
        save.Foreground = Brushes.White;
        bar.Children.Add(save);
        bar.Children.Add(Btn(Loc.T("Batal"), Close));

        var panel = new StackPanel();
        panel.Children.Add(bar);
        panel.Children.Add(_status);
        return new Border
        {
            Child = panel,
            Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x1C, 0x1C, 0x24)),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(16),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top
        };
    }

    FrameworkElement BuildHelp() => new Border
    {
        Child = new TextBlock
        {
            Text = Loc.T("Seret titik untuk memindahkan · Klik dua kali di tempat kosong untuk menambah titik · Klik kanan titik untuk menghapus · Enter = simpan · Esc = batal"),
            Foreground = Brushes.Gainsboro,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap
        },
        Background = new SolidColorBrush(Color.FromArgb(0xCC, 0x1C, 0x1C, 0x24)),
        CornerRadius = new CornerRadius(8),
        Padding = new Thickness(10, 6, 10, 6),
        Margin = new Thickness(16),
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Bottom,
        IsHitTestVisible = false
    };

    static Button Btn(string text, Action click)
    {
        var b = new Button { Content = text, Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 6, 4), MinWidth = 54 };
        b.Click += (_, _) => click();
        return b;
    }

    void Status(string text) => _status.Text = text;

    // ---------------- Koordinat ----------------

    Point ToCanvas(Point f) => new(_r.X + f.X * _r.Width, _r.Y + f.Y * _r.Height);
    Point FromCanvas(Point c) => new((c.X - _r.X) / _r.Width, (c.Y - _r.Y) / _r.Height);

    // ---------------- Gambar ----------------

    void Redraw(bool withText)
    {
        double w = _canvas.ActualWidth, h = _canvas.ActualHeight;
        if (w < 10 || h < 10) return;
        // area layar diperkecil supaya titik di luar layar (−0.32 … 1.3) tetap terlihat di antara toolbar & teks bantuan
        const double top = 110, bottom = 60, minF = -0.32, maxF = 1.3;
        double aspect = w / h;
        double rh = Math.Min((h - top - bottom) / (maxF - minF), w * ViewScale / aspect);
        double rw = rh * aspect;
        _r = new Rect((w - rw) / 2, top - minF * rh, rw, rh);
        _canvas.Children.Clear();

        // latar: wallpaper yang sudah terbaca
        var snap = _sal.Snapshot;
        if (snap != null)
        {
            var img = new Image { Source = snap, Width = _r.Width, Height = _r.Height, Stretch = Stretch.Fill, Opacity = 0.9 };
            Place(img, _r.X, _r.Y);
        }
        else
        {
            Place(new Rectangle { Width = _r.Width, Height = _r.Height, Fill = new SolidColorBrush(Color.FromRgb(0x24, 0x24, 0x2C)) }, _r.X, _r.Y);
            var t = new TextBlock
            {
                Text = Loc.T("Wallpaper belum terbaca. Tutup editor, tampilkan desktop (Win+D) beberapa detik, lalu buka lagi."),
                Foreground = Brushes.Gray, Width = _r.Width * 0.7, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center
            };
            Place(t, _r.X + _r.Width * 0.15, _r.Y + _r.Height / 2 - 20);
        }
        Place(new Rectangle { Width = _r.Width, Height = _r.Height, Stroke = Brushes.White, StrokeThickness = 1.5, Opacity = 0.8 }, _r.X, _r.Y);

        // garis horizon (2 titik pertama) sebagai patokan
        if (_pts.Count >= 2) DrawInfinite(ToCanvas(_pts[0]), ToCanvas(_pts[1]) - ToCanvas(_pts[0]), Colors.White, 1, 0.45, true);
        else if (_pts.Count == 1) DrawInfinite(ToCanvas(_pts[0]), new Vector(1, 0), Colors.White, 1, 0.45, true);

        // garis-garis dari tiap titik hilang
        for (int i = 0; i < _pts.Count; i++)
        {
            var c = Palette[i % Palette.Length];
            var vp = ToCanvas(_pts[i]);
            for (int k = 0; k < _rays; k++)
            {
                double a = (k + 0.5) * 2 * Math.PI / _rays;
                var dir = new Vector(Math.Cos(a), Math.Sin(a));
                bool used = Math.Abs(dir.X) >= MinAbsX;
                var line = new Line
                {
                    X1 = vp.X, Y1 = vp.Y, X2 = vp.X + dir.X * 5000, Y2 = vp.Y + dir.Y * 5000,
                    Stroke = new SolidColorBrush(c), StrokeThickness = used ? 1.4 : 1, Opacity = used ? 0.6 : 0.15,
                    IsHitTestVisible = false
                };
                if (!used) line.StrokeDashArray = new DoubleCollection { 4, 4 };
                _canvas.Children.Add(line);
            }
        }

        // pegangan titik
        for (int i = 0; i < _pts.Count; i++)
        {
            var c = Palette[i % Palette.Length];
            var p = ToCanvas(_pts[i]);
            var g = new Grid { Width = 28, Height = 28, Tag = i, Cursor = Cursors.SizeAll };
            g.Children.Add(new Ellipse { Fill = new SolidColorBrush(c), Stroke = Brushes.White, StrokeThickness = 2 });
            g.Children.Add(new TextBlock { Text = (i + 1).ToString(), Foreground = Brushes.Black, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
            g.ToolTip = Loc.T("Titik hilang") + $" {i + 1}";
            Place(g, p.X - 14, p.Y - 14);
        }

        if (withText) DrawSamples();
    }

    void DrawInfinite(Point p, Vector d, Color c, double thick, double opacity, bool dashed)
    {
        if (d.Length < 1e-6) return;
        d.Normalize();
        var line = new Line
        {
            X1 = p.X - d.X * 6000, Y1 = p.Y - d.Y * 6000, X2 = p.X + d.X * 6000, Y2 = p.Y + d.Y * 6000,
            Stroke = new SolidColorBrush(c), StrokeThickness = thick, Opacity = opacity, IsHitTestVisible = false
        };
        if (dashed) line.StrokeDashArray = new DoubleCollection { 6, 4 };
        _canvas.Children.Add(line);
    }

    /// <summary>Contoh dua baris lirik di garis perspektif (memakai font, warna & cahaya dari pengaturan).</summary>
    void DrawSamples()
    {
        _textLayer.Children.Clear();
        var infos = PerspectiveLayout.FromManual(_pts, _rays);
        double key = double.NaN;
        var samples = new[] { Loc.T("Contoh lirik mengikuti garis perspektif"), Loc.T("Baris berikutnya di garis lain") };
        int drawn = 0;
        for (int i = 0; i < samples.Length; i++)
        {
            Point origin = default;
            var layout = PerspectiveLayout.Create(infos, _r.Width, _r.Height, new Random(_seed + i), ref key, o => origin = o, MinAbsX);
            if (layout == null) continue;
            var color = ColorHelper.Parse(_s.TextColor);
            var rt = new RevealText
            {
                Text = samples[i],
                TextFontFamily = new FontFamily(_s.FontFamily),
                TextFontSize = Math.Max(8, _s.FontSize * 1.15 * ViewScale),
                TextFontWeight = _s.Bold ? FontWeights.Bold : FontWeights.Normal,
                Fill = new SolidColorBrush(color),
                Stroke = ColorHelper.Brush(_s.OutlineColor),
                StrokeThickness = _s.OutlineThickness * ViewScale,
                MaxLineWidth = 100000,
                Layout = layout,
                GlowColor = color,
                GlowStrength = _s.GlowStrength,
                GlowSize = _s.GlowSize * ViewScale,
                IsHitTestVisible = false
            };
            _textLayer.Children.Add(rt);
            rt.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(rt, _r.X + origin.X);
            Canvas.SetTop(rt, _r.Y + origin.Y);
            drawn++;
        }
        if (drawn == 0) Status(Loc.T("Tidak ada garis yang cocok untuk lirik di dalam layar. Geser titik atau centang 'Izinkan garis tegak'."));
    }

    void Place(UIElement e, double x, double y)
    {
        Canvas.SetLeft(e, x);
        Canvas.SetTop(e, y);
        _canvas.Children.Add(e);
    }

    // ---------------- Mouse ----------------

    int HitPoint(Point pos)
    {
        for (int i = _pts.Count - 1; i >= 0; i--)
            if ((ToCanvas(_pts[i]) - pos).Length <= 16) return i;
        return -1;
    }

    void Canvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        var pos = e.GetPosition(_canvas);
        int hit = HitPoint(pos);
        if (hit >= 0)
        {
            _drag = hit;
            _canvas.CaptureMouse();
            return;
        }
        if (e.ClickCount == 2)
        {
            if (_pts.Count >= MaxPoints) { Status(Loc.F("Maksimal {0} titik.", MaxPoints)); return; }
            _pts.Add(FromCanvas(pos));
            Status(Loc.F("Titik {0} ditambahkan.", _pts.Count));
            Redraw(true);
        }
    }

    void Canvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (_drag < 0) return;
        var pos = e.GetPosition(_canvas);
        pos.X = Math.Clamp(pos.X, 10, _canvas.ActualWidth - 10);
        pos.Y = Math.Clamp(pos.Y, 100, _canvas.ActualHeight - 56); // jangan sampai masuk ke bawah toolbar / teks bantuan
        _pts[_drag] = FromCanvas(pos);
        Redraw(false); // contoh teks digambar ulang saat tombol dilepas
        _textLayer.Children.Clear();
    }

    void Canvas_RightDown(object sender, MouseButtonEventArgs e)
    {
        int hit = HitPoint(e.GetPosition(_canvas));
        if (hit < 0) return;
        if (_pts.Count <= 1) { Status(Loc.T("Minimal harus ada 1 titik.")); return; }
        _pts.RemoveAt(hit);
        Status(Loc.F("Titik {0} dihapus.", hit + 1));
        Redraw(true);
    }

    // ---------------- Aksi ----------------

    void AutoDetect()
    {
        var info = _sal.AnalyzePerspectiveNow();
        if (info == null)
        {
            Status(Loc.T("Tidak ada garis perspektif yang terdeteksi di wallpaper ini."));
            return;
        }
        _pts = new List<Point> { new(info.VpX, info.VpY) };
        Status(Loc.T("Titik hilang dari deteksi otomatis dimuat — silakan disesuaikan."));
        Redraw(true);
    }

    void Save()
    {
        _s.PerspectivePoints = PerspectiveLayout.FormatPoints(_pts);
        _s.PerspectiveRays = _rays;
        _s.PerspectiveSteep = _steep;
        _s.PerspectiveSource = PerspectiveSource.Manual;
        _s.Style = LyricStyle.Perspective;
        Close();
    }
}
