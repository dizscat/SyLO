using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Windows.Media.Control;

namespace SyLO;

public sealed record TrackInfo(string Title, string Artist, string Album, TimeSpan Duration)
{
    public string Key => $"{Title}\u001f{Artist}\u001f{Album}";
}

/// <summary>
/// Membaca lagu yang sedang diputar Spotify lewat Windows Media Session (SMTC).
/// Tidak perlu login / API key. Posisi lagu dihitung sendiri di antara update
/// karena Spotify hanya melapor posisi saat play/pause/seek.
/// </summary>
public sealed class MediaWatcher
{
    GlobalSystemMediaTransportControlsSessionManager? _mgr;
    readonly Stopwatch _sw = new();
    TimeSpan _anchorPos;
    DateTimeOffset _lastUpdated;
    TimeSpan _lastReportedPos;
    bool _busy;

    public TrackInfo? Current { get; private set; }
    public bool IsPlaying { get; private set; }
    public event Action<TrackInfo?>? TrackChanged;
    /// <summary>Warna dominan cover album lagu sekarang (null jika tidak ada / tidak berwarna).</summary>
    public event Action<System.Windows.Media.Color?>? AlbumColorChanged;

    string? _coverKey;
    DateTime _trackChangedUtc;
    bool _coverLoading;

    public TimeSpan Position
    {
        get
        {
            var p = _anchorPos + (IsPlaying ? _sw.Elapsed : TimeSpan.Zero);
            if (Current is { Duration.Ticks: > 0 } c && p > c.Duration) p = c.Duration;
            return p < TimeSpan.Zero ? TimeSpan.Zero : p;
        }
    }

    public async Task PollAsync()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            _mgr ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();

            GlobalSystemMediaTransportControlsSession? session = null;
            foreach (var s in _mgr.GetSessions())
            {
                if (s.SourceAppUserModelId?.Contains("spotify", StringComparison.OrdinalIgnoreCase) == true)
                {
                    session = s;
                    break;
                }
            }

            if (session == null) { Clear(); return; }

            var props = await session.TryGetMediaPropertiesAsync();
            var timeline = session.GetTimelineProperties();
            var playback = session.GetPlaybackInfo();

            string title = props?.Title?.Trim() ?? "";
            string artist = props?.Artist?.Trim() ?? "";
            string album = props?.AlbumTitle?.Trim() ?? "";
            if (title.Length == 0) { Clear(); return; }

            bool playing = playback?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            var duration = timeline.EndTime - timeline.StartTime;
            if (duration < TimeSpan.Zero) duration = TimeSpan.Zero;

            var track = new TrackInfo(title, artist, album, duration);
            bool trackChanged = Current == null || Current.Key != track.Key;
            bool newReport = timeline.LastUpdatedTime != _lastUpdated || timeline.Position != _lastReportedPos;

            if (trackChanged || newReport)
            {
                var reported = timeline.Position;
                if (playing)
                {
                    var lag = DateTimeOffset.Now - timeline.LastUpdatedTime;
                    if (lag > TimeSpan.Zero && lag < TimeSpan.FromMinutes(30)) reported += lag;
                }
                _anchorPos = reported;
                _sw.Restart();
                _lastUpdated = timeline.LastUpdatedTime;
                _lastReportedPos = timeline.Position;
            }
            else if (playing != IsPlaying)
            {
                // status berubah tanpa laporan posisi baru: bekukan / lanjutkan dari posisi hitungan
                _anchorPos = Position;
                _sw.Restart();
            }

            IsPlaying = playing;
            Current = track; // durasi bisa menyusul setelah ganti lagu
            if (trackChanged)
            {
                _trackChangedUtc = DateTime.UtcNow;
                TrackChanged?.Invoke(track);
            }

            // Cover album: tunggu sebentar setelah ganti lagu, karena Spotify memperbarui gambarnya belakangan
            if (_coverKey != track.Key && !_coverLoading && props?.Thumbnail != null
                && (DateTime.UtcNow - _trackChangedUtc).TotalMilliseconds > 900)
                _ = LoadCoverAsync(props.Thumbnail, track.Key);
        }
        catch
        {
            _mgr = null; // coba sambung ulang di poll berikutnya
        }
        finally
        {
            _busy = false;
        }
    }

    async Task LoadCoverAsync(Windows.Storage.Streams.IRandomAccessStreamReference thumb, string key)
    {
        _coverLoading = true;
        try
        {
            using var ras = await thumb.OpenReadAsync();
            using var stream = ras.AsStreamForRead();
            var ms = new System.IO.MemoryStream();
            await stream.CopyToAsync(ms);
            ms.Position = 0;
            var color = await Task.Run(() =>
            {
                using (ms) return AlbumColor.Dominant(ms);
            });
            _coverKey = key;
            if (Current?.Key == key) AlbumColorChanged?.Invoke(color);
        }
        catch
        {
            _coverKey = key; // jangan coba terus-menerus untuk lagu yang sama
        }
        finally
        {
            _coverLoading = false;
        }
    }

    void Clear()
    {
        IsPlaying = false;
        if (Current != null)
        {
            Current = null;
            TrackChanged?.Invoke(null);
        }
    }
}
