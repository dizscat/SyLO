using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SyLO;

public enum OverlayAnchor
{
    TopLeft, TopCenter, TopRight,
    MiddleLeft, MiddleCenter, MiddleRight,
    BottomLeft, BottomCenter, BottomRight
}

public enum LyricAlign { Left, Center, Right }

/// <summary>Classic = beberapa baris di posisi tetap. Cinematic = satu baris muncul per huruf di posisi acak.</summary>
public enum LyricStyle { Classic, Cinematic, Perspective }

/// <summary>Asal warna cahaya: warna isi huruf (efek neon), warna pilihan sendiri, atau warna cover album.</summary>
public enum GlowSource { TextColor, Custom, Album, Blend }

/// <summary>Asal titik hilang untuk gaya garis perspektif.</summary>
public enum PerspectiveSource { Auto, Manual }

/// <summary>Semua pengaturan overlay. Disimpan di %AppData%\SyLO\settings.json.</summary>
public sealed class AppSettings : INotifyPropertyChanged
{
    public static string Folder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SyLO");
    static string FilePath => Path.Combine(Folder, "settings.json");

    static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public event PropertyChangedEventHandler? PropertyChanged;

    bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }

    // ---------- Teks ----------
    string _fontFamily = "Segoe UI";
    public string FontFamily { get => _fontFamily; set => Set(ref _fontFamily, string.IsNullOrWhiteSpace(value) ? "Segoe UI" : value); }

    double _fontSize = 32;
    public double FontSize { get => _fontSize; set => Set(ref _fontSize, Math.Clamp(value, 8, 160)); }

    bool _bold = true;
    public bool Bold { get => _bold; set => Set(ref _bold, value); }

    string _textColor = "#FFFFFF";
    public string TextColor { get => _textColor; set => Set(ref _textColor, value); }

    string _otherLinesColor = "#DDDDDD";
    public string OtherLinesColor { get => _otherLinesColor; set => Set(ref _otherLinesColor, value); }

    string _outlineColor = "#000000";
    public string OutlineColor { get => _outlineColor; set => Set(ref _outlineColor, value); }

    double _outlineThickness = 2;
    public double OutlineThickness { get => _outlineThickness; set => Set(ref _outlineThickness, Math.Clamp(value, 0, 12)); }

    // ---------- Tampilan ----------
    double _lyricsOpacity = 1.0;
    public double LyricsOpacity { get => _lyricsOpacity; set => Set(ref _lyricsOpacity, Math.Clamp(value, 0.05, 1)); }

    int _linesBefore = 1;
    public int LinesBefore { get => _linesBefore; set => Set(ref _linesBefore, Math.Clamp(value, 0, 4)); }

    int _linesAfter = 1;
    public int LinesAfter { get => _linesAfter; set => Set(ref _linesAfter, Math.Clamp(value, 0, 4)); }

    double _otherLinesOpacity = 0.55;
    public double OtherLinesOpacity { get => _otherLinesOpacity; set => Set(ref _otherLinesOpacity, Math.Clamp(value, 0.05, 1)); }

    double _otherLinesScale = 0.75;
    public double OtherLinesScale { get => _otherLinesScale; set => Set(ref _otherLinesScale, Math.Clamp(value, 0.3, 1)); }

    LyricAlign _align = LyricAlign.Center;
    public LyricAlign Align { get => _align; set => Set(ref _align, value); }

    string _backgroundColor = "#000000";
    public string BackgroundColor { get => _backgroundColor; set => Set(ref _backgroundColor, value); }

    double _backgroundOpacity = 0.0;
    public double BackgroundOpacity { get => _backgroundOpacity; set => Set(ref _backgroundOpacity, Math.Clamp(value, 0, 1)); }

    bool _showTrackInfo = false;
    public bool ShowTrackInfo { get => _showTrackInfo; set => Set(ref _showTrackInfo, value); }

    bool _hideWhenPaused = true;
    public bool HideWhenPaused { get => _hideWhenPaused; set => Set(ref _hideWhenPaused, value); }

    LyricStyle _style = LyricStyle.Classic;
    public LyricStyle Style { get => _style; set => Set(ref _style, value); }

    // ---------- Sinematik ----------
    double _revealSpeed = 1.0; // 1 = normal, 2 = dua kali lebih cepat
    public double RevealSpeed { get => _revealSpeed; set => Set(ref _revealSpeed, Math.Clamp(value, 0.25, 4)); }

    int _cineLeadMs = 300; // baris ditampilkan sekian ms lebih awal (menutupi waktu animasi)
    public int CineLeadMs { get => _cineLeadMs; set => Set(ref _cineLeadMs, Math.Clamp(value, 0, 2000)); }

    double _glowStrength = 0.6; // 0 = tanpa cahaya
    public double GlowStrength { get => _glowStrength; set => Set(ref _glowStrength, Math.Clamp(value, 0, 1)); }

    double _glowSize = 14;
    public double GlowSize { get => _glowSize; set => Set(ref _glowSize, Math.Clamp(value, 0, 40)); }

    string _glowColor = "#7CC4FF";
    public string GlowColor { get => _glowColor; set => Set(ref _glowColor, value); }

    GlowSource _glowSource = GlowSource.Blend;
    public GlowSource GlowSource { get => _glowSource; set => Set(ref _glowSource, value); }

    bool _albumColorText = false;
    public bool AlbumColorText { get => _albumColorText; set => Set(ref _albumColorText, value); }

    // ---------- Garis perspektif ----------
    PerspectiveSource _perspectiveSource = PerspectiveSource.Auto;
    public PerspectiveSource PerspectiveSource { get => _perspectiveSource; set => Set(ref _perspectiveSource, value); }

    string _perspectivePoints = "0.5,0.42"; // titik hilang buatan sendiri: "x,y;x,y" (fraksi layar)
    public string PerspectivePoints { get => _perspectivePoints; set => Set(ref _perspectivePoints, value ?? ""); }

    int _perspectiveRays = 12; // jumlah garis per titik hilang (mode manual)
    public int PerspectiveRays { get => _perspectiveRays; set => Set(ref _perspectiveRays, Math.Clamp(value, 4, 36)); }

    bool _perspectiveSteep = false; // izinkan garis yang hampir tegak (berguna untuk perspektif 3/4 titik)
    public bool PerspectiveSteep { get => _perspectiveSteep; set => Set(ref _perspectiveSteep, value); }

    bool _occlusion = false; // lirik lewat di belakang objek wallpaper (butuh model AI depth)
    public bool Occlusion { get => _occlusion; set => Set(ref _occlusion, value); }

    double _occlusionStrength = 0.5; // makin tinggi, makin banyak objek yang dianggap ada di depan lirik
    public double OcclusionStrength { get => _occlusionStrength; set => Set(ref _occlusionStrength, Math.Clamp(value, 0, 1)); }

    bool _avoidWallpaperFocus = true;
    public bool AvoidWallpaperFocus { get => _avoidWallpaperFocus; set => Set(ref _avoidWallpaperFocus, value); }

    bool _animate = true;
    public bool Animate { get => _animate; set => Set(ref _animate, value); }

    // ---------- Posisi ----------
    int _monitorIndex = -1; // -1 = monitor utama
    public int MonitorIndex { get => _monitorIndex; set => Set(ref _monitorIndex, value); }

    OverlayAnchor _anchor = OverlayAnchor.BottomCenter;
    public OverlayAnchor Anchor { get => _anchor; set => Set(ref _anchor, value); }

    double _offsetX = 0;
    public double OffsetX { get => _offsetX; set => Set(ref _offsetX, value); }

    double _offsetY = -90;
    public double OffsetY { get => _offsetY; set => Set(ref _offsetY, value); }

    double _widthPercent = 60;
    public double WidthPercent { get => _widthPercent; set => Set(ref _widthPercent, Math.Clamp(value, 10, 100)); }

    // ---------- Sinkronisasi ----------
    int _syncOffsetMs = 0; // + = lirik muncul lebih cepat
    public int SyncOffsetMs { get => _syncOffsetMs; set => Set(ref _syncOffsetMs, Math.Clamp(value, -10000, 10000)); }

    // ---------- Lainnya ----------
    UiLanguage _language = Loc.SystemDefault();
    public UiLanguage Language { get => _language; set => Set(ref _language, value); }

    bool _overlayVisible = true;
    public bool OverlayVisible { get => _overlayVisible; set => Set(ref _overlayVisible, value); }

    bool _runAtStartup = false;
    public bool RunAtStartup { get => _runAtStartup; set => Set(ref _runAtStartup, value); }

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOpts) ?? new AppSettings();
        }
        catch { /* file rusak: pakai default */ }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, JsonOpts));
            File.Move(tmp, FilePath, true);
        }
        catch { /* abaikan, dicoba lagi saat perubahan berikutnya */ }
    }

    /// <summary>Kembalikan tampilan & posisi ke default (tidak mengubah startup / visibilitas).</summary>
    public void ResetToDefaults()
    {
        var d = new AppSettings();
        foreach (var p in typeof(AppSettings).GetProperties())
        {
            if (!p.CanWrite || p.Name is nameof(RunAtStartup) or nameof(OverlayVisible) or nameof(Language)) continue;
            p.SetValue(this, p.GetValue(d));
        }
    }
}
