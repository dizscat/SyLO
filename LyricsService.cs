using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace SyLO;

public sealed record LyricLine(TimeSpan Time, string Text);

public enum LyricsStatus { None, Loading, Synced, Unsynced, Instrumental, NotFound, Error }

public sealed class Lyrics
{
    public static readonly Lyrics NotFound = new(LyricsStatus.NotFound);
    public static readonly Lyrics Instrumental = new(LyricsStatus.Instrumental);
    public static readonly Lyrics Unsynced = new(LyricsStatus.Unsynced);
    public static readonly Lyrics Error = new(LyricsStatus.Error);

    public LyricsStatus Status { get; }
    public IReadOnlyList<LyricLine> Lines { get; }

    public Lyrics(LyricsStatus status, IReadOnlyList<LyricLine>? lines = null)
    {
        Status = status;
        Lines = lines ?? Array.Empty<LyricLine>();
    }

    /// <summary>Index baris terakhir yang waktunya &lt;= t, atau -1 jika belum mulai.</summary>
    public int IndexAt(TimeSpan t)
    {
        int lo = 0, hi = Lines.Count - 1, ans = -1;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            if (Lines[mid].Time <= t) { ans = mid; lo = mid + 1; }
            else hi = mid - 1;
        }
        return ans;
    }
}

public static class LrcParser
{
    static readonly Regex Tag = new(@"\[(\d{1,3}):(\d{1,2})(?:[.:](\d{1,3}))?\]", RegexOptions.Compiled);
    static readonly Regex WordTag = new(@"<\d{1,3}:\d{1,2}(?:[.:]\d{1,3})?>", RegexOptions.Compiled);

    public static List<LyricLine> Parse(string lrc)
    {
        var list = new List<LyricLine>();
        foreach (var raw in lrc.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            var times = new List<TimeSpan>();
            int textStart = 0;
            foreach (Match m in Tag.Matches(line))
            {
                if (m.Index != textStart) break; // hanya tag di awal baris
                times.Add(ToTime(m));
                textStart = m.Index + m.Length;
            }
            if (times.Count == 0) continue;
            var text = WordTag.Replace(line[textStart..], "").Trim();
            foreach (var t in times) list.Add(new LyricLine(t, text));
        }
        list.Sort((a, b) => a.Time.CompareTo(b.Time));
        return list;
    }

    static TimeSpan ToTime(Match m)
    {
        int min = int.Parse(m.Groups[1].Value);
        int sec = int.Parse(m.Groups[2].Value);
        int ms = 0;
        if (m.Groups[3].Success)
        {
            var f = m.Groups[3].Value;
            ms = f.Length switch { 1 => int.Parse(f) * 100, 2 => int.Parse(f) * 10, _ => int.Parse(f) };
        }
        return TimeSpan.FromMilliseconds((min * 60 + sec) * 1000.0 + ms);
    }
}

/// <summary>Mengambil lirik tersinkron dari LRCLIB (https://lrclib.net) — gratis & tanpa API key.</summary>
public sealed class LyricsService : IDisposable
{
    readonly HttpClient _http;
    readonly Dictionary<string, Lyrics> _cache = new();

    static readonly Regex DashSuffix = new(@"\s+-\s+.*$", RegexOptions.Compiled);
    static readonly Regex BracketNoise = new(
        @"\s*[\(\[][^\)\]]*(feat\.|ft\.|with |remaster|version|live|edit|mix|mono|stereo|deluxe)[^\)\]]*[\)\]]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public LyricsService()
    {
        _http = new HttpClient { BaseAddress = new Uri("https://lrclib.net/"), Timeout = TimeSpan.FromSeconds(12) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("SyLO/1.0 (desktop lyrics overlay)");
    }

    public async Task<Lyrics> GetAsync(TrackInfo t, CancellationToken ct)
    {
        if (_cache.TryGetValue(t.Key, out var cached)) return cached;

        Lyrics? best = null;
        try
        {
            int dur = (int)Math.Round(t.Duration.TotalSeconds);
            string artist = PrimaryArtist(t.Artist);

            if (dur > 0 && t.Album.Length > 0)
                best = await TryGet(t.Title, t.Artist, t.Album, dur, ct);

            if (!IsFinal(best))
                best = Better(best, await TrySearch(t.Title, artist, dur, ct));

            var clean = CleanTitle(t.Title);
            if (!IsFinal(best) && clean.Length > 0 && clean != t.Title)
                best = Better(best, await TrySearch(clean, artist, dur, ct));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch
        {
            return Lyrics.Error; // jangan di-cache: coba lagi saat lagu diputar ulang
        }

        best ??= Lyrics.NotFound;
        _cache[t.Key] = best;
        return best;
    }

    static bool IsFinal(Lyrics? l) => l is { Status: LyricsStatus.Synced or LyricsStatus.Instrumental };

    static Lyrics? Better(Lyrics? a, Lyrics? b)
    {
        static int Rank(Lyrics? l) => l?.Status switch
        {
            LyricsStatus.Synced => 3,
            LyricsStatus.Instrumental => 2,
            LyricsStatus.Unsynced => 1,
            _ => 0
        };
        return Rank(b) > Rank(a) ? b : a;
    }

    async Task<Lyrics?> TryGet(string title, string artist, string album, int dur, CancellationToken ct)
    {
        var url = $"api/get?track_name={E(title)}&artist_name={E(artist)}&album_name={E(album)}&duration={dur}";
        using var res = await _http.GetAsync(url, ct);
        if (res.StatusCode == HttpStatusCode.NotFound) return null;
        res.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
        return FromJson(doc.RootElement);
    }

    async Task<Lyrics?> TrySearch(string title, string artist, int dur, CancellationToken ct)
    {
        var url = $"api/search?track_name={E(title)}" + (artist.Length > 0 ? $"&artist_name={E(artist)}" : "");
        using var res = await _http.GetAsync(url, ct);
        if (res.StatusCode == HttpStatusCode.NotFound) return null;
        res.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;

        Lyrics? best = null;
        double bestDiff = double.MaxValue;
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            var l = FromJson(item);
            if (l == null) continue;

            double diff = 0;
            if (dur > 0 && item.TryGetProperty("duration", out var d) && d.ValueKind == JsonValueKind.Number)
                diff = Math.Abs(d.GetDouble() - dur);

            // lirik tersinkron dari versi lagu yang durasinya beda jauh pasti meleset
            if (l.Status == LyricsStatus.Synced && diff > 8) continue;

            bool better = best == null
                || (l.Status == LyricsStatus.Synced && best.Status != LyricsStatus.Synced)
                || (l.Status == best.Status && diff < bestDiff);
            if (better) { best = l; bestDiff = diff; }
        }
        return best;
    }

    static Lyrics? FromJson(JsonElement e)
    {
        if (e.TryGetProperty("instrumental", out var ins) && ins.ValueKind == JsonValueKind.True)
            return Lyrics.Instrumental;

        var synced = Str(e, "syncedLyrics");
        if (!string.IsNullOrWhiteSpace(synced))
        {
            var lines = LrcParser.Parse(synced);
            if (lines.Count > 0) return new Lyrics(LyricsStatus.Synced, lines);
        }
        return string.IsNullOrWhiteSpace(Str(e, "plainLyrics")) ? null : Lyrics.Unsynced;
    }

    static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    static string E(string s) => Uri.EscapeDataString(s);

    static string PrimaryArtist(string artist)
    {
        int i = artist.IndexOf(", ", StringComparison.Ordinal);
        return i > 0 ? artist[..i] : artist;
    }

    static string CleanTitle(string title) => BracketNoise.Replace(DashSuffix.Replace(title, ""), "").Trim();

    public void Dispose() => _http.Dispose();
}
