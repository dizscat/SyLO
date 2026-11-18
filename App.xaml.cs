using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace SyLO;

public partial class App : Application
{
    const int HK_TOGGLE = 1, HK_SETTINGS = 2, HK_EARLIER = 3, HK_LATER = 4;
    const uint VK_L = 0x4C, VK_O = 0x4F, VK_OEM_4 = 0xDB /* [ */, VK_OEM_6 = 0xDD /* ] */;

    static readonly string[] SampleBefore = { "Ini contoh baris sebelumnya", "Baris yang sudah lewat" };
    static readonly string SampleCurrent = "Ini baris lirik yang sedang dinyanyikan";
    static readonly string[] SampleAfter = { "Baris berikutnya muncul di sini", "Lalu baris setelahnya" };

    AppSettings _s = null!;
    PresetStore _presets = null!;
    OverlayWindow _overlay = null!;
    TrayIcon _tray = null!;
    SettingsWindow? _settingsWindow;
    readonly MediaWatcher _media = new();
    readonly LyricsService _lyricsService = new();
    Mutex? _mutex;

    Lyrics? _lyrics;
    LyricsStatus _status = LyricsStatus.None;
    CancellationTokenSource? _fetchCts;
    DateTime _trackStartedUtc;
    string? _toast;
    DateTime _toastUntilUtc;

    DispatcherTimer _pollTimer = null!, _renderTimer = null!, _saveTimer = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _mutex = new Mutex(true, @"Local\SyLO.SingleInstance", out bool isFirst);
        if (!isFirst)
        {
            MessageBox.Show(Loc.T("SyLO sudah berjalan. Cek ikon ♪ di system tray (pojok kanan bawah)."), "SyLO");
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, ex) =>
        {
            Log(ex.Exception);
            ex.Handled = true;
        };

        _s = AppSettings.Load();
        Loc.Lang = _s.Language;
        _s.PropertyChanged += OnSettingChanged;

        _overlay = new OverlayWindow(_s);
        _overlay.HotkeyPressed += OnHotkey;
        _overlay.Show();

        _presets = PresetStore.Load();
        _tray = new TrayIcon(ToggleOverlay, OpenSettings, ExitApp,
            () => _presets.Items.Select(p => p.Name).ToList(), LoadPresetFromTray);
        _tray.SetOverlayVisible(_s.OverlayVisible);
        _tray.SetTooltip(Loc.T("SyLO — menunggu Spotify"));

        RegisterHotkeys();
        StartupRegistration.Set(_s.RunAtStartup);

        _media.TrackChanged += OnTrackChanged;
        _media.AlbumColorChanged += c =>
        {
            _overlay.SetAlbumColor(c);
            Diag($"album color: {(c?.ToString() ?? "-")}");
        };

        Diag("start");
        if (Array.IndexOf(e.Args, "--settings") >= 0) OpenSettings();
        if (Array.IndexOf(e.Args, "--perspective-editor") >= 0) OpenPerspectiveEditor();

        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _pollTimer.Tick += async (_, _) => await _media.PollAsync();
        _pollTimer.Start();

        _renderTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(60) };
        _renderTimer.Tick += (_, _) => Render();
        _renderTimer.Start();

        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); _s.Save(); };

        if (!File.Exists(Path.Combine(AppSettings.Folder, "settings.json")))
        {
            _s.Save();
            _tray.ShowBalloon(Loc.T("SyLO berjalan"),
                Loc.T("Putar lagu di Spotify, liriknya akan muncul di layar. Klik ikon ♪ di tray untuk pengaturan."));
        }
    }

    // ---------------- Hotkeys ----------------

    void RegisterHotkeys()
    {
        var failed = new List<string>();
        const uint mods = Native.MOD_CONTROL | Native.MOD_ALT;
        if (!_overlay.RegisterHotkey(HK_TOGGLE, mods, VK_L)) failed.Add("Ctrl+Alt+L");
        if (!_overlay.RegisterHotkey(HK_SETTINGS, mods, VK_O)) failed.Add("Ctrl+Alt+O");
        if (!_overlay.RegisterHotkey(HK_EARLIER, mods, VK_OEM_4)) failed.Add("Ctrl+Alt+[");
        if (!_overlay.RegisterHotkey(HK_LATER, mods, VK_OEM_6)) failed.Add("Ctrl+Alt+]");
        if (failed.Count > 0)
            _tray.ShowBalloon(Loc.T("Sebagian hotkey tidak aktif"),
                Loc.F("Sudah dipakai aplikasi lain: {0}", string.Join(", ", failed)));
    }

    void OnHotkey(int id)
    {
        switch (id)
        {
            case HK_TOGGLE: ToggleOverlay(); break;
            case HK_SETTINGS: OpenSettings(); break;
            case HK_EARLIER:
                _s.SyncOffsetMs -= 250;
                Toast(Loc.F("Sinkron lirik: {0:+0;-0;0} ms", _s.SyncOffsetMs));
                break;
            case HK_LATER:
                _s.SyncOffsetMs += 250;
                Toast(Loc.F("Sinkron lirik: {0:+0;-0;0} ms", _s.SyncOffsetMs));
                break;
        }
    }

    void Toast(string text)
    {
        _toast = text;
        _toastUntilUtc = DateTime.UtcNow.AddSeconds(2);
    }

    // ---------------- Actions ----------------

    void ToggleOverlay() => _s.OverlayVisible = !_s.OverlayVisible;

    PerspectiveEditor? _perspectiveEditor;

    /// <summary>Buka editor titik hilang (layar penuh). Overlay disembunyikan selama editor terbuka.</summary>
    void OpenPerspectiveEditor()
    {
        if (_perspectiveEditor != null) { _perspectiveEditor.Activate(); return; }
        var sal = _overlay.Saliency;
        sal.Refresh(_overlay.ScreenBounds, force: true); // ambil wallpaper terbaru sebelum editor menutupi desktop
        _perspectiveEditor = new PerspectiveEditor(_s, sal);
        _perspectiveEditor.Closed += (_, _) =>
        {
            _perspectiveEditor = null;
            _settingsWindow?.Activate();
        };
        _perspectiveEditor.Show();
        _perspectiveEditor.Activate();
    }

    void OpenSettings()
    {
        if (_settingsWindow == null)
        {
            _settingsWindow = new SettingsWindow(_s, _presets, OpenPerspectiveEditor);
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            _settingsWindow.Show();
        }
        if (_settingsWindow.WindowState == WindowState.Minimized) _settingsWindow.WindowState = WindowState.Normal;
        _settingsWindow.Activate();
    }

    void LoadPresetFromTray(int index)
    {
        if (index < 0 || index >= _presets.Items.Count) return;
        var p = _presets.Items[index];
        _presets.LoadInto(_s, p.Data);
        Toast(Loc.F("Template: {0}", p.Name));
    }

    /// <summary>Buka ulang jendela pengaturan supaya semua teks memakai bahasa yang baru dipilih.</summary>
    void ReopenSettings()
    {
        if (_settingsWindow == null) return;
        var old = _settingsWindow;
        Dispatcher.BeginInvoke(() =>
        {
            double left = old.Left, top = old.Top;
            old.Close();
            OpenSettings();
            if (_settingsWindow != null) { _settingsWindow.Left = left; _settingsWindow.Top = top; }
        });
    }

    void ExitApp()
    {
        _settingsWindow?.Close();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _pollTimer?.Stop();
            _renderTimer?.Stop();
            if (_overlay != null)
                foreach (var id in new[] { HK_TOGGLE, HK_SETTINGS, HK_EARLIER, HK_LATER }) _overlay.UnregisterHotkey(id);
            _s?.Save();
            _tray?.Dispose();
            _lyricsService.Dispose();
            _mutex?.Dispose();
        }
        catch { }
        base.OnExit(e);
    }

    void OnSettingChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(AppSettings.RunAtStartup):
                StartupRegistration.Set(_s.RunAtStartup);
                break;
            case nameof(AppSettings.Language):
                Loc.Lang = _s.Language;
                _tray.Relabel(_s.OverlayVisible);
                ReopenSettings();
                break;
            case nameof(AppSettings.OverlayVisible):
                _tray.SetOverlayVisible(_s.OverlayVisible);
                break;
        }
        _overlay.ApplySettings();
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    // ---------------- Lyrics ----------------

    async void OnTrackChanged(TrackInfo? track)
    {
        _fetchCts?.Cancel();
        _lyrics = null;
        _overlay.SetAlbumColor(null); // warna lagu sebelumnya tidak dipakai lagi

        if (track == null)
        {
            _status = LyricsStatus.None;
            _tray.SetTooltip(Loc.T("SyLO — tidak ada lagu"));
            return;
        }

        _tray.SetTooltip($"SyLO — {track.Artist} – {track.Title}");
        _trackStartedUtc = DateTime.UtcNow;
        Diag($"track: '{track.Title}' / '{track.Artist}' / '{track.Album}' dur={track.Duration} playing={_media.IsPlaying}");

        // Iklan Spotify biasanya tanpa nama artis
        if (track.Artist.Length == 0)
        {
            _status = LyricsStatus.None;
            return;
        }

        _status = LyricsStatus.Loading;
        var cts = _fetchCts = new CancellationTokenSource();
        try
        {
            var result = await _lyricsService.GetAsync(track, cts.Token);
            if (cts.IsCancellationRequested) return;
            _lyrics = result;
            _status = result.Status;
            Diag($"lyrics: {result.Status}, {result.Lines.Count} baris; {_overlay.DebugBounds()}");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Log(ex);
            if (!cts.IsCancellationRequested) _status = LyricsStatus.Error;
        }
    }

    // ---------------- Render ----------------

    void Render()
    {
        if (!_s.OverlayVisible || _perspectiveEditor != null) { _overlay.ShowNothing(); return; }

        bool preview = _settingsWindow != null;
        string? toast = _toast != null && DateTime.UtcNow < _toastUntilUtc ? _toast : null;
        var track = _media.Current;
        bool active = track != null && (_media.IsPlaying || !_s.HideWhenPaused) && _status != LyricsStatus.None;

        if (!active)
        {
            if (preview && _s.Style != LyricStyle.Classic)
            {
                // pratinjau sinematik: ganti contoh baris tiap 3,5 detik supaya animasinya terlihat
                var samples = new[] { SampleBefore[0], SampleCurrent, SampleAfter[0], SampleAfter[1] }.Select(Loc.T).ToArray();
                int n = (int)(Environment.TickCount64 / 3500 % samples.Length);
                _overlay.ShowLines(null, Array.Empty<string>(), samples[n], Array.Empty<string>(), n, TimeSpan.FromSeconds(3));
            }
            else if (preview)
                _overlay.ShowLines(toast ?? (_s.ShowTrackInfo ? Loc.T("Artis — Judul Lagu (pratinjau)") : null),
                    Take(SampleBefore, _s.LinesBefore, fromEnd: true), Loc.T(SampleCurrent), Take(SampleAfter, _s.LinesAfter), 0);
            else if (toast != null)
                Message(null, toast);
            else
                _overlay.ShowNothing();
            return;
        }

        string? header = toast ?? (_s.ShowTrackInfo ? $"{track!.Artist} — {track.Title}" : null);
        bool recent = preview || (DateTime.UtcNow - _trackStartedUtc).TotalSeconds < 5;

        switch (_status)
        {
            case LyricsStatus.Synced when _lyrics != null:
                RenderSynced(header);
                break;
            case LyricsStatus.Instrumental:
                Message(header, Loc.T("♪ Instrumental ♪"));
                break;
            case LyricsStatus.Loading:
                if (recent || header != null) Message(header, Loc.T("♪ Mencari lirik…"));
                else _overlay.ShowNothing();
                break;
            default:
                var msg = _status switch
                {
                    LyricsStatus.Unsynced => Loc.T("Lirik untuk lagu ini belum tersinkron"),
                    LyricsStatus.Error => Loc.T("Gagal mengambil lirik (cek koneksi internet)"),
                    _ => Loc.T("Lirik tidak ditemukan")
                };
                if (recent || toast != null) Message(header, msg);
                else _overlay.ShowNothing();
                break;
        }
    }

    void RenderSynced(string? header)
    {
        var lines = _lyrics!.Lines;
        int lead = _s.Style != LyricStyle.Classic ? _s.CineLeadMs : 0; // tampil lebih awal untuk menutupi waktu animasi
        var pos = _media.Position + TimeSpan.FromMilliseconds(_s.SyncOffsetMs + lead);
        int idx = _lyrics.IndexAt(pos);

        string Line(int i) =>
            i < 0 || i >= lines.Count ? "" : (lines[i].Text.Length == 0 ? "♪" : lines[i].Text);

        var before = new List<string>();
        for (int k = _s.LinesBefore; k >= 1; k--) before.Add(Line(idx - k));
        var after = new List<string>();
        for (int k = 1; k <= _s.LinesAfter; k++) after.Add(Line(idx + k));

        // durasi baris aktif = jarak ke baris berikutnya (untuk kecepatan munculnya huruf di gaya sinematik)
        var dur = idx >= 0 && idx + 1 < lines.Count ? lines[idx + 1].Time - lines[idx].Time
                : idx < 0 && lines.Count > 0 ? lines[0].Time - pos
                : TimeSpan.FromSeconds(3);

        _overlay.ShowLines(header, before, idx < 0 ? "♪" : Line(idx), after, idx, dur);
    }

    void Message(string? header, string text) =>
        _overlay.ShowLines(header, Array.Empty<string>(), text, Array.Empty<string>(), -1000);

    static string[] Take(string[] src, int n, bool fromEnd = false)
    {
        n = Math.Clamp(n, 0, 4);
        var r = new string[n];
        for (int i = 0; i < n; i++)
        {
            int j = fromEnd ? src.Length - n + i : i;
            r[i] = j >= 0 && j < src.Length ? Loc.T(src[j]) : "";
        }
        return r;
    }

    /// <summary>Catatan singkat untuk diagnosa (%AppData%\SyLO\debug.log, dipotong jika &gt; 200 KB).</summary>
    static void Diag(string text)
    {
        try
        {
            Directory.CreateDirectory(AppSettings.Folder);
            var path = Path.Combine(AppSettings.Folder, "debug.log");
            if (File.Exists(path) && new FileInfo(path).Length > 200_000) File.Delete(path);
            File.AppendAllText(path, $"[{DateTime.Now:HH:mm:ss}] {text}\n");
        }
        catch { }
    }

    static void Log(Exception ex)
    {
        try
        {
            Directory.CreateDirectory(AppSettings.Folder);
            File.AppendAllText(Path.Combine(AppSettings.Folder, "error.log"), $"[{DateTime.Now:s}] {ex}\n\n");
        }
        catch { }
    }
}
