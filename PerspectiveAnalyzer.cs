using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;

namespace SyLO;

/// <summary>Satu garis perspektif: berangkat dari titik hilang (vanishing point) ke arah luar.</summary>
/// <param name="Angle">Arah dari titik hilang ke luar (radian, koordinat layar: y ke bawah).</param>
/// <param name="Length">Panjang garis yang benar-benar terlihat di gambar, sebagai fraksi lebar layar.</param>
/// <param name="Strength">Seberapa kuat garis ini (jumlah piksel tepi yang mendukung).</param>
public sealed record PerspectiveRay(double Angle, double Length, double Strength);

/// <summary>Titik hilang (fraksi layar 0..1, boleh sedikit di luar layar) dan garis-garis yang menuju ke sana.</summary>
public sealed record PerspectiveInfo(double VpX, double VpY, IReadOnlyList<PerspectiveRay> Rays);

/// <summary>
/// Mencari titik hilang & garis perspektif (seperti pagar dermaga / rel / lorong yang menyempit ke satu titik)
/// dengan deteksi tepi (Sobel) + transformasi Hough + voting titik potong.
/// </summary>
public static class PerspectiveAnalyzer
{
    const int W = 320;
    const int ThetaBins = 180;

    record Line(double Theta, double Rho, double Votes)
    {
        // garis: x*cos(t) + y*sin(t) = rho; arah garis tegak lurus normal
        public double Dist(double x, double y) => Math.Abs(x * Math.Cos(Theta) + y * Math.Sin(Theta) - Rho);
    }

    /// <summary>Info diagnosa analisis terakhir (untuk debug.log).</summary>
    public static string LastDebug { get; private set; } = "";

    public static PerspectiveInfo? Analyze(Bitmap image)
    {
        int h = Math.Max(20, W * image.Height / Math.Max(1, image.Width));
        var gray = Gray(image, W, h);

        // ---- Sobel ----
        var mag = new float[W * h];
        var ang = new float[W * h];
        for (int y = 1; y < h - 1; y++)
        for (int x = 1; x < W - 1; x++)
        {
            float gx = -gray[(y - 1) * W + x - 1] - 2 * gray[y * W + x - 1] - gray[(y + 1) * W + x - 1]
                       + gray[(y - 1) * W + x + 1] + 2 * gray[y * W + x + 1] + gray[(y + 1) * W + x + 1];
            float gy = -gray[(y - 1) * W + x - 1] - 2 * gray[(y - 1) * W + x] - gray[(y - 1) * W + x + 1]
                       + gray[(y + 1) * W + x - 1] + 2 * gray[(y + 1) * W + x] + gray[(y + 1) * W + x + 1];
            mag[y * W + x] = MathF.Sqrt(gx * gx + gy * gy);
            ang[y * W + x] = MathF.Atan2(gy, gx);
        }

        // ambil ±10% piksel dengan tepi terkuat
        var sample = mag.Where((_, i) => i % 5 == 0).OrderBy(v => v).ToArray();
        float thr = Math.Max(12f, sample[(int)(sample.Length * 0.90)]); // ambang rendah supaya wallpaper gelap tetap terbaca
        var edge = new bool[W * h];
        for (int i = 0; i < mag.Length; i++) edge[i] = mag[i] >= thr;

        // ---- Hough (hanya sudut yang sesuai arah gradien, supaya cepat & bersih) ----
        int diag = (int)Math.Ceiling(Math.Sqrt(W * W + h * h));
        int rhoBins = diag * 2 + 1;
        var acc = new float[ThetaBins * rhoBins];
        var cos = new double[ThetaBins];
        var sin = new double[ThetaBins];
        for (int t = 0; t < ThetaBins; t++) { cos[t] = Math.Cos(t * Math.PI / ThetaBins); sin[t] = Math.Sin(t * Math.PI / ThetaBins); }

        for (int y = 1; y < h - 1; y++)
        for (int x = 1; x < W - 1; x++)
        {
            int i = y * W + x;
            if (!edge[i]) continue;
            double th = ang[i];
            if (th < 0) th += Math.PI;
            int tc = (int)Math.Round(th / Math.PI * ThetaBins) % ThetaBins;
            for (int dt = -2; dt <= 2; dt++)
            {
                int t = (tc + dt + ThetaBins) % ThetaBins;
                // lewati garis (hampir) horizontal / vertikal: tidak menuju titik hilang
                if (t < 7 || t > ThetaBins - 7 || Math.Abs(t - ThetaBins / 2) < 7) continue;
                int r = (int)Math.Round(x * cos[t] + y * sin[t]) + diag;
                acc[t * rhoBins + r] += mag[i] / 255f;
            }
        }

        // ---- Ambil puncak (garis terkuat) ----
        var lines = new List<Line>();
        float top = acc.Max();
        if (top <= 0) return null;
        var work = (float[])acc.Clone();
        for (int k = 0; k < 70; k++)
        {
            int best = 0;
            for (int i = 1; i < work.Length; i++) if (work[i] > work[best]) best = i;
            if (work[best] < top * 0.06f) break;
            int bt = best / rhoBins, br = best % rhoBins;
            lines.Add(new Line(bt * Math.PI / ThetaBins, br - diag, work[best]));
            for (int dt = -4; dt <= 4; dt++)
            for (int dr = -10; dr <= 10; dr++)
            {
                int t = bt + dt, r = br + dr;
                if (t < 0 || t >= ThetaBins || r < 0 || r >= rhoBins) continue;
                work[t * rhoBins + r] = 0;
            }
        }

        // Garis horizontal / vertikal tidak menuju titik hilang (sejajar bidang layar)
        var cand = lines.Where(l =>
        {
            double dir = (l.Theta + Math.PI / 2) % Math.PI;          // arah garis 0..π
            double fromH = Math.Min(dir, Math.PI - dir);             // jarak sudut ke horizontal
            double fromV = Math.Abs(dir - Math.PI / 2);              // jarak sudut ke vertikal
            return fromH > Deg(7) && fromV > Deg(7);
        }).ToList();
        LastDebug = $"lines={lines.Count} cand={cand.Count} thr={thr:0}";
        if (cand.Count < 3) return null;

        // ---- Titik hilang: titik potong yang didukung paling banyak garis ----
        double tol = W * 0.022;
        double bestScore = 0, vx = 0, vy = 0;
        for (int a = 0; a < cand.Count; a++)
        for (int b = a + 1; b < cand.Count; b++)
        {
            var p = Intersect(cand[a], cand[b]);
            if (p == null) continue;
            var (px, py) = p.Value;
            if (px < -0.25 * W || px > 1.25 * W || py < -0.25 * h || py > 1.25 * h) continue;
            double score = 0; int n = 0; int sectors = 0;
            foreach (var l in cand)
            {
                if (l.Dist(px, py) >= tol) continue;
                score += l.Votes; n++;
                sectors |= 1 << (int)(((l.Theta + Math.PI / 2) % Math.PI) / (Math.PI / 6)); // arah garis dibagi 6 sektor
            }
            // titik hilang asli didukung garis dari berbagai arah; titik palsu biasanya dari garis yang hampir searah
            score *= System.Numerics.BitOperations.PopCount((uint)sectors);
            if (n >= 3 && score > bestScore) { bestScore = score; vx = px; vy = py; }
        }
        LastDebug += $" vpScore={bestScore:0}";
        if (bestScore <= 0) return null;

        // perhalus titik hilang: kuadrat terkecil dari garis pendukung
        var support = cand.Where(l => l.Dist(vx, vy) < tol).ToList();
        (vx, vy) = Refine(support, vx, vy);

        // ---- Sinar dari titik hilang: pilih sisi garis yang benar-benar ada tepinya ----
        var rays = new List<PerspectiveRay>();
        foreach (var l in support)
        {
            double dx = -Math.Sin(l.Theta), dy = Math.Cos(l.Theta); // arah garis
            foreach (var s in new[] { 1, -1 })
            {
                double ux = dx * s, uy = dy * s;
                int hits = 0; double far = 0;
                for (double d = 6; ; d += 2)
                {
                    int x = (int)Math.Round(vx + ux * d), y = (int)Math.Round(vy + uy * d);
                    if (x < 1 || y < 1 || x >= W - 1 || y >= h - 1)
                    {
                        if (d > 2 * diag) break;
                        if (hits == 0 && (vx + ux * d < -W || vx + ux * d > 2 * W || vy + uy * d < -h || vy + uy * d > 2 * h)) break;
                        if (x < -W || y < -h || x > 2 * W || y > 2 * h) break;
                        continue; // titik hilang bisa di luar layar: jalan terus sampai masuk
                    }
                    if (edge[y * W + x] || edge[y * W + x + 1] || edge[(y + 1) * W + x]) { hits++; far = d; }
                }
                if (hits < 18) continue;
                double angle = Math.Atan2(uy, ux);
                if (rays.Any(r => AngleDiff(r.Angle, angle) < Deg(4))) continue;
                rays.Add(new PerspectiveRay(angle, far / W, hits));
            }
        }
        LastDebug += $" support={support.Count} rays={rays.Count}";
        if (rays.Count == 0) return null;
        return new PerspectiveInfo(vx / W, vy / h, rays.OrderByDescending(r => r.Strength).ToList());
    }

    static double Deg(double d) => d * Math.PI / 180;

    static double AngleDiff(double a, double b)
    {
        double d = Math.Abs(a - b) % (2 * Math.PI);
        return d > Math.PI ? 2 * Math.PI - d : d;
    }

    static (double, double)? Intersect(Line a, Line b)
    {
        double c1 = Math.Cos(a.Theta), s1 = Math.Sin(a.Theta), c2 = Math.Cos(b.Theta), s2 = Math.Sin(b.Theta);
        double det = c1 * s2 - s1 * c2;
        if (Math.Abs(det) < Math.Sin(Deg(4))) return null; // hampir sejajar
        return ((a.Rho * s2 - b.Rho * s1) / det, (c1 * b.Rho - c2 * a.Rho) / det);
    }

    static (double, double) Refine(List<Line> lines, double x0, double y0)
    {
        double a11 = 0, a12 = 0, a22 = 0, b1 = 0, b2 = 0;
        foreach (var l in lines)
        {
            double c = Math.Cos(l.Theta), s = Math.Sin(l.Theta), w = l.Votes;
            a11 += w * c * c; a12 += w * c * s; a22 += w * s * s;
            b1 += w * c * l.Rho; b2 += w * s * l.Rho;
        }
        double det = a11 * a22 - a12 * a12;
        if (Math.Abs(det) < 1e-9) return (x0, y0);
        return ((b1 * a22 - b2 * a12) / det, (a11 * b2 - a12 * b1) / det);
    }

    static float[] Gray(Bitmap src, int w, int h)
    {
        using var small = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(small))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
            g.DrawImage(src, 0, 0, w, h);
        }
        var res = new float[w * h];
        var data = small.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var row = new byte[data.Stride];
            for (int y = 0; y < h; y++)
            {
                Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, data.Stride);
                for (int x = 0; x < w; x++)
                    res[y * w + x] = 0.299f * row[x * 4 + 2] + 0.587f * row[x * 4 + 1] + 0.114f * row[x * 4];
            }
        }
        finally { small.UnlockBits(data); }
        return res;
    }
}
