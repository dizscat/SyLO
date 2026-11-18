using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace SyLO;

/// <summary>
/// Tata letak "lirik di garis perspektif": huruf berjajar di sepanjang satu garis yang menuju titik hilang,
/// makin dekat ke titik hilang makin kecil, dan miring mengikuti arah garis.
/// Mendukung beberapa titik hilang (1, 2, 3, 4 titik, dst.), termasuk titik di luar layar.
/// </summary>
public static class PerspectiveLayout
{
    /// <summary>Titik hilang buatan sendiri (editor) → garis-garis kipas di sekelilingnya.</summary>
    public static List<PerspectiveInfo> FromManual(IReadOnlyList<Point> points, int raysPerPoint)
    {
        int n = Math.Clamp(raysPerPoint, 2, 72);
        var list = new List<PerspectiveInfo>();
        foreach (var p in points)
        {
            var rays = new List<PerspectiveRay>();
            for (int i = 0; i < n; i++)
            {
                double a = (i + 0.5) * 2 * Math.PI / n; // +0.5 supaya tidak tepat horizontal/vertikal
                rays.Add(new PerspectiveRay(a, 3.0, 1));
            }
            list.Add(new PerspectiveInfo(p.X, p.Y, rays));
        }
        return list;
    }

    /// <summary>Format teks titik di pengaturan: "x,y;x,y" (fraksi layar, boleh di luar 0..1).</summary>
    public static List<Point> ParsePoints(string? s)
    {
        var pts = new List<Point>();
        foreach (var part in (s ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var xy = part.Split(',');
            if (xy.Length == 2
                && double.TryParse(xy[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
                && double.TryParse(xy[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
                pts.Add(new Point(Math.Clamp(x, -2, 3), Math.Clamp(y, -2, 3)));
        }
        return pts;
    }

    public static string FormatPoints(IEnumerable<Point> pts) =>
        string.Join(";", pts.Select(p => string.Create(CultureInfo.InvariantCulture, $"{p.X:0.####},{p.Y:0.####}")));

    /// <summary>Preset posisi titik hilang untuk perspektif 1–4 titik.</summary>
    public static List<Point> Preset(int count) => count switch
    {
        1 => new() { new(0.5, 0.42) },
        2 => new() { new(-0.12, 0.42), new(1.12, 0.42) },
        3 => new() { new(-0.12, 0.40), new(1.12, 0.40), new(0.5, 1.22) },
        _ => new() { new(-0.12, 0.42), new(1.12, 0.42), new(0.5, -0.25), new(0.5, 1.22) },
    };

    /// <param name="infos">Titik hilang beserta garisnya (otomatis atau buatan sendiri).</param>
    /// <param name="sw">Lebar layar (DIP).</param>
    /// <param name="sh">Tinggi layar (DIP).</param>
    /// <param name="lastKey">Garis yang dipakai baris sebelumnya (dihindari), lalu diisi garis baru.</param>
    /// <param name="setOrigin">Dipanggil dengan pojok kiri-atas area teks (DIP, relatif ke layar).</param>
    /// <param name="minAbsX">Batas kemiringan: 0.35 ≈ maks. ±70° dari horizontal; lebih kecil = boleh lebih tegak.</param>
    public static Func<IReadOnlyList<Rect>, double, (Matrix[] Mats, Size Size)>? Create(
        IReadOnlyList<PerspectiveInfo> infos, double sw, double sh, Random rng, ref double lastKey,
        Action<Point> setOrigin, double minAbsX = 0.35)
    {
        // calon garis: tidak terlalu tegak dan punya bagian yang cukup panjang di dalam layar
        var options = new List<(Point Vp, PerspectiveRay Ray, double Near, double Far, double Key)>();
        for (int k = 0; k < infos.Count; k++)
        {
            var info = infos[k];
            var vp = new Point(info.VpX * sw, info.VpY * sh);
            foreach (var r in info.Rays)
            {
                var o = new Vector(Math.Cos(r.Angle), Math.Sin(r.Angle));
                if (Math.Abs(o.X) < minAbsX) continue;
                if (!InsideSegment(vp, o, sw, sh, out double tIn, out double tOut)) continue;
                double near = Math.Max(tIn, 0.02 * sw);
                double far = Math.Min(tOut, near + Math.Max(r.Length * sw, 0.3 * sw));
                if (far - near < 0.18 * sw) continue;
                options.Add((vp, r, near, far, k * 10 + r.Angle));
            }
        }
        if (options.Count == 0) return null;

        // pilih acak (berbobot kekuatan garis), hindari garis yang sama dengan baris sebelumnya
        double prev = lastKey;
        var pool = options.Count > 1 && !double.IsNaN(prev)
            ? options.Where(x => Math.Abs(x.Key - prev) > 0.05).ToList()
            : options;
        if (pool.Count == 0) pool = options;
        double total = pool.Sum(x => x.Ray.Strength), pick = rng.NextDouble() * total;
        var chosen = pool[^1];
        foreach (var x in pool) { pick -= x.Ray.Strength; if (pick <= 0) { chosen = x; break; } }
        lastKey = chosen.Key;

        var vpc = chosen.Vp;
        var outward = new Vector(Math.Cos(chosen.Ray.Angle), Math.Sin(chosen.Ray.Angle));
        double nearLimit = chosen.Near;
        // jarak ujung teks terjauh dari titik hilang (di dalam bagian garis yang terlihat)
        double D = nearLimit + (chosen.Far - nearLimit) * (0.72 + rng.NextDouble() * 0.24);
        bool towardVp = outward.X <= 0; // teks selalu dibaca kiri→kanan
        var read = towardVp ? -outward : outward;
        double angleDeg = Math.Atan2(read.Y, read.X) * 180 / Math.PI;
        var up = new Vector(read.Y, -read.X); // sisi "atas" teks

        return (rects, baseline) =>
        {
            var valid = rects.Where(r => !r.IsEmpty).ToList();
            var mats = new Matrix[rects.Count];
            if (valid.Count == 0) { setOrigin(vpc); return (mats, new Size(1, 1)); }

            double x0 = valid.Min(r => r.Left), X = valid.Max(r => r.Right) - x0;
            // panjang teks (setelah perspektif) maksimal D·ln(D/near) supaya tidak keluar dari bagian garis yang terlihat
            double room = D * Math.Log(D / Math.Max(1, nearLimit));
            double f = Math.Clamp(Math.Min(1.25 * D, room) / Math.Max(1, X), 0.3, 1);
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;

            for (int i = 0; i < rects.Count; i++)
            {
                var r = rects[i];
                if (r.IsEmpty) { mats[i] = Matrix.Identity; continue; }
                double cx = r.Left + r.Width / 2;
                double x = (cx - x0) * f;
                double d = towardVp ? D * Math.Exp(-x / D) : D * Math.Exp((x - X * f) / D);
                double s = d / D * f;
                var p = vpc + outward * d + up * (3 * s);

                var m = Matrix.Identity;
                m.Translate(-cx, -baseline);
                m.Scale(s, s);
                m.Rotate(angleDeg);
                m.Translate(p.X, p.Y);
                mats[i] = m;

                foreach (var c in new[] { r.TopLeft, r.TopRight, r.BottomLeft, r.BottomRight })
                {
                    var t = m.Transform(c);
                    minX = Math.Min(minX, t.X); minY = Math.Min(minY, t.Y);
                    maxX = Math.Max(maxX, t.X); maxY = Math.Max(maxY, t.Y);
                }
            }

            for (int i = 0; i < mats.Length; i++)
            {
                if (rects[i].IsEmpty) continue;
                var m = mats[i];
                m.Translate(-minX, -minY);
                mats[i] = m;
            }
            setOrigin(new Point(minX, minY));
            return (mats, new Size(maxX - minX, maxY - minY));
        };
    }

    /// <summary>Bagian sinar p + t·v (t ≥ 0) yang berada di dalam layar [0,w]×[0,h].</summary>
    public static bool InsideSegment(Point p, Vector v, double w, double h, out double tIn, out double tOut)
    {
        tIn = 0; tOut = double.MaxValue;
        if (!Slab(p.X, v.X, w, ref tIn, ref tOut) || !Slab(p.Y, v.Y, h, ref tIn, ref tOut)) return false;
        return tOut > tIn;
    }

    static bool Slab(double p, double v, double max, ref double tIn, ref double tOut)
    {
        if (Math.Abs(v) < 1e-9) return p >= 0 && p <= max;
        double a = (0 - p) / v, b = (max - p) / v;
        if (a > b) (a, b) = (b, a);
        tIn = Math.Max(tIn, a);
        tOut = Math.Min(tOut, b);
        return tOut > tIn;
    }
}
