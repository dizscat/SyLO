# SyLO — Spotify Lyrics Overlay

Menampilkan lirik lagu yang sedang diputar di Spotify desktop sebagai overlay di layar.
Overlay **tidak bisa diklik** (klik tembus ke Word / game di belakangnya) dan tidak mencuri fokus.
Semua pengaturan ada di jendela terpisah. Bahasa antarmuka: **Indonesia, English, 中文**.

*Shows the lyrics of the song playing in Spotify desktop as a click-through on-screen overlay. UI in Indonesian, English and Chinese.*

## Unduh & pakai (tanpa perlu install .NET)

1. Unduh `SyLO-…-win-x64.zip` dari halaman **Releases**, ekstrak ke folder mana saja.
2. Jalankan `SyLO.exe`. Ikon **♪** muncul di system tray (pojok kanan bawah).
3. Putar lagu di **Spotify desktop**, lirik muncul otomatis.

Butuh Windows 10 (2004) / Windows 11 64-bit.

## Build dari source

Butuh [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
dotnet run -c Release
```

Membuat paket siap pakai (seperti di Releases):

```bash
dotnet publish -c Release -r win-x64 --self-contained true -o publish
```

## Cara pakai

- Ikon **♪ di system tray**: klik kiri = pengaturan, klik kanan = menu (tampil/sembunyi, muat template, keluar).
- Hotkey:
  - `Ctrl+Alt+L` tampilkan / sembunyikan overlay
  - `Ctrl+Alt+O` buka pengaturan
  - `Ctrl+Alt+[` / `Ctrl+Alt+]` lirik lebih lambat / lebih cepat (±250 ms)
- **Bahasa**: pilih di baris paling atas jendela pengaturan.
- **Gaya tampilan** (pengaturan → *Tampilan*):
  - *Klasik*: beberapa baris lirik di posisi tetap.
  - *Sinematik*: satu baris muncul huruf demi huruf (memudar + naik), mengambang pelan, dan setiap baris pindah ke posisi acak.
    Bisa diberi cahaya (glow) — warna dalam = warna teks, warna luar = warna wallpaper / cover album.
  - *Garis perspektif* **(experimental)**: lirik ditulis di sepanjang garis yang menuju titik hilang wallpaper
    (pagar, rel, lorong), makin jauh makin kecil. Kalau tidak ada garis yang ketemu, tampil seperti Sinematik.
    Titik hilangnya bisa **otomatis** (dideteksi dari wallpaper) atau **diatur sendiri** lewat *Buka editor perspektif…*:
    preset perspektif 1 / 2 / 3 / 4 titik, seret titik di atas gambar wallpaper, tambah titik (klik dua kali, maks. 6),
    hapus (klik kanan), atur jumlah garis per titik, dan lihat contoh lirik langsung.
  - *Lirik lewat di belakang objek wallpaper* **(experimental)**: bagian lirik yang berada di belakang tiang / karakter
    disembunyikan, memakai model AI kedalaman (diunduh dari pengaturan, ±26 MB).
- **Template**: simpan & muat gaya + posisi, dan bagikan lewat kode `SYLO1-…`.
- Game harus di mode **Borderless / Windowed** (Exclusive Fullscreen menutupi semua overlay).

### Catatan fitur wallpaper

Wallpaper dibaca dari **bagian desktop yang sedang terlihat** (untuk Wallpaper Engine berbasis DirectX tidak ada cara
lain), jadi efek yang bergantung pada wallpaper baru bekerja setelah desktop sempat terlihat beberapa detik (mis. `Win+D`).

## Cara kerja

| Bagian | File |
|---|---|
| Baca lagu & posisi dari Windows Media Session (tanpa login Spotify) | `MediaWatcher.cs` |
| Ambil lirik tersinkron (LRC) dari [lrclib.net](https://lrclib.net) | `LyricsService.cs` |
| Window overlay click-through, topmost, tidak di Alt+Tab | `OverlayWindow.xaml(.cs)` |
| Teks per huruf + glow (pre-render ke bitmap supaya ringan) | `RevealText.cs` |
| Baca wallpaper, peta bagian menonjol, warna dominan | `WallpaperSaliency.cs` |
| Titik hilang & garis perspektif (Sobel + Hough) | `PerspectiveAnalyzer.cs`, `PerspectiveLayout.cs` |
| Editor titik hilang (1–4+ titik) | `PerspectiveEditor.cs` |
| Model AI kedalaman (ONNX Runtime) | `DepthEstimator.cs` |
| Warna dominan cover album | `AlbumColor.cs` |
| Terjemahan ID / EN / ZH | `Loc.cs` |
| Jendela pengaturan | `SettingsWindow.xaml(.cs)` |
| Template & kode share (`SYLO1-` + JSON terkompresi, base64url) | `PresetStore.cs` |
| Ikon tray, hotkey, alur utama | `TrayIcon.cs`, `App.xaml.cs` |

Pengaturan disimpan di `%AppData%\SyLO\settings.json`, template di `presets.json`, model AI di `models\`.
Log ada di `%AppData%\SyLO\debug.log` dan `error.log`.

## Pihak ketiga

- Lirik: [LRCLIB](https://lrclib.net) (komunitas).
- Model kedalaman: [Depth Anything V2 Small](https://huggingface.co/onnx-community/depth-anything-v2-small) — Apache-2.0,
  tidak disertakan di paket; diunduh oleh pengguna dari jendela pengaturan.
- [ONNX Runtime](https://github.com/microsoft/onnxruntime) — MIT.
