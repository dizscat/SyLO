using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace SyLO;

/// <summary>Peta kedalaman relatif: 0 = jauh, 1 = paling dekat ke kamera.</summary>
public sealed class DepthMap
{
    public int Width { get; }
    public int Height { get; }
    public float[] Data { get; }

    public DepthMap(int w, int h, float[] data) { Width = w; Height = h; Data = data; }

    /// <summary>Kedalaman di posisi fraksi layar (0..1), interpolasi bilinear.</summary>
    public float At(double fx, double fy)
    {
        double x = Math.Clamp(fx * Width - 0.5, 0, Width - 1), y = Math.Clamp(fy * Height - 0.5, 0, Height - 1);
        int x0 = (int)x, y0 = (int)y, x1 = Math.Min(x0 + 1, Width - 1), y1 = Math.Min(y0 + 1, Height - 1);
        double tx = x - x0, ty = y - y0;
        double a = Data[y0 * Width + x0] * (1 - tx) + Data[y0 * Width + x1] * tx;
        double b = Data[y1 * Width + x0] * (1 - tx) + Data[y1 * Width + x1] * tx;
        return (float)(a * (1 - ty) + b * ty);
    }
}

/// <summary>
/// Menjalankan model AI "Depth Anything V2 Small" (ONNX, Apache-2.0) untuk menebak objek mana di wallpaper
/// yang berada di depan — dipakai supaya lirik bisa "lewat di belakang" karakter / tiang / objek dekat.
/// </summary>
public sealed class DepthEstimator : IDisposable
{
    public const string ModelUrl =
        "https://huggingface.co/onnx-community/depth-anything-v2-small/resolve/main/onnx/model_quantized.onnx";
    const long MinModelBytes = 10 * 1024 * 1024;

    public static string ModelPath => Path.Combine(AppSettings.Folder, "models", "depth-anything-v2-small-q8.onnx");
    public static bool ModelExists => File.Exists(ModelPath) && new FileInfo(ModelPath).Length > MinModelBytes;

    static readonly float[] Mean = { 0.485f, 0.456f, 0.406f };
    static readonly float[] Std = { 0.229f, 0.224f, 0.225f };

    InferenceSession? _session;
    string _input = "", _output = "";
    readonly object _lock = new();

    /// <summary>Tinggi input model (kelipatan 14). Lebih kecil = lebih cepat, lebih kasar.</summary>
    public int InputHeight { get; set; } = 308;

    bool EnsureSession()
    {
        if (_session != null) return true;
        if (!ModelExists) return false;
        var opt = new SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
            IntraOpNumThreads = Math.Clamp(Environment.ProcessorCount / 4, 1, 4) // jangan sampai mengganggu game
        };
        _session = new InferenceSession(ModelPath, opt);
        _input = _session.InputMetadata.Keys.First();
        _output = _session.OutputMetadata.Keys.First();
        return true;
    }

    public DepthMap? Estimate(Bitmap image)
    {
        lock (_lock)
        {
            if (!EnsureSession()) return null;

            int h = Math.Max(14, InputHeight / 14 * 14);
            int w = Math.Max(14, (int)Math.Round(h * (double)image.Width / image.Height / 14.0) * 14);

            using var small = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(small))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                g.DrawImage(image, 0, 0, w, h);
            }

            var input = new DenseTensor<float>(new[] { 1, 3, h, w });
            var data = small.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                var row = new byte[data.Stride];
                for (int y = 0; y < h; y++)
                {
                    Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, data.Stride);
                    for (int x = 0; x < w; x++)
                    {
                        int i = x * 4; // BGRA
                        input[0, 0, y, x] = (row[i + 2] / 255f - Mean[0]) / Std[0];
                        input[0, 1, y, x] = (row[i + 1] / 255f - Mean[1]) / Std[1];
                        input[0, 2, y, x] = (row[i + 0] / 255f - Mean[2]) / Std[2];
                    }
                }
            }
            finally { small.UnlockBits(data); }

            using var results = _session!.Run(new[] { NamedOnnxValue.CreateFromTensor(_input, input) });
            var output = results.First(r => r.Name == _output).AsTensor<float>();
            int oh = output.Dimensions[^2], ow = output.Dimensions[^1];
            var raw = output.ToArray();

            // Normalisasi pakai persentil 2%–98% supaya satu titik ekstrem tidak merusak skala
            var sorted = raw.Where((_, i) => i % 7 == 0).OrderBy(v => v).ToArray();
            float lo = sorted[(int)(sorted.Length * 0.02)], hi = sorted[(int)(sorted.Length * 0.98)];
            float range = Math.Max(1e-6f, hi - lo);
            var norm = new float[ow * oh];
            for (int i = 0; i < norm.Length; i++) norm[i] = Math.Clamp((raw[i] - lo) / range, 0, 1);
            return new DepthMap(ow, oh, Foreground(norm, ow, oh));
        }
    }

    /// <summary>
    /// Ubah kedalaman jadi "seberapa menonjol ke depan dibanding sekitarnya" (0..1).
    /// Lantai/dinding yang jaraknya berubah halus jadi ~0, sedangkan tiang, karakter, dll. jadi tinggi.
    /// </summary>
    static float[] Foreground(float[] d, int w, int h)
    {
        int r = Math.Max(4, w / 9);
        var blur = BoxBlur(BoxBlur(d, w, h, r), w, h, r); // dua kali box blur ≈ blur gaussian
        var fg = new float[d.Length];
        for (int i = 0; i < d.Length; i++)
        {
            // objek lebih dekat dari sekitarnya; dikali sedikit dengan kedalaman asli supaya latar jauh tidak ikut
            float pop = (d[i] - blur[i]) / 0.18f;
            fg[i] = Math.Clamp(pop * (0.4f + 0.6f * d[i]), 0, 1);
        }
        return BoxBlur(fg, w, h, 1);
    }

    static float[] BoxBlur(float[] src, int w, int h, int r)
    {
        var tmp = new float[src.Length];
        var dst = new float[src.Length];
        for (int y = 0; y < h; y++)
        {
            double sum = 0; int n = 0;
            for (int x = -r; x <= r; x++) if (x >= 0 && x < w) { sum += src[y * w + x]; n++; }
            for (int x = 0; x < w; x++)
            {
                tmp[y * w + x] = (float)(sum / n);
                int add = x + r + 1, rem = x - r;
                if (add < w) { sum += src[y * w + add]; n++; }
                if (rem >= 0) { sum -= src[y * w + rem]; n--; }
            }
        }
        for (int x = 0; x < w; x++)
        {
            double sum = 0; int n = 0;
            for (int y = -r; y <= r; y++) if (y >= 0 && y < h) { sum += tmp[y * w + x]; n++; }
            for (int y = 0; y < h; y++)
            {
                dst[y * w + x] = (float)(sum / n);
                int add = y + r + 1, rem = y - r;
                if (add < h) { sum += tmp[add * w + x]; n++; }
                if (rem >= 0) { sum -= tmp[rem * w + x]; n--; }
            }
        }
        return dst;
    }

    /// <summary>Unduh model (dipanggil dari tombol di pengaturan).</summary>
    public static async Task DownloadModelAsync(IProgress<double>? progress, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ModelPath)!);
        var tmp = ModelPath + ".download";
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("SyLO/1.0");
        using var res = await http.GetAsync(ModelUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        res.EnsureSuccessStatusCode();
        long total = res.Content.Headers.ContentLength ?? -1;
        await using (var src = await res.Content.ReadAsStreamAsync(ct))
        await using (var dst = File.Create(tmp))
        {
            var buf = new byte[81920];
            long done = 0;
            int n;
            while ((n = await src.ReadAsync(buf, ct)) > 0)
            {
                await dst.WriteAsync(buf.AsMemory(0, n), ct);
                done += n;
                if (total > 0) progress?.Report((double)done / total);
            }
        }
        if (new FileInfo(tmp).Length < MinModelBytes) { File.Delete(tmp); throw new IOException("File model tidak lengkap."); }
        File.Move(tmp, ModelPath, true);
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _session?.Dispose();
            _session = null;
        }
    }
}
