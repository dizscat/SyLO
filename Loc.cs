using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace SyLO;

public enum UiLanguage { Indonesian, English, Chinese }

/// <summary>
/// Terjemahan sederhana: teks bahasa Indonesia dipakai sebagai kunci, lalu dicari padanannya
/// dalam bahasa Inggris / Mandarin (Sederhana). Teks yang tidak ada di kamus tampil apa adanya.
/// </summary>
public static class Loc
{
    public static UiLanguage Lang { get; set; } = UiLanguage.Indonesian;

    public static UiLanguage SystemDefault()
    {
        var c = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        return c switch { "id" or "ms" => UiLanguage.Indonesian, "zh" => UiLanguage.Chinese, _ => UiLanguage.English };
    }

    public static string T(string id)
    {
        if (Lang == UiLanguage.Indonesian || !M.TryGetValue(id, out var t)) return id;
        return Lang == UiLanguage.English ? t.En : t.Zh;
    }

    public static string F(string id, params object[] args) => string.Format(T(id), args);

    /// <summary>Terjemahkan semua teks statis di jendela (judul, label, tombol, kotak centang, header, tooltip).</summary>
    public static void Apply(DependencyObject root, ISet<string>? skipNames = null)
    {
        if (root is FrameworkElement fe && skipNames != null && fe.Name.Length > 0 && skipNames.Contains(fe.Name)) return;

        switch (root)
        {
            case Window w:
                w.Title = T(w.Title);
                break;
            case TextBlock tb when !BindingOperations.IsDataBound(tb, TextBlock.TextProperty) && !string.IsNullOrEmpty(tb.Text):
                tb.Text = T(tb.Text);
                break;
            case HeaderedContentControl h when h.Header is string hs:
                h.Header = T(hs);
                break;
            case ContentControl cc when cc.Content is string cs:
                cc.Content = T(cs);
                break;
        }
        if (root is FrameworkElement f && f.ToolTip is string tip) f.ToolTip = T(tip);

        foreach (var child in LogicalTreeHelper.GetChildren(root))
            if (child is DependencyObject d) Apply(d, skipNames);
    }

    static readonly Dictionary<string, (string En, string Zh)> M = new()
    {
        // ---------- Jendela pengaturan ----------
        ["SyLO — Pengaturan Overlay Lirik"] = ("SyLO — Lyrics Overlay Settings", "SyLO — 歌词悬浮窗设置"),
        ["Reset ke default"] = ("Reset to defaults", "恢复默认"),
        ["Tutup"] = ("Close", "关闭"),
        ["Pengaturan tersimpan otomatis."] = ("Settings are saved automatically.", "设置会自动保存。"),
        ["Overlay tidak bisa diklik — semua diatur dari sini. Selama jendela ini terbuka, overlay menampilkan pratinjau supaya kamu bisa lihat hasil perubahan langsung."] =
            ("The overlay can't be clicked — everything is set here. While this window is open, the overlay shows a preview so you can see changes right away.",
             "悬浮窗无法点击——所有设置都在这里调整。此窗口打开时，悬浮窗会显示预览，方便你即时查看效果。"),
        ["Template gaya & posisi"] = ("Style & position templates", "样式与位置模板"),
        ["Simpan tampilan & posisi sekarang sebagai template, lalu muat lagi kapan saja. Setiap template punya kode yang bisa dibagikan ke pengguna SyLO lain."] =
            ("Save the current look & position as a template and load it again any time. Every template has a code you can share with other SyLO users.",
             "将当前的外观和位置保存为模板，随时重新加载。每个模板都有一个可分享给其他 SyLO 用户的代码。"),
        ["Belum ada template. Atur tampilan sesukamu, beri nama di bawah, lalu klik Simpan."] =
            ("No templates yet. Set things up the way you like, enter a name below, then click Save.", "还没有模板。按喜好调整外观，在下方输入名称，然后点击保存。"),
        ["Muat"] = ("Load", "加载"),
        ["Timpa dengan pengaturan sekarang"] = ("Overwrite with current settings", "用当前设置覆盖"),
        ["Ganti nama"] = ("Rename", "重命名"),
        ["Salin kode"] = ("Copy code", "复制代码"),
        ["Hapus"] = ("Delete", "删除"),
        ["Simpan sebagai template baru"] = ("Save as new template", "另存为新模板"),
        ["Nama template (dipakai juga untuk Ganti nama)"] = ("Template name (also used for Rename)", "模板名称（重命名时也使用）"),
        ["Punya kode template dari orang lain?"] = ("Got a template code from someone?", "有别人分享的模板代码？"),
        ["Impor & pakai"] = ("Import & apply", "导入并应用"),
        ["Tempel kode SYLO1-… di sini"] = ("Paste a SYLO1-… code here", "在此粘贴 SYLO1-… 代码"),
        ["↶ Urungkan muat terakhir"] = ("↶ Undo last load", "↶ 撤销上次加载"),
        ["Teks"] = ("Text", "文字"),
        ["Font"] = ("Font", "字体"),
        ["Ukuran font"] = ("Font size", "字号"),
        ["Gaya"] = ("Style", "样式"),
        ["Huruf tebal (bold)"] = ("Bold", "粗体"),
        ["Warna lirik aktif"] = ("Active lyric color", "当前歌词颜色"),
        ["Warna baris lain"] = ("Other lines color", "其他行颜色"),
        ["Warna outline"] = ("Outline color", "描边颜色"),
        ["Ketebalan outline"] = ("Outline thickness", "描边粗细"),
        ["Tampilan"] = ("Appearance", "外观"),
        ["Gaya tampilan"] = ("Display style", "显示样式"),
        ["Animasi aktif (matikan kalau ingin teks langsung muncul tanpa gerakan)"] =
            ("Animations on (turn off to show text instantly without motion)", "启用动画（关闭后文字直接显示，不带动效）"),
        ["Sinematik & Garis perspektif: satu baris muncul huruf demi huruf sambil mengambang. Pengaturan posisi & jumlah baris tidak dipakai; lebar area = lebar maksimal satu baris."] =
            ("Cinematic & Perspective lines: one line appears letter by letter while floating. Position and line-count settings are not used; area width = max width of a line.",
             "电影感与透视线：每次显示一行，逐字出现并轻轻漂浮。不使用位置和行数设置；区域宽度＝单行最大宽度。"),
        ["Kejelasan lirik (opacity)"] = ("Lyrics opacity", "歌词不透明度"),
        ["Baris sebelumnya"] = ("Previous lines", "前面行数"),
        ["Baris berikutnya"] = ("Next lines", "后面行数"),
        ["Kejelasan baris lain"] = ("Other lines opacity", "其他行不透明度"),
        ["Ukuran baris lain"] = ("Other lines size", "其他行大小"),
        ["Rata teks"] = ("Text alignment", "文字对齐"),
        ["Warna latar"] = ("Background color", "背景颜色"),
        ["Kepekatan latar"] = ("Background opacity", "背景不透明度"),
        ["0% = tanpa latar (hanya teks ber-outline)."] = ("0% = no background (outlined text only).", "0% = 无背景（仅描边文字）。"),
        ["Tampilkan judul & artis di atas lirik"] = ("Show title & artist above lyrics", "在歌词上方显示歌名和歌手"),
        ["Sembunyikan overlay saat lagu di-pause"] = ("Hide overlay when paused", "暂停时隐藏悬浮窗"),
        ["Sinematik"] = ("Cinematic", "电影感"),
        ["Berlaku untuk gaya Sinematik dan Garis perspektif."] = ("Applies to the Cinematic and Perspective lines styles.", "适用于“电影感”和“透视线”样式。"),
        ["Kecepatan munculnya huruf"] = ("Letter reveal speed", "逐字出现速度"),
        ["Tampilkan lebih awal"] = ("Show earlier", "提前显示"),
        ["Kalau kata pertama terasa telat, naikkan 'Tampilkan lebih awal' dan/atau kecepatan."] =
            ("If the first word feels late, raise 'Show earlier' and/or the speed.", "如果第一个字感觉慢了，请调高“提前显示”和/或速度。"),
        ["Kekuatan cahaya (glow)"] = ("Glow strength", "发光强度"),
        ["Ukuran cahaya"] = ("Glow size", "发光大小"),
        ["Warna cahaya dari"] = ("Glow color from", "发光颜色来源"),
        ["Warna cahaya (pilihan sendiri)"] = ("Glow color (custom)", "发光颜色（自定义）"),
        ["Warna teks ikut warna cover album"] = ("Text color follows album cover", "文字颜色跟随专辑封面"),
        ["Lirik lewat di belakang objek wallpaper (tiang, karakter, dll.) (experimental)"] =
            ("Lyrics pass behind wallpaper objects (poles, characters, etc.) (experimental)", "歌词从壁纸物体（柱子、人物等）后方穿过（实验性）"),
        ["Banyaknya objek di depan lirik"] = ("Objects in front of lyrics", "歌词前方物体数量"),
        ["Unduh model AI (±26 MB)"] = ("Download AI model (±26 MB)", "下载 AI 模型（约 26 MB）"),
        ["Memakai model AI Depth Anything V2 Small (Apache-2.0) untuk menebak objek mana yang ada di depan. Hanya berlaku saat lirik berada di atas desktop; kalau di atas Word / game, lirik tampil utuh. Wallpaper dibaca dari bagian desktop yang sedang terlihat. Tips: matikan 'Hindari bagian menonjol' supaya lirik lebih sering lewat di belakang karakter."] =
            ("Uses the Depth Anything V2 Small AI model (Apache-2.0) to guess which objects are in front. Only applies when the lyrics are over the desktop; over Word or a game the lyrics stay whole. The wallpaper is read from the visible parts of the desktop. Tip: turn off 'Avoid prominent parts' so lyrics pass behind characters more often.",
             "使用 Depth Anything V2 Small AI 模型（Apache-2.0）判断哪些物体在前方。仅在歌词位于桌面上方时生效；在 Word 或游戏上方时歌词完整显示。壁纸从桌面可见部分读取。提示：关闭“避开壁纸显眼部分”，歌词会更常从人物后方穿过。"),
        ["Hindari bagian menonjol wallpaper (Wallpaper Engine / wallpaper biasa)"] =
            ("Avoid prominent parts of the wallpaper (Wallpaper Engine / regular wallpaper)", "避开壁纸的显眼部分（Wallpaper Engine / 普通壁纸）"),
        ["Wallpaper dicek tiap ±3 detik; kalau berubah, warna cahaya ikut berubah saat itu juga. Lirik dipilihkan tempat yang paling sedikit menutupi karakter / objek utama (boleh lewat sedikit)."] =
            ("The wallpaper is checked every ~3 seconds; if it changes, the glow color updates right away. Lyrics are placed where they cover the main character / object the least (a little overlap is fine).",
             "每约 3 秒检查一次壁纸；壁纸变化时发光颜色立即更新。歌词会放在最少遮挡主要人物/物体的位置（稍有重叠无妨）。"),
        ["Garis perspektif (experimental): lirik ditulis di sepanjang garis yang menuju titik hilang wallpaper (pagar, rel, lorong), makin jauh makin kecil. Kalau garisnya tidak ditemukan, lirik tampil seperti Sinematik."] =
            ("Perspective lines (experimental): lyrics are written along lines that run to the wallpaper's vanishing point (railings, tracks, corridors), getting smaller with distance. If no lines are found, lyrics show as Cinematic.",
             "透视线（实验性）：歌词沿着通往壁纸消失点的线条（栏杆、轨道、走廊）书写，越远越小。若找不到线条，则按“电影感”样式显示。"),
        ["Posisi"] = ("Position", "位置"),
        ["Monitor"] = ("Monitor", "显示器"),
        ["Posisi di layar"] = ("Screen position", "屏幕位置"),
        ["Geser kiri / kanan"] = ("Shift left / right", "左右偏移"),
        ["Geser atas / bawah"] = ("Shift up / down", "上下偏移"),
        ["Negatif = ke kiri / ke atas, positif = ke kanan / ke bawah."] = ("Negative = left / up, positive = right / down.", "负值 = 向左/向上，正值 = 向右/向下。"),
        ["Lebar area lirik"] = ("Lyrics area width", "歌词区域宽度"),
        ["Kembalikan geseran ke 0"] = ("Reset shift to 0", "偏移归零"),
        ["Sinkronisasi"] = ("Sync", "同步"),
        ["Geser waktu lirik"] = ("Lyrics time offset", "歌词时间偏移"),
        ["Positif = lirik muncul lebih cepat, negatif = lebih lambat. Saat main game bisa pakai Ctrl+Alt+[ dan Ctrl+Alt+] (±250 ms)."] =
            ("Positive = lyrics appear earlier, negative = later. While gaming you can use Ctrl+Alt+[ and Ctrl+Alt+] (±250 ms).",
             "正值 = 歌词提前出现，负值 = 延后。游戏中可使用 Ctrl+Alt+[ 和 Ctrl+Alt+]（±250 毫秒）。"),
        ["Lainnya"] = ("Other", "其他"),
        ["Tampilkan overlay"] = ("Show overlay", "显示悬浮窗"),
        ["Jalankan SyLO otomatis saat Windows menyala"] = ("Start SyLO automatically with Windows", "开机时自动启动 SyLO"),
        ["Hotkey:\nCtrl+Alt+L — tampilkan / sembunyikan overlay\nCtrl+Alt+O — buka jendela pengaturan ini\nCtrl+Alt+[ / Ctrl+Alt+] — lirik lebih lambat / lebih cepat"] =
            ("Hotkeys:\nCtrl+Alt+L — show / hide overlay\nCtrl+Alt+O — open this settings window\nCtrl+Alt+[ / Ctrl+Alt+] — lyrics later / earlier",
             "快捷键：\nCtrl+Alt+L — 显示/隐藏悬浮窗\nCtrl+Alt+O — 打开此设置窗口\nCtrl+Alt+[ / Ctrl+Alt+] — 歌词延后/提前"),
        ["Untuk game: pakai mode Borderless / Windowed. Mode Exclusive Fullscreen akan menutupi overlay."] =
            ("For games: use Borderless / Windowed mode. Exclusive Fullscreen will cover the overlay.", "游戏请使用无边框/窗口模式。独占全屏会遮住悬浮窗。"),
        ["Sumber lirik: lrclib.net (komunitas). Tidak semua lagu tersedia."] =
            ("Lyrics source: lrclib.net (community). Not every song is available.", "歌词来源：lrclib.net（社区）。并非所有歌曲都有歌词。"),

        // ---------- Editor perspektif ----------
        ["Titik hilang perspektif"] = ("Perspective vanishing points", "透视消失点"),
        ["Buka editor perspektif…"] = ("Open perspective editor…", "打开透视编辑器…"),
        ["Otomatis dari wallpaper"] = ("Automatic from wallpaper", "根据壁纸自动检测"),
        ["Atur sendiri (editor)"] = ("Set manually (editor)", "手动设置（编辑器）"),
        ["Di editor kamu bisa memilih perspektif 1, 2, 3, atau 4 titik, menyeret titik hilang di atas gambar wallpaper, menambah titik (sampai 6), dan melihat contoh lirik langsung."] =
            ("In the editor you can pick 1-, 2-, 3- or 4-point perspective, drag vanishing points over the wallpaper, add points (up to 6) and see sample lyrics live.",
             "在编辑器中可选择一点、两点、三点或四点透视，在壁纸上拖动消失点，添加点（最多 6 个），并实时查看示例歌词。"),
        ["Editor perspektif — SyLO"] = ("Perspective editor — SyLO", "透视编辑器 — SyLO"),
        ["{0} titik"] = ("{0}-point", "{0} 点"),
        ["Preset perspektif {0} titik."] = ("{0}-point perspective preset.", "{0} 点透视预设。"),
        ["Deteksi otomatis"] = ("Auto-detect", "自动检测"),
        ["Garis per titik"] = ("Lines per point", "每点线数"),
        ["Izinkan garis tegak"] = ("Allow steep lines", "允许陡峭线条"),
        ["Contoh lain"] = ("Another sample", "换个示例"),
        ["Simpan & pakai"] = ("Save & apply", "保存并应用"),
        ["Seret titik untuk memindahkan · Klik dua kali di tempat kosong untuk menambah titik · Klik kanan titik untuk menghapus · Enter = simpan · Esc = batal"] =
            ("Drag a point to move it · Double-click empty space to add a point · Right-click a point to remove it · Enter = save · Esc = cancel",
             "拖动点以移动 · 在空白处双击添加点 · 右键点击点以删除 · Enter = 保存 · Esc = 取消"),
        ["Wallpaper belum terbaca. Tutup editor, tampilkan desktop (Win+D) beberapa detik, lalu buka lagi."] =
            ("The wallpaper hasn't been read yet. Close the editor, show the desktop (Win+D) for a few seconds, then open it again.",
             "尚未读取到壁纸。请关闭编辑器，显示桌面（Win+D）几秒钟后再打开。"),
        ["Titik hilang"] = ("Vanishing point", "消失点"),
        ["Contoh lirik mengikuti garis perspektif"] = ("Sample lyrics following the perspective line", "沿透视线显示的示例歌词"),
        ["Baris berikutnya di garis lain"] = ("The next line on another line", "下一行在另一条线上"),
        ["Tidak ada garis yang cocok untuk lirik di dalam layar. Geser titik atau centang 'Izinkan garis tegak'."] =
            ("No line inside the screen fits the lyrics. Move a point or tick 'Allow steep lines'.", "屏幕内没有适合显示歌词的线条。请移动点或勾选“允许陡峭线条”。"),
        ["Maksimal {0} titik."] = ("At most {0} points.", "最多 {0} 个点。"),
        ["Titik {0} ditambahkan."] = ("Point {0} added.", "已添加第 {0} 个点。"),
        ["Minimal harus ada 1 titik."] = ("There must be at least 1 point.", "至少需要 1 个点。"),
        ["Titik {0} dihapus."] = ("Point {0} removed.", "已删除第 {0} 个点。"),
        ["Tidak ada garis perspektif yang terdeteksi di wallpaper ini."] = ("No perspective lines were detected in this wallpaper.", "未在此壁纸中检测到透视线。"),
        ["Titik hilang dari deteksi otomatis dimuat — silakan disesuaikan."] =
            ("Loaded the auto-detected vanishing point — adjust it as you like.", "已载入自动检测的消失点——可自行调整。"),

        // ---------- Pilihan di combo box ----------
        ["Klasik — beberapa baris di posisi tetap"] = ("Classic — several lines at a fixed position", "经典 — 多行固定位置"),
        ["Sinematik — per huruf, mengambang, posisi acak"] = ("Cinematic — letter by letter, floating, random position", "电影感 — 逐字出现、漂浮、随机位置"),
        ["Garis perspektif — mengikuti garis wallpaper (experimental)"] = ("Perspective lines — follows wallpaper lines (experimental)", "透视线 — 跟随壁纸线条（实验性）"),
        ["Dalam = warna teks → luar = warna wallpaper"] = ("Inner = text color → outer = wallpaper color", "内 = 文字颜色 → 外 = 壁纸颜色"),
        ["Warna teks (huruf ikut menyala / neon)"] = ("Text color (neon letters)", "文字颜色（霓虹效果）"),
        ["Warna pilihan sendiri"] = ("Custom color", "自定义颜色"),
        ["Warna cover album"] = ("Album cover color", "专辑封面颜色"),
        ["Rata kiri"] = ("Left", "左对齐"),
        ["Tengah"] = ("Center", "居中"),
        ["Rata kanan"] = ("Right", "右对齐"),
        ["Kiri atas"] = ("Top left", "左上"),
        ["Tengah atas"] = ("Top center", "中上"),
        ["Kanan atas"] = ("Top right", "右上"),
        ["Kiri tengah"] = ("Middle left", "左中"),
        ["Tengah layar"] = ("Center of screen", "正中"),
        ["Kanan tengah"] = ("Middle right", "右中"),
        ["Kiri bawah"] = ("Bottom left", "左下"),
        ["Tengah bawah"] = ("Bottom center", "中下"),
        ["Kanan bawah"] = ("Bottom right", "右下"),
        ["Monitor utama"] = ("Primary monitor", "主显示器"),
        ["Monitor {0} — {1}×{2}"] = ("Monitor {0} — {1}×{2}", "显示器 {0} — {1}×{2}"),
        [" (utama)"] = (" (primary)", "（主）"),

        // ---------- Model AI ----------
        ["✓ Model AI siap dipakai."] = ("✓ AI model ready.", "✓ AI 模型已就绪。"),
        ["Model AI belum diunduh (diperlukan untuk efek ini)."] = ("AI model not downloaded yet (needed for this effect).", "尚未下载 AI 模型（此效果需要）。"),
        ["Batal"] = ("Cancel", "取消"),
        ["Mengunduh model… {0:0%}"] = ("Downloading model… {0:0%}", "正在下载模型… {0:0%}"),
        ["Unduhan dibatalkan."] = ("Download canceled.", "下载已取消。"),
        ["Gagal mengunduh: {0}"] = ("Download failed: {0}", "下载失败：{0}"),

        // ---------- Template ----------
        ["Kembalikan semua pengaturan tampilan & posisi ke default?"] = ("Reset all appearance & position settings to defaults?", "将所有外观和位置设置恢复为默认值？"),
        ["Template {0}"] = ("Template {0}", "模板 {0}"),
        ["Template \"{0}\" sudah ada.\n\nYes = timpa template itu\nNo = simpan sebagai template baru dengan nama lain"] =
            ("Template \"{0}\" already exists.\n\nYes = overwrite it\nNo = save as a new template with another name", "模板“{0}”已存在。\n\n是 = 覆盖该模板\n否 = 用另一个名称另存为新模板"),
        ["Tersimpan: \"{0}\""] = ("Saved: \"{0}\"", "已保存：“{0}”"),
        ["Timpa template \"{0}\" dengan tampilan & posisi sekarang?"] = ("Overwrite template \"{0}\" with the current look & position?", "用当前外观和位置覆盖模板“{0}”？"),
        ["Template \"{0}\" diperbarui"] = ("Template \"{0}\" updated", "模板“{0}”已更新"),
        ["Dimuat: \"{0}\""] = ("Loaded: \"{0}\"", "已加载：“{0}”"),
        ["Tulis nama baru di kotak nama dulu, lalu klik Ganti nama."] = ("Type a new name in the name box first, then click Rename.", "请先在名称框中输入新名称，然后点击重命名。"),
        ["Namanya masih sama."] = ("The name is unchanged.", "名称没有变化。"),
        ["Nama \"{0}\" sudah dipakai template lain."] = ("The name \"{0}\" is already used by another template.", "名称“{0}”已被其他模板使用。"),
        ["Nama diganti jadi \"{0}\""] = ("Renamed to \"{0}\"", "已重命名为“{0}”"),
        ["Hapus template \"{0}\"?\nSalin kodenya dulu kalau mau disimpan."] =
            ("Delete template \"{0}\"?\nCopy its code first if you want to keep it.", "删除模板“{0}”？\n如需保留，请先复制其代码。"),
        ["Dihapus: \"{0}\""] = ("Deleted: \"{0}\"", "已删除：“{0}”"),
        ["Kode \"{0}\" disalin ({1} karakter). Tinggal paste ke teman."] =
            ("Code for \"{0}\" copied ({1} characters). Just paste it to a friend.", "已复制“{0}”的代码（{1} 个字符）。直接粘贴给朋友即可。"),
        ["Clipboard sedang sibuk. Kodenya ada di kotak impor, salin manual dengan Ctrl+C."] =
            ("The clipboard is busy. The code is in the import box — copy it manually with Ctrl+C.", "剪贴板正忙。代码已放在导入框中，请用 Ctrl+C 手动复制。"),
        ["Diimpor & dipakai: \"{0}\""] = ("Imported & applied: \"{0}\"", "已导入并应用：“{0}”"),
        ["Template ini sudah ada, dimuat: \"{0}\""] = ("This template already exists, loaded: \"{0}\"", "该模板已存在，已加载：“{0}”"),
        ["Dikembalikan ke tampilan sebelum template dimuat."] = ("Restored the look from before the template was loaded.", "已恢复到加载模板之前的外观。"),
        ["Kode masih kosong."] = ("The code is empty.", "代码为空。"),
        ["Ini bukan kode template SyLO (harus diawali \"{0}\")."] = ("This isn't a SyLO template code (it must start with \"{0}\").", "这不是 SyLO 模板代码（必须以“{0}”开头）。"),
        ["Kode terlalu panjang."] = ("The code is too long.", "代码过长。"),
        ["Isi kode terlalu besar."] = ("The code content is too large.", "代码内容过大。"),
        ["Kode rusak atau tidak lengkap."] = ("The code is damaged or incomplete.", "代码已损坏或不完整。"),
        ["Kode rusak atau salah salin. Pastikan seluruh kode tersalin utuh."] =
            ("The code is damaged or was copied incorrectly. Make sure the whole code was copied.", "代码已损坏或复制有误。请确保完整复制了整段代码。"),
        ["Template impor"] = ("Imported template", "导入的模板"),

        // ---------- Aplikasi, tray, overlay ----------
        ["SyLO sudah berjalan. Cek ikon ♪ di system tray (pojok kanan bawah)."] =
            ("SyLO is already running. Check the ♪ icon in the system tray (bottom-right).", "SyLO 已在运行。请查看系统托盘（右下角）中的 ♪ 图标。"),
        ["Ini contoh baris sebelumnya"] = ("This is a sample previous line", "这是上一行示例"),
        ["Baris yang sudah lewat"] = ("A line that already passed", "已经唱过的一行"),
        ["Ini baris lirik yang sedang dinyanyikan"] = ("This is the line being sung", "这是正在演唱的歌词"),
        ["Baris berikutnya muncul di sini"] = ("The next line appears here", "下一行会出现在这里"),
        ["Lalu baris setelahnya"] = ("And the one after that", "然后是再下一行"),
        ["SyLO — menunggu Spotify"] = ("SyLO — waiting for Spotify", "SyLO — 等待 Spotify"),
        ["SyLO berjalan"] = ("SyLO is running", "SyLO 正在运行"),
        ["Putar lagu di Spotify, liriknya akan muncul di layar. Klik ikon ♪ di tray untuk pengaturan."] =
            ("Play a song in Spotify and its lyrics will appear on screen. Click the ♪ tray icon for settings.", "在 Spotify 播放歌曲，歌词就会显示在屏幕上。点击托盘中的 ♪ 图标打开设置。"),
        ["Sebagian hotkey tidak aktif"] = ("Some hotkeys are not active", "部分快捷键未启用"),
        ["Sudah dipakai aplikasi lain: {0}"] = ("Already used by another app: {0}", "已被其他应用占用：{0}"),
        ["Sinkron lirik: {0:+0;-0;0} ms"] = ("Lyrics sync: {0:+0;-0;0} ms", "歌词同步：{0:+0;-0;0} 毫秒"),
        ["Template: {0}"] = ("Template: {0}", "模板：{0}"),
        ["SyLO — tidak ada lagu"] = ("SyLO — no song", "SyLO — 没有歌曲"),
        ["Artis — Judul Lagu (pratinjau)"] = ("Artist — Song title (preview)", "歌手 — 歌名（预览）"),
        ["♪ Mencari lirik…"] = ("♪ Searching for lyrics…", "♪ 正在搜索歌词…"),
        ["♪ Instrumental ♪"] = ("♪ Instrumental ♪", "♪ 纯音乐 ♪"),
        ["Lirik untuk lagu ini belum tersinkron"] = ("Lyrics for this song aren't synced yet", "这首歌的歌词尚未同步"),
        ["Gagal mengambil lirik (cek koneksi internet)"] = ("Couldn't fetch lyrics (check your internet connection)", "获取歌词失败（请检查网络连接）"),
        ["Lirik tidak ditemukan"] = ("Lyrics not found", "未找到歌词"),
        ["Sembunyikan overlay"] = ("Hide overlay", "隐藏悬浮窗"),
        ["Muat template"] = ("Load template", "加载模板"),
        ["(belum ada template)"] = ("(no templates yet)", "（还没有模板）"),
        ["Pengaturan…"] = ("Settings…", "设置…"),
        ["Keluar"] = ("Exit", "退出"),
    };
}
