using System;
using System.IO;
using MediaColor = System.Windows.Media.Color;

namespace SyLO;

/// <summary>Mengambil warna dominan yang "hidup" (bukan hitam/abu) dari cover album.</summary>
public static class AlbumColor
{
    const int Bins = 24;

    public static MediaColor? Dominant(Stream image)
    {
        using var src = new System.Drawing.Bitmap(image);
        using var bmp = new System.Drawing.Bitmap(src, 48, 48);
        var px = new System.Collections.Generic.List<System.Drawing.Color>(48 * 48);
        for (int y = 0; y < bmp.Height; y++)
        for (int x = 0; x < bmp.Width; x++) px.Add(bmp.GetPixel(x, y));
        return FromPixels(px, 0.18, 0.2);
    }

    /// <summary>Warna dominan dari kumpulan piksel, dibuat terang & jenuh supaya cocok untuk cahaya.</summary>
    /// <param name="minValue">Piksel lebih gelap dari ini diabaikan.</param>
    /// <param name="minSat">Piksel lebih abu-abu dari ini diabaikan.</param>
    public static MediaColor? FromPixels(System.Collections.Generic.IEnumerable<System.Drawing.Color> pixels,
        double minValue, double minSat)
    {
        var w = new double[Bins];
        var r = new double[Bins];
        var g = new double[Bins];
        var b = new double[Bins];
        double total = 0;

        foreach (var p in pixels)
        {
            ToHsv(p.R, p.G, p.B, out var h, out var s, out var v);
            total++;
            if (v < minValue || s < minSat) continue; // abaikan gelap & abu-abu
            double weight = s * s * v;
            int bin = (int)(h / (360.0 / Bins)) % Bins;
            w[bin] += weight;
            r[bin] += p.R * weight;
            g[bin] += p.G * weight;
            b[bin] += p.B * weight;
        }

        int best = -1;
        double bestScore = 0;
        for (int i = 0; i < Bins; i++)
        {
            double score = w[i] + 0.5 * (w[(i + Bins - 1) % Bins] + w[(i + 1) % Bins]);
            if (score > bestScore) { bestScore = score; best = i; }
        }
        if (best < 0 || w[best] < total * 0.01) return null; // cover hampir tanpa warna

        ToHsv(r[best] / w[best], g[best] / w[best], b[best] / w[best], out var hh, out var ss, out var vv);
        // buat cukup terang & jenuh supaya enak dipakai sebagai cahaya
        return FromHsv(hh, Math.Clamp(ss, 0.45, 0.95), Math.Max(vv, 0.92));
    }

    static void ToHsv(double r, double g, double b, out double h, out double s, out double v)
    {
        r /= 255; g /= 255; b /= 255;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
        v = max;
        s = max <= 0 ? 0 : d / max;
        if (d <= 0) { h = 0; return; }
        if (max == r) h = 60 * (((g - b) / d) % 6);
        else if (max == g) h = 60 * ((b - r) / d + 2);
        else h = 60 * ((r - g) / d + 4);
        if (h < 0) h += 360;
    }

    static MediaColor FromHsv(double h, double s, double v)
    {
        double c = v * s, x = c * (1 - Math.Abs(h / 60 % 2 - 1)), m = v - c;
        (double r, double g, double b) = (h % 360) switch
        {
            < 60 => (c, x, 0d),
            < 120 => (x, c, 0d),
            < 180 => (0d, c, x),
            < 240 => (0d, x, c),
            < 300 => (x, 0d, c),
            _ => (c, 0d, x)
        };
        return MediaColor.FromRgb((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
    }
}
