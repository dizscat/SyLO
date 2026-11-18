using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace SyLO;

/// <summary>Satu template gaya + posisi yang disimpan.</summary>
public sealed class Preset : INotifyPropertyChanged
{
    string _name = "";
    public string Name
    {
        get => _name;
        set { _name = value; OnChanged(); }
    }

    public DateTime Created { get; set; } = DateTime.Now;
    public JsonObject Data { get; set; } = new();

    [JsonIgnore]
    public string Subtitle
    {
        get
        {
            try
            {
                return $"{Created:dd MMM yyyy HH:mm} · {Data["Anchor"]?.GetValue<string>() ?? "-"} · " +
                       $"{Data["FontFamily"]?.GetValue<string>() ?? "-"} {Data["FontSize"]?.GetValue<double>() ?? 0:0}px";
            }
            catch { return $"{Created:dd MMM yyyy HH:mm}"; }
        }
    }

    public void NotifyDataChanged() => OnChanged(nameof(Subtitle));

    public event PropertyChangedEventHandler? PropertyChanged;
    void OnChanged([CallerMemberName] string? n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

/// <summary>
/// Menyimpan template di %AppData%\SyLO\presets.json, dan mengubah template
/// menjadi kode teks yang bisa dibagikan (SYLO1-xxxx) / sebaliknya.
/// </summary>
public sealed class PresetStore
{
    public const string CodePrefix = "SYLO1-";
    const int MaxCodeLength = 20_000;
    const int MaxJsonBytes = 64 * 1024;
    public const int MaxNameLength = 60;

    /// <summary>Pengaturan yang bukan bagian dari "gaya & posisi".</summary>
    static readonly string[] Excluded =
    {
        nameof(AppSettings.RunAtStartup),
        nameof(AppSettings.OverlayVisible),
        nameof(AppSettings.SyncOffsetMs),
        nameof(AppSettings.Language),
    };

    /// <summary>Monitor tergantung komputer masing-masing, jadi tidak ikut di kode share.</summary>
    const string MachineSpecific = nameof(AppSettings.MonitorIndex);

    static readonly JsonSerializerOptions Opts = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    static string FilePath => Path.Combine(AppSettings.Folder, "presets.json");

    public ObservableCollection<Preset> Items { get; } = new();

    /// <summary>Kondisi sebelum template terakhir dimuat (untuk tombol Urungkan).</summary>
    public JsonObject? UndoSnapshot { get; set; }

    // ---------------- Simpan / muat file ----------------

    public static PresetStore Load()
    {
        var store = new PresetStore();
        try
        {
            if (File.Exists(FilePath))
            {
                var list = JsonSerializer.Deserialize<Preset[]>(File.ReadAllText(FilePath), Opts);
                if (list != null)
                    foreach (var p in list.Where(p => p.Data != null)) store.Items.Add(p);
            }
        }
        catch
        {
            // file rusak: simpan cadangan supaya template lama tidak hilang begitu saja
            try { File.Copy(FilePath, FilePath + ".rusak", true); } catch { }
        }
        return store;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(AppSettings.Folder);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(Items.ToArray(), Opts));
            File.Move(tmp, FilePath, true);
        }
        catch { }
    }

    public Preset? Find(string name) =>
        Items.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    public string UniqueName(string name)
    {
        if (Find(name) == null) return name;
        for (int i = 2; ; i++)
        {
            var n = $"{name} ({i})";
            if (Find(n) == null) return n;
        }
    }

    public static string CleanName(string? name)
    {
        var n = (name ?? "").Trim();
        n = new string(n.Where(c => !char.IsControl(c)).ToArray());
        if (n.Length > MaxNameLength) n = n[..MaxNameLength];
        return n;
    }

    // ---------------- Ambil / terapkan pengaturan ----------------

    static bool IsPresetProperty(System.Reflection.PropertyInfo p) =>
        p.CanWrite && p.CanRead && !Excluded.Contains(p.Name);

    /// <summary>Ambil gaya & posisi saat ini.</summary>
    public static JsonObject Capture(AppSettings s, bool forShare = false)
    {
        var obj = new JsonObject();
        foreach (var p in typeof(AppSettings).GetProperties().Where(IsPresetProperty))
        {
            if (forShare && p.Name == MachineSpecific) continue;
            obj[p.Name] = JsonSerializer.SerializeToNode(p.GetValue(s), p.PropertyType, Opts);
        }
        return obj;
    }

    /// <summary>Terapkan template. Nilai yang tidak dikenal / rusak dilewati; angka tetap dibatasi oleh setter.</summary>
    public static void Apply(AppSettings s, JsonObject data)
    {
        foreach (var (key, value) in data)
        {
            var p = typeof(AppSettings).GetProperty(key);
            if (p == null || !IsPresetProperty(p) || value == null) continue;
            try
            {
                var v = value.Deserialize(p.PropertyType, Opts);
                if (v != null) p.SetValue(s, v);
            }
            catch { /* abaikan nilai yang tidak valid */ }
        }
    }

    /// <summary>Muat template ke pengaturan, simpan kondisi sebelumnya untuk Urungkan.</summary>
    public void LoadInto(AppSettings s, JsonObject data)
    {
        UndoSnapshot = Capture(s);
        Apply(s, data);
    }

    public bool Undo(AppSettings s)
    {
        if (UndoSnapshot == null) return false;
        var snap = UndoSnapshot;
        UndoSnapshot = null;
        Apply(s, snap);
        return true;
    }

    // ---------------- Kode share ----------------

    public static string ToCode(string name, JsonObject data)
    {
        var clean = (JsonObject)data.DeepClone();
        clean.Remove(MachineSpecific);
        var payload = new JsonObject { ["v"] = 1, ["n"] = name, ["s"] = clean };

        var json = Encoding.UTF8.GetBytes(payload.ToJsonString());
        using var ms = new MemoryStream();
        using (var z = new DeflateStream(ms, CompressionLevel.SmallestSize, leaveOpen: true))
            z.Write(json);

        return CodePrefix + Convert.ToBase64String(ms.ToArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static bool TryParseCode(string? code, out string name, out JsonObject data, out string error)
    {
        name = "";
        data = new JsonObject();
        error = "";

        var c = new string((code ?? "").Where(ch => !char.IsWhiteSpace(ch)).ToArray());
        if (c.Length == 0) { error = Loc.T("Kode masih kosong."); return false; }
        if (!c.StartsWith(CodePrefix, StringComparison.OrdinalIgnoreCase))
        {
            error = Loc.F("Ini bukan kode template SyLO (harus diawali \"{0}\").", CodePrefix);
            return false;
        }
        if (c.Length > MaxCodeLength) { error = Loc.T("Kode terlalu panjang."); return false; }

        try
        {
            var b64 = c[CodePrefix.Length..].Replace('-', '+').Replace('_', '/');
            b64 = b64.PadRight(b64.Length + (4 - b64.Length % 4) % 4, '=');
            var compressed = Convert.FromBase64String(b64);

            using var input = new DeflateStream(new MemoryStream(compressed), CompressionMode.Decompress);
            using var output = new MemoryStream();
            var buf = new byte[4096];
            int read;
            while ((read = input.Read(buf, 0, buf.Length)) > 0)
            {
                output.Write(buf, 0, read);
                if (output.Length > MaxJsonBytes) { error = Loc.T("Isi kode terlalu besar."); return false; }
            }

            if (JsonNode.Parse(output.ToArray()) is not JsonObject payload || payload["s"] is not JsonObject s)
            {
                error = Loc.T("Kode rusak atau tidak lengkap.");
                return false;
            }

            name = CleanName(payload["n"]?.GetValue<string>());
            if (name.Length == 0) name = Loc.T("Template impor");
            s.Remove(MachineSpecific);
            data = (JsonObject)s.DeepClone();
            return true;
        }
        catch
        {
            error = Loc.T("Kode rusak atau salah salin. Pastikan seluruh kode tersalin utuh.");
            return false;
        }
    }
}
