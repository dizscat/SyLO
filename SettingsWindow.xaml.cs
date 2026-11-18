using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using WinForms = System.Windows.Forms;

namespace SyLO;

public partial class SettingsWindow : Window
{
    readonly AppSettings _s;
    readonly PresetStore _presets;

    /// <summary>Elemen yang teksnya diisi dari kode (jangan diterjemahkan ulang oleh Loc.Apply).</summary>
    static readonly HashSet<string> Dynamic = new() { "PresetStatus", "ModelStatus" };

    readonly Action _openPerspectiveEditor;

    public SettingsWindow(AppSettings settings, PresetStore presets, Action openPerspectiveEditor)
    {
        InitializeComponent();
        _s = settings;
        _presets = presets;
        _openPerspectiveEditor = openPerspectiveEditor;
        Loc.Apply(this, Dynamic);

        PresetList.ItemsSource = _presets.Items;
        _presets.Items.CollectionChanged += (_, _) => UpdatePresetButtons();
        UpdatePresetButtons();

        FontBox.ItemsSource = Fonts.SystemFontFamilies
            .Select(f => f.Source)
            .Distinct()
            .OrderBy(n => n)
            .ToList();

        LanguageBox.ItemsSource = new List<KeyValuePair<UiLanguage, string>>
        {
            new(UiLanguage.Indonesian, "Bahasa Indonesia"),
            new(UiLanguage.English, "English"),
            new(UiLanguage.Chinese, "中文（简体）"),
        };

        StyleBox.ItemsSource = Options(
            (LyricStyle.Classic, "Klasik — beberapa baris di posisi tetap"),
            (LyricStyle.Cinematic, "Sinematik — per huruf, mengambang, posisi acak"),
            (LyricStyle.Perspective, "Garis perspektif — mengikuti garis wallpaper (experimental)"));

        PerspectiveSourceBox.ItemsSource = Options(
            (PerspectiveSource.Auto, "Otomatis dari wallpaper"),
            (PerspectiveSource.Manual, "Atur sendiri (editor)"));

        GlowSourceBox.ItemsSource = Options(
            (GlowSource.Blend, "Dalam = warna teks → luar = warna wallpaper"),
            (GlowSource.TextColor, "Warna teks (huruf ikut menyala / neon)"),
            (GlowSource.Custom, "Warna pilihan sendiri"),
            (GlowSource.Album, "Warna cover album"));

        AlignBox.ItemsSource = Options(
            (LyricAlign.Left, "Rata kiri"),
            (LyricAlign.Center, "Tengah"),
            (LyricAlign.Right, "Rata kanan"));

        AnchorBox.ItemsSource = Options(
            (OverlayAnchor.TopLeft, "Kiri atas"),
            (OverlayAnchor.TopCenter, "Tengah atas"),
            (OverlayAnchor.TopRight, "Kanan atas"),
            (OverlayAnchor.MiddleLeft, "Kiri tengah"),
            (OverlayAnchor.MiddleCenter, "Tengah layar"),
            (OverlayAnchor.MiddleRight, "Kanan tengah"),
            (OverlayAnchor.BottomLeft, "Kiri bawah"),
            (OverlayAnchor.BottomCenter, "Tengah bawah"),
            (OverlayAnchor.BottomRight, "Kanan bawah"));

        var monitors = new List<KeyValuePair<int, string>> { new(-1, Loc.T("Monitor utama")) };
        var screens = WinForms.Screen.AllScreens;
        for (int i = 0; i < screens.Length; i++)
        {
            var b = screens[i].Bounds;
            monitors.Add(new(i, Loc.F("Monitor {0} — {1}×{2}", i + 1, b.Width, b.Height) + (screens[i].Primary ? Loc.T(" (utama)") : "")));
        }
        MonitorBox.ItemsSource = monitors;

        DataContext = _s;
        UpdateModelStatus();
    }

    static List<KeyValuePair<T, string>> Options<T>(params (T Key, string Label)[] items) =>
        items.Select(i => new KeyValuePair<T, string>(i.Key, Loc.T(i.Label))).ToList();

    // ---------------- Model AI kedalaman ----------------

    System.Threading.CancellationTokenSource? _downloadCts;

    void UpdateModelStatus()
    {
        bool ok = DepthEstimator.ModelExists;
        ModelStatus.Text = ok ? Loc.T("✓ Model AI siap dipakai.") : Loc.T("Model AI belum diunduh (diperlukan untuk efek ini).");
        ModelStatus.Foreground = ok ? new SolidColorBrush(Color.FromRgb(0x2B, 0x7A, 0x3D)) : Brushes.DarkOrange;
        ModelBtn.Visibility = ok ? Visibility.Collapsed : Visibility.Visible;
    }

    async void DownloadModel_Click(object sender, RoutedEventArgs e)
    {
        if (_downloadCts != null) { _downloadCts.Cancel(); return; } // klik lagi = batal

        _downloadCts = new System.Threading.CancellationTokenSource();
        ModelBtn.Content = Loc.T("Batal");
        var progress = new Progress<double>(p => ModelStatus.Text = Loc.F("Mengunduh model… {0:0%}", p));
        try
        {
            await DepthEstimator.DownloadModelAsync(progress, _downloadCts.Token);
            _s.Occlusion = true;
            // terapkan ulang supaya overlay mulai memakai model
            var cur = _s.OcclusionStrength;
            _s.OcclusionStrength = cur > 0.5 ? cur - 0.01 : cur + 0.01;
            _s.OcclusionStrength = cur;
        }
        catch (OperationCanceledException)
        {
            ModelStatus.Text = Loc.T("Unduhan dibatalkan.");
        }
        catch (Exception ex)
        {
            ModelStatus.Text = Loc.F("Gagal mengunduh: {0}", ex.Message);
        }
        finally
        {
            _downloadCts = null;
            ModelBtn.Content = Loc.T("Unduh model AI (±26 MB)");
            if (DepthEstimator.ModelExists) UpdateModelStatus();
        }
    }

    void PickColor(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string prop }) return;
        var pi = typeof(AppSettings).GetProperty(prop);
        if (pi == null) return;

        var c = ColorHelper.Parse(pi.GetValue(_s) as string);
        using var dlg = new WinForms.ColorDialog
        {
            FullOpen = true,
            AnyColor = true,
            Color = System.Drawing.Color.FromArgb(c.R, c.G, c.B)
        };
        if (dlg.ShowDialog() == WinForms.DialogResult.OK)
            pi.SetValue(_s, $"#{dlg.Color.R:X2}{dlg.Color.G:X2}{dlg.Color.B:X2}");
    }

    void ResetOffset_Click(object sender, RoutedEventArgs e)
    {
        _s.OffsetX = 0;
        _s.OffsetY = 0;
    }

    void Reset_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, Loc.T("Kembalikan semua pengaturan tampilan & posisi ke default?"), "SyLO",
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            _s.ResetToDefaults();
    }

    void Close_Click(object sender, RoutedEventArgs e) => Close();

    void OpenPerspectiveEditor_Click(object sender, RoutedEventArgs e) => _openPerspectiveEditor();

    // ---------------- Template ----------------

    Preset? Selected => PresetList.SelectedItem as Preset;

    void UpdatePresetButtons()
    {
        bool has = Selected != null;
        LoadBtn.IsEnabled = OverwriteBtn.IsEnabled = RenameBtn.IsEnabled = CopyCodeBtn.IsEnabled = DeleteBtn.IsEnabled = has;
        UndoBtn.IsEnabled = _presets.UndoSnapshot != null;
        EmptyPresetsHint.Visibility = _presets.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    void Status(string text, bool error = false)
    {
        PresetStatus.Text = text;
        PresetStatus.Foreground = error ? Brushes.Firebrick : new SolidColorBrush(Color.FromRgb(0x2B, 0x7A, 0x3D));
    }

    void PresetList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (Selected != null) PresetNameBox.Text = Selected.Name;
        UpdatePresetButtons();
    }

    void PresetList_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (Selected != null) LoadPreset_Click(sender, e);
    }

    void SavePreset_Click(object sender, RoutedEventArgs e)
    {
        var name = PresetStore.CleanName(PresetNameBox.Text);
        if (name.Length == 0) name = Loc.F("Template {0}", _presets.Items.Count + 1);

        var existing = _presets.Find(name);
        if (existing != null)
        {
            var r = MessageBox.Show(this,
                Loc.F("Template \"{0}\" sudah ada.\n\nYes = timpa template itu\nNo = simpan sebagai template baru dengan nama lain", existing.Name),
                "SyLO", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (r == MessageBoxResult.Cancel) return;
            if (r == MessageBoxResult.Yes)
            {
                Overwrite(existing);
                return;
            }
            name = _presets.UniqueName(name);
        }

        var p = new Preset { Name = name, Created = DateTime.Now, Data = PresetStore.Capture(_s) };
        _presets.Items.Add(p);
        _presets.Save();
        PresetList.SelectedItem = p;
        PresetList.ScrollIntoView(p);
        Status(Loc.F("Tersimpan: \"{0}\"", name));
    }

    void OverwritePreset_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } p) return;
        if (MessageBox.Show(this, Loc.F("Timpa template \"{0}\" dengan tampilan & posisi sekarang?", p.Name), "SyLO",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        Overwrite(p);
    }

    void Overwrite(Preset p)
    {
        p.Data = PresetStore.Capture(_s);
        p.Created = DateTime.Now;
        p.NotifyDataChanged();
        _presets.Save();
        PresetList.SelectedItem = p;
        Status(Loc.F("Template \"{0}\" diperbarui", p.Name));
    }

    void LoadPreset_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } p) return;
        _presets.LoadInto(_s, p.Data);
        UpdatePresetButtons();
        Status(Loc.F("Dimuat: \"{0}\"", p.Name));
    }

    void RenamePreset_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } p) return;
        var name = PresetStore.CleanName(PresetNameBox.Text);
        if (name.Length == 0)
        {
            Status(Loc.T("Tulis nama baru di kotak nama dulu, lalu klik Ganti nama."), true);
            PresetNameBox.Focus();
            return;
        }
        if (string.Equals(name, p.Name, StringComparison.Ordinal)) { Status(Loc.T("Namanya masih sama.")); return; }
        if (_presets.Find(name) is { } other && other != p)
        {
            Status(Loc.F("Nama \"{0}\" sudah dipakai template lain.", name), true);
            return;
        }
        p.Name = name;
        _presets.Save();
        Status(Loc.F("Nama diganti jadi \"{0}\"", name));
    }

    void DeletePreset_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } p) return;
        if (MessageBox.Show(this, Loc.F("Hapus template \"{0}\"?\nSalin kodenya dulu kalau mau disimpan.", p.Name), "SyLO",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        _presets.Items.Remove(p);
        _presets.Save();
        Status(Loc.F("Dihapus: \"{0}\"", p.Name));
    }

    void CopyCode_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } p) return;
        var code = PresetStore.ToCode(p.Name, p.Data);
        for (int i = 0; i < 5; i++)
        {
            try
            {
                Clipboard.SetText(code);
                Status(Loc.F("Kode \"{0}\" disalin ({1} karakter). Tinggal paste ke teman.", p.Name, code.Length));
                return;
            }
            catch { System.Threading.Thread.Sleep(60); } // clipboard sedang dipakai aplikasi lain
        }
        // gagal: tampilkan kodenya supaya bisa disalin manual
        ImportBox.Text = code;
        ImportBox.SelectAll();
        ImportBox.Focus();
        Status(Loc.T("Clipboard sedang sibuk. Kodenya ada di kotak impor, salin manual dengan Ctrl+C."), true);
    }

    void ImportCode_Click(object sender, RoutedEventArgs e)
    {
        if (!PresetStore.TryParseCode(ImportBox.Text, out var name, out var data, out var error))
        {
            Status(error, true);
            return;
        }

        // kode yang sama persis sudah pernah diimpor? cukup muat yang lama
        var same = _presets.Items.FirstOrDefault(x =>
            PresetStore.ToCode(x.Name, x.Data) == PresetStore.ToCode(name, data));
        var p = same ?? new Preset { Name = _presets.UniqueName(name), Created = DateTime.Now, Data = data };
        if (same == null)
        {
            // simpan template lengkap: isi dari kode + monitor yang sedang dipakai di komputer ini
            p.Data[nameof(AppSettings.MonitorIndex)] = _s.MonitorIndex;
            _presets.Items.Add(p);
            _presets.Save();
        }

        _presets.LoadInto(_s, p.Data);
        PresetList.SelectedItem = p;
        PresetList.ScrollIntoView(p);
        ImportBox.Clear();
        UpdatePresetButtons();
        Status(same == null ? Loc.F("Diimpor & dipakai: \"{0}\"", p.Name) : Loc.F("Template ini sudah ada, dimuat: \"{0}\"", p.Name));
    }

    void Undo_Click(object sender, RoutedEventArgs e)
    {
        if (_presets.Undo(_s)) Status(Loc.T("Dikembalikan ke tampilan sebelum template dimuat."));
        UpdatePresetButtons();
    }
}
