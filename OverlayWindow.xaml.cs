using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace SyLO;

/// <summary>
/// Window overlay: transparan, selalu di atas, tidak bisa diklik (klik tembus ke aplikasi di belakang),
/// tidak muncul di taskbar / Alt+Tab, dan tidak mencuri fokus.
/// </summary>
public partial class OverlayWindow : Window
{
    readonly AppSettings _s;
    readonly DispatcherTimer _topmostTimer;
    IntPtr _hwnd;
    string _lastKey = "";
    int _lastLineId = int.MinValue;

    public event Action<int>? HotkeyPressed;

    public OverlayWindow(AppSettings settings)
    {
        InitializeComponent();
        _s = settings;
        SourceInitialized += OnSourceInitialized;
        SizeChanged += (_, _) => Reposition();
        DpiChanged += (_, _) => ApplySettings();

        // Beberapa game / taskbar bisa "merebut" posisi paling atas, jadi ditegaskan ulang berkala.
        _topmostTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _topmostTimer.Tick += (_, _) => EnsureTopmost();
        _topmostTimer.Start();
    }

    void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        int ex = Native.GetWindowLong(_hwnd, Native.GWL_EXSTYLE);
        Native.SetWindowLong(_hwnd, Native.GWL_EXSTYLE,
            ex | Native.WS_EX_TRANSPARENT | Native.WS_EX_LAYERED | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE);
        HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);
        ApplySettings();
    }

    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Native.WM_HOTKEY)
        {
            HotkeyPressed?.Invoke(wParam.ToInt32());
            handled = true;
        }
        return IntPtr.Zero;
    }

    public bool RegisterHotkey(int id, uint modifiers, uint vk) =>
        _hwnd != IntPtr.Zero && Native.RegisterHotKey(_hwnd, id, modifiers | Native.MOD_NOREPEAT, vk);

    public void UnregisterHotkey(int id)
    {
        if (_hwnd != IntPtr.Zero) Native.UnregisterHotKey(_hwnd, id);
    }

    void EnsureTopmost()
    {
        if (_hwnd != IntPtr.Zero)
            Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, 0, 0, 0, 0,
                Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
    }

    System.Windows.Forms.Screen TargetScreen()
    {
        var all = System.Windows.Forms.Screen.AllScreens;
        return _s.MonitorIndex >= 0 && _s.MonitorIndex < all.Length
            ? all[_s.MonitorIndex]
            : System.Windows.Forms.Screen.PrimaryScreen ?? all[0];
    }

    double DpiScale => _hwnd == IntPtr.Zero ? 1.0 : VisualTreeHelper.GetDpi(this).DpiScaleX;

    /// <summary>Terapkan ulang semua pengaturan visual (dipanggil setiap kali pengaturan berubah).</summary>
    public void ApplySettings()
    {
        var bg = _s.BackgroundOpacity > 0.001 ? ColorHelper.Brush(_s.BackgroundColor, _s.BackgroundOpacity) : null;
        Root.Opacity = CineWrap.Opacity = _s.LyricsOpacity;
        Root.Background = CineHost.Background = bg;

        var bounds = TargetScreen().Bounds;
        double maxW = Math.Max(150, bounds.Width * _s.WidthPercent / 100.0 / DpiScale);

        if (Cinematic)
        {
            Root.Visibility = Visibility.Collapsed;
            CineWrap.MaxWidth = maxW;
            SizeToContent = SizeToContent.WidthAndHeight;
        }
        else
        {
            CineHost.BeginAnimation(OpacityProperty, null);
            CineHost.Visibility = Visibility.Collapsed;
            CineWrap.Children.Clear();
            SizeToContent = SizeToContent.Height;
            Width = maxW;
        }

        _lastKey = ""; // paksa render ulang isi
        _lastLineId = int.MinValue;
        _cineId = int.MinValue;
        _cineText = "";
        _pending = null;
        _saliency.DepthEnabled = WantsOcclusion;
        _saliency.PerspectiveEnabled = PerspectiveStyle && _s.PerspectiveSource == PerspectiveSource.Auto;
        if (WantsWallpaper && _hwnd != IntPtr.Zero) _saliency.Refresh(bounds);
        EnsureWallpaperWatch();
        if (Cinematic) UpdateOcclusion(force: true);
        Reposition();
    }

    void Reposition()
    {
        if (_hwnd == IntPtr.Zero) return;
        var b = TargetScreen().Bounds; // piksel fisik
        double scale = DpiScale;
        int w = (int)Math.Round(ActualWidth * scale);
        int h = (int)Math.Round(ActualHeight * scale);
        int ox = (int)Math.Round(_s.OffsetX * scale);
        int oy = (int)Math.Round(_s.OffsetY * scale);

        int x, y;
        if (Cinematic)
        {
            // posisi acak (fraksi dari ruang kosong) supaya selalu di dalam layar, plus geseran mengambang
            if (_persActive)
            {
                x = b.Left + (int)Math.Round(_absX * scale) + (int)Math.Round(_bobX * scale);
                y = b.Top + (int)Math.Round(_absY * scale) + (int)Math.Round(_bobY * scale);
            }
            else
            {
                x = b.Left + (int)(_fx * Math.Max(0, b.Width - w)) + (int)Math.Round(_bobX * scale);
                y = b.Top + (int)(_fy * Math.Max(0, b.Height - h)) + (int)Math.Round(_bobY * scale);
            }
        }
        else
        {
            int col = (int)_s.Anchor % 3, row = (int)_s.Anchor / 3;
            x = col switch { 0 => b.Left, 1 => b.Left + (b.Width - w) / 2, _ => b.Right - w } + ox;
            y = row switch { 0 => b.Top, 1 => b.Top + (b.Height - h) / 2, _ => b.Bottom - h } + oy;
        }

        Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, x, y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
    }

    public string DebugBounds() =>
        $"overlay L={Left:0} T={Top:0} W={ActualWidth:0} H={ActualHeight:0} vis={Root.Visibility} dpi={DpiScale:0.##} screen={TargetScreen().Bounds}";

    public void ShowNothing()
    {
        if (_lastKey == "\0hidden") return;
        _lastKey = "\0hidden";
        _lastLineId = int.MinValue;
        Root.Visibility = Cinematic ? Visibility.Collapsed : Visibility.Hidden;

        if (Cinematic)
        {
            _cineId = int.MinValue;
            _cineText = "";
            _pending = null;
            if (_s.Animate && CineHost.Visibility == Visibility.Visible)
                FadeOutCine(() => { if (_cineId == int.MinValue) CineHost.Visibility = Visibility.Hidden; });
            else
                CineHost.Visibility = Visibility.Hidden;
        }
    }

    /// <param name="lineId">ID baris aktif; animasi hanya dijalankan saat ID ini berganti.</param>
    /// <param name="lineDuration">Lama baris ini dinyanyikan (dipakai gaya sinematik untuk kecepatan munculnya huruf).</param>
    public void ShowLines(string? header, IReadOnlyList<string> before, string current, IReadOnlyList<string> after,
        int lineId, TimeSpan lineDuration = default)
    {
        if (Cinematic)
        {
            _lastKey = "\0cine";
            ShowCinematic(current, lineId, lineDuration);
            return;
        }

        string key = $"{header}\u001e{string.Join("\u001f", before)}\u001e{current}\u001e{string.Join("\u001f", after)}\u001e{lineId}";
        if (key == _lastKey) return;
        _lastKey = key;

        bool animate = _s.Animate && lineId != _lastLineId && _lastLineId != int.MinValue;
        _lastLineId = lineId;

        Root.Visibility = Visibility.Visible;
        LinesPanel.Children.Clear();

        var align = _s.Align switch
        {
            LyricAlign.Left => TextAlignment.Left,
            LyricAlign.Right => TextAlignment.Right,
            _ => TextAlignment.Center
        };

        if (!string.IsNullOrEmpty(header))
            LinesPanel.Children.Add(MakeLine(header, 0.5, _s.OtherLinesColor, 0.85, align));

        foreach (var l in before)
            LinesPanel.Children.Add(MakeLine(l, _s.OtherLinesScale, _s.OtherLinesColor, _s.OtherLinesOpacity, align));

        var cur = MakeLine(current, 1.0, _s.TextColor, 1.0, align);
        LinesPanel.Children.Add(cur);

        foreach (var l in after)
            LinesPanel.Children.Add(MakeLine(l, _s.OtherLinesScale, _s.OtherLinesColor, _s.OtherLinesOpacity, align));

        if (animate)
        {
            var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
            var dur = TimeSpan.FromMilliseconds(220);
            var tt = new TranslateTransform(0, 10);
            cur.RenderTransform = tt;
            tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(10, 0, dur) { EasingFunction = ease });
            cur.BeginAnimation(OpacityProperty, new DoubleAnimation(0.2, 1, dur) { EasingFunction = ease });
        }
    }

    OutlinedText MakeLine(string text, double scale, string color, double opacity, TextAlignment align) => new()
    {
        Text = string.IsNullOrWhiteSpace(text) ? " " : text,
        TextFontFamily = new FontFamily(_s.FontFamily),
        TextFontSize = Math.Max(6, _s.FontSize * scale),
        TextFontWeight = _s.Bold ? FontWeights.Bold : FontWeights.Normal,
        Fill = ColorHelper.Brush(color),
        Stroke = ColorHelper.Brush(_s.OutlineColor),
        StrokeThickness = _s.OutlineThickness * Math.Max(0.5, scale),
        Alignment = align,
        Opacity = opacity,
        Margin = new Thickness(0, 1, 0, 1)
    };

    // ---------------- Gaya sinematik ----------------

    /// <summary>Gaya satu-baris (Sinematik & Garis perspektif) memakai jalur render yang sama.</summary>
    bool Cinematic => _s.Style is LyricStyle.Cinematic or LyricStyle.Perspective;
    bool PerspectiveStyle => _s.Style == LyricStyle.Perspective;

    // posisi absolut (DIP, relatif ke pojok kiri-atas layar) saat lirik mengikuti garis perspektif
    bool _persActive;
    double _absX, _absY;
    double _lastRayAngle = double.NaN;
    readonly Random _rng = new();
    double _fx = 0.5, _fy = 0.75;
    int _cineId = int.MinValue;
    string _cineText = "";
    (string Text, TimeSpan Duration)? _pending;
    bool _fadingOut;

    void ShowCinematic(string text, int id, TimeSpan duration)
    {
        if (id == _cineId && text == _cineText) return;
        _cineId = id;
        _cineText = text;

        bool showing = CineHost.Visibility == Visibility.Visible && CineWrap.Children.Count > 0;
        if (!showing || !_s.Animate)
        {
            BuildCine(text, duration);
            return;
        }

        // baris lama memudar dulu, baru baris baru muncul di tempat lain
        _pending = (text, duration);
        if (_fadingOut) return;
        _fadingOut = true;
        FadeOutCine(() =>
        {
            _fadingOut = false;
            if (_pending is { } p)
            {
                _pending = null;
                BuildCine(p.Text, p.Duration);
            }
        });
    }

    System.Windows.Media.Color? _albumColor;
    readonly WallpaperSaliency _saliency = new();

    RevealText? _cineReveal;
    Color _cineTextColor = Colors.White;
    DispatcherTimer? _wallpaperTimer;

    /// <summary>Warna cover album lagu sekarang; dipakai mulai baris berikutnya.</summary>
    public void SetAlbumColor(System.Windows.Media.Color? c) => _albumColor = c;

    double _bobX, _bobY;
    DispatcherTimer? _bobTimer;
    readonly System.Diagnostics.Stopwatch _bobClock = System.Diagnostics.Stopwatch.StartNew();

    void EnsureBobTimer()
    {
        if (_bobTimer != null) return;
        _bobTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
        _bobTimer.Tick += (_, _) => BobTick();
        _bobTimer.Start();
    }

    /// <summary>Gerak mengambang pelan (sinus = ease in/out alami), ~30 fps.</summary>
    void BobTick()
    {
        bool active = Cinematic && _s.Animate && CineHost.Visibility == Visibility.Visible;
        double t = _bobClock.Elapsed.TotalSeconds;
        double nx = active ? 4 * Math.Sin(t * 2 * Math.PI / 7.4) : 0;
        double ny = active ? 6 * Math.Sin(t * 2 * Math.PI / 4.8) : 0;
        // tiap ±0,5 detik: cek apakah lirik sekarang di atas desktop atau tertutup jendela lain
        if (++_occTick % 15 == 0) UpdateOcclusion();
        if (Math.Abs(nx - _bobX) < 0.25 && Math.Abs(ny - _bobY) < 0.25) return;
        _bobX = nx;
        _bobY = ny;
        Reposition();
    }

    bool WantsWallpaper => Cinematic && (_s.AvoidWallpaperFocus || _s.GlowSource == GlowSource.Blend || WantsOcclusion || PerspectiveStyle);
    bool WantsOcclusion => Cinematic && _s.Occlusion && DepthEstimator.ModelExists;

    // ---------------- Lirik lewat di belakang objek wallpaper ----------------

    string _occKey = "";
    int _occTick;

    /// <summary>
    /// Tutup bagian lirik yang berada di belakang objek dekat (tiang, karakter, ...) memakai peta kedalaman AI.
    /// Hanya aktif saat yang terlihat di belakang lirik memang desktop/wallpaper (bukan Word, game, dll).
    /// </summary>
    void UpdateOcclusion(bool force = false)
    {
        var depth = WantsOcclusion ? _saliency.DepthMap : null;
        if (depth == null || _cineReveal == null || _hwnd == IntPtr.Zero || CineHost.Visibility != Visibility.Visible)
        {
            if (CineHost.OpacityMask != null) CineHost.OpacityMask = null;
            _occKey = "";
            return;
        }

        Native.GetWindowRect(_hwnd, out var wr);
        var r = new System.Drawing.Rectangle(wr.L, wr.T, wr.R - wr.L, wr.B - wr.T);
        bool desktop = WallpaperSaliency.IsDesktopVisible(r);
        // posisi dibulatkan supaya gerak mengambang kecil tidak memicu hitung ulang terus-menerus
        string key = $"{depth.GetHashCode()}|{_cineId}|{desktop}|{_s.OcclusionStrength:0.00}|{r.X / 24},{r.Y / 24},{r.Width},{r.Height}";
        if (!force && key == _occKey) return;
        _occKey = key;

        if (!desktop || r.Width < 4 || r.Height < 4)
        {
            CineHost.OpacityMask = null;
            return;
        }

        var b = TargetScreen().Bounds;
        int mw = Math.Max(8, r.Width / 3), mh = Math.Max(8, r.Height / 3);
        var px = new byte[mw * mh * 4];
        double th = 0.75 - _s.OcclusionStrength * 0.6; // kekuatan 0 → hanya objek sangat menonjol, 1 → hampir semua objek
        const double soft = 0.06;
        for (int y = 0; y < mh; y++)
        for (int x = 0; x < mw; x++)
        {
            double fx = (r.X + (x + 0.5) * r.Width / mw - b.X) / b.Width;
            double fy = (r.Y + (y + 0.5) * r.Height / mh - b.Y) / b.Height;
            double fg = depth.At(fx, fy);
            double t = Math.Clamp((fg - (th - soft)) / (2 * soft), 0, 1);
            byte a = (byte)(255 * (1 - t * t * (3 - 2 * t))); // smoothstep: tepi halus
            int i = (y * mw + x) * 4;
            px[i] = px[i + 1] = px[i + 2] = px[i + 3] = a;
        }
        var bmp = System.Windows.Media.Imaging.BitmapSource.Create(mw, mh, 96, 96, PixelFormats.Pbgra32, null, px, mw * 4);
        bmp.Freeze();
        var brush = new ImageBrush(bmp) { Stretch = Stretch.Fill };
        brush.Freeze();
        CineHost.OpacityMask = brush;
    }

    /// <summary>Warna cahaya (dalam, luar) sesuai pengaturan dan posisi lirik sekarang.</summary>
    (Color Inner, Color Outer) GlowColors()
    {
        var custom = ColorHelper.Parse(_s.GlowColor);
        switch (_s.GlowSource)
        {
            case GlowSource.TextColor:
                return (_cineTextColor, _cineTextColor);
            case GlowSource.Album:
                var a = _albumColor ?? custom;
                return (a, a);
            case GlowSource.Blend:
                var b = TargetScreen().Bounds;
                double scale = DpiScale;
                double wf = Math.Min(1, ActualWidth * scale / b.Width), hf = Math.Min(1, ActualHeight * scale / b.Height);
                double left = _persActive ? _absX * scale / b.Width : _fx * (1 - wf);
                double top = _persActive ? _absY * scale / b.Height : _fy * (1 - hf);
                var wall = _saliency.DominantAround(left, top, wf, hf);
                return (_cineTextColor, wall ?? _albumColor ?? custom);
            default:
                return (custom, custom);
        }
    }

    void UpdateCineGlow()
    {
        if (_cineReveal == null || !Cinematic) return;
        var (inner, outer) = GlowColors();
        _cineReveal.SetGlow(inner, outer);
    }

    /// <summary>Cek wallpaper tiap ±3 detik; kalau berubah, cahaya lirik yang tampil langsung menyesuaikan.</summary>
    void EnsureWallpaperWatch()
    {
        if (_wallpaperTimer == null)
        {
            _saliency.Changed += () => Dispatcher.BeginInvoke(() =>
            {
                UpdateCineGlow();
                UpdateOcclusion(force: true);
            });
            _wallpaperTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _wallpaperTimer.Tick += (_, _) =>
            {
                if (WantsWallpaper && CineHost.Visibility == Visibility.Visible) _saliency.Refresh(TargetScreen().Bounds);
            };
        }
        if (WantsWallpaper) _wallpaperTimer.Start(); else _wallpaperTimer.Stop();
    }

    /// <summary>Daftar posisi acak, lalu pilih yang paling sedikit menutupi bagian menonjol wallpaper.</summary>
    void PickCinePosition()
    {
        var b = TargetScreen().Bounds;
        double scale = DpiScale;
        double wf = Math.Min(1, ActualWidth * scale / b.Width), hf = Math.Min(1, ActualHeight * scale / b.Height);
        var map = _s.AvoidWallpaperFocus ? _saliency.Map : null;
        if (WantsWallpaper) _saliency.Refresh(b);

        double bestX = _fx, bestY = _fy, bestScore = double.MaxValue;
        for (int i = 0; i < 40; i++)
        {
            double nx = 0.04 + _rng.NextDouble() * 0.92;
            double ny = 0.06 + _rng.NextDouble() * 0.86;
            bool tooClose = Math.Abs(nx - _fx) < 0.3 && Math.Abs(ny - _fy) < 0.3;
            double score = tooClose ? 10 : 0;
            if (map != null)
                score += WallpaperSaliency.Cost(map, nx * (1 - wf), ny * (1 - hf), wf, hf);
            score += _rng.NextDouble() * 0.08; // sedikit acak supaya tidak selalu di tempat yang sama
            if (score < bestScore) { bestScore = score; bestX = nx; bestY = ny; }
            if (map == null && !tooClose) break; // tanpa peta wallpaper: cukup posisi acak pertama yang valid
        }
        _fx = bestX;
        _fy = bestY;
    }

    /// <summary>Tata letak lirik di garis perspektif wallpaper (null jika belum ada garis yang cocok).</summary>
    Func<IReadOnlyList<Rect>, double, (Matrix[] Mats, Size Size)>? MakePerspectiveLayout(Action<Point> setOrigin)
    {
        bool manual = _s.PerspectiveSource == PerspectiveSource.Manual;
        IReadOnlyList<PerspectiveInfo>? infos = manual
            ? PerspectiveLayout.FromManual(PerspectiveLayout.ParsePoints(_s.PerspectivePoints), _s.PerspectiveRays)
            : _saliency.Perspective is { } auto ? new[] { auto } : null;
        if (infos == null || infos.Count == 0) return null;
        var b = TargetScreen().Bounds;
        double scale = DpiScale;
        double minAbsX = manual && _s.PerspectiveSteep ? 0.12 : 0.35;
        return PerspectiveLayout.Create(infos, b.Width / scale, b.Height / scale, _rng, ref _lastRayAngle, setOrigin, minAbsX);
    }

    /// <summary>Pembaca wallpaper (dipakai juga oleh editor perspektif).</summary>
    public WallpaperSaliency Saliency => _saliency;

    /// <summary>Batas layar target overlay (piksel fisik).</summary>
    public System.Drawing.Rectangle ScreenBounds => TargetScreen().Bounds;

    static Color Lighten(Color c, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        byte L(byte v) => (byte)(v + (255 - v) * amount);
        return Color.FromRgb(L(c.R), L(c.G), L(c.B));
    }

    void FadeOutCine(Action done)
    {
        var a = new DoubleAnimation(CineHost.Opacity, 0, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
        };
        a.Completed += (_, _) => done();
        CineHost.BeginAnimation(OpacityProperty, a);
    }

    void BuildCine(string text, TimeSpan duration)
    {
        CineHost.BeginAnimation(OpacityProperty, null);
        CineHost.Opacity = 1;
        Root.Visibility = Visibility.Collapsed;
        CineHost.Visibility = Visibility.Visible;
        CineWrap.Children.Clear();

        bool animate = _s.Animate;
        double size = Math.Max(8, _s.FontSize * 1.15);
        var font = new FontFamily(_s.FontFamily);
        var weight = _s.Bold ? FontWeights.Bold : FontWeights.Normal;
        var textColor = _albumColor is { } ac && _s.AlbumColorText ? ac : ColorHelper.Parse(_s.TextColor);
        _cineTextColor = textColor;
        var (glowInner, glowColor) = GlowColors(); // posisi belum dipilih; dihitung ulang setelah PickCinePosition
        bool glowing = _s.GlowStrength > 0.01 && _s.GlowSize > 0.5;
        // Isi huruf ikut "menyala": sedikit diterangkan ke arah putih sesuai kekuatan cahaya
        var fill = new SolidColorBrush(glowing ? Lighten(textColor, _s.GlowStrength * 0.35) : textColor);
        fill.Freeze();
        var stroke = ColorHelper.Brush(_s.OutlineColor);

        var line = string.IsNullOrWhiteSpace(text) ? "♪" : text;
        int total = Math.Max(1, new System.Globalization.StringInfo(line.Replace(" ", "")).LengthInTextElements);

        // Huruf selesai muncul kira-kira di 45% durasi baris (cepat untuk baris pendek, pelan untuk baris panjang)
        double budget = duration > TimeSpan.Zero ? duration.TotalMilliseconds * 0.45 : 900;
        double perChar = Math.Clamp(budget / total, 18, 80) / _s.RevealSpeed;

        // Gaya garis perspektif: pilih satu garis dari wallpaper; kalau tidak ada, jatuh ke tata letak sinematik biasa
        Point persOrigin = default;
        var persLayout = PerspectiveStyle ? MakePerspectiveLayout(o => persOrigin = o) : null;
        _persActive = persLayout != null;
        CineHost.Padding = new Thickness(_persActive ? 4 : 28);

        var reveal = new RevealText
        {
            Text = line,
            TextFontFamily = font,
            TextFontSize = size,
            TextFontWeight = weight,
            Fill = fill,
            Stroke = stroke,
            StrokeThickness = _s.OutlineThickness,
            MaxLineWidth = _persActive ? 100000 : CineWrap.MaxWidth,
            Layout = persLayout,
            Rise = animate ? size * 0.4 : 0,
            Spread = Math.Clamp(480 / _s.RevealSpeed / perChar, 1.5, 14), // tiap huruf memudar ~480 ms (pada kecepatan 1x)
            Progress = animate ? 0 : double.MaxValue,
            GlowColor = glowColor,
            GlowInnerColor = glowInner,
            GlowStrength = _s.GlowStrength,
            GlowSize = _s.GlowSize
        };
        CineWrap.Children.Add(reveal);

        // Seluruh baris sedikit miring & mengambang pelan (ease in/out)
        var rot = new RotateTransform(animate && !_persActive ? _rng.NextDouble() * 4 - 2 : 0);
        var bob = new TranslateTransform();
        var group = new TransformGroup();
        group.Children.Add(rot);
        group.Children.Add(bob);
        CineWrap.RenderTransform = group;

        // Efek mengambang: jendelanya yang digeser (lihat BobTick), bukan isinya, supaya tidak perlu digambar ulang tiap frame
        if (animate) EnsureBobTimer();

        _cineReveal = reveal;
        UpdateLayout();
        if (_persActive)
        {
            _absX = persOrigin.X - CineHost.Padding.Left;
            _absY = persOrigin.Y - CineHost.Padding.Top;
        }
        else PickCinePosition();
        Reposition();
        UpdateCineGlow(); // warna wallpaper di sekitar posisi baru
        UpdateOcclusion(force: true);
        if (animate) reveal.StartReveal(perChar);
    }

    protected override void OnClosed(EventArgs e)
    {
        _topmostTimer.Stop();
        base.OnClosed(e);
    }
}
