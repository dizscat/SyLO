using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace SyLO;

/// <summary>Teks dengan outline (stroke) supaya tetap terbaca di atas background apa pun.</summary>
public sealed class OutlinedText : FrameworkElement
{
    const FrameworkPropertyMetadataOptions Affects =
        FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender;

    static DependencyProperty Reg<T>(string name, T def) =>
        DependencyProperty.Register(name, typeof(T), typeof(OutlinedText), new FrameworkPropertyMetadata(def, Affects));

    public static readonly DependencyProperty TextProperty = Reg(nameof(Text), "");
    public static readonly DependencyProperty TextFontFamilyProperty = Reg(nameof(TextFontFamily), new FontFamily("Segoe UI"));
    public static readonly DependencyProperty TextFontSizeProperty = Reg(nameof(TextFontSize), 30.0);
    public static readonly DependencyProperty TextFontWeightProperty = Reg(nameof(TextFontWeight), FontWeights.Bold);
    public static readonly DependencyProperty FillProperty = Reg<Brush>(nameof(Fill), Brushes.White);
    public static readonly DependencyProperty StrokeProperty = Reg<Brush>(nameof(Stroke), Brushes.Black);
    public static readonly DependencyProperty StrokeThicknessProperty = Reg(nameof(StrokeThickness), 2.0);
    public static readonly DependencyProperty AlignmentProperty = Reg(nameof(Alignment), TextAlignment.Center);

    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public FontFamily TextFontFamily { get => (FontFamily)GetValue(TextFontFamilyProperty); set => SetValue(TextFontFamilyProperty, value); }
    public double TextFontSize { get => (double)GetValue(TextFontSizeProperty); set => SetValue(TextFontSizeProperty, value); }
    public FontWeight TextFontWeight { get => (FontWeight)GetValue(TextFontWeightProperty); set => SetValue(TextFontWeightProperty, value); }
    public Brush Fill { get => (Brush)GetValue(FillProperty); set => SetValue(FillProperty, value); }
    public Brush Stroke { get => (Brush)GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }
    public double StrokeThickness { get => (double)GetValue(StrokeThicknessProperty); set => SetValue(StrokeThicknessProperty, value); }
    public TextAlignment Alignment { get => (TextAlignment)GetValue(AlignmentProperty); set => SetValue(AlignmentProperty, value); }

    FormattedText? _ft;
    Geometry? _geo;

    protected override Size MeasureOverride(Size available)
    {
        double pad = StrokeThickness;
        double maxW = double.IsInfinity(available.Width) ? 0 : Math.Max(1, available.Width - pad * 2);

        _ft = new FormattedText(string.IsNullOrEmpty(Text) ? " " : Text, CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface(TextFontFamily, FontStyles.Normal, TextFontWeight, FontStretches.Normal),
            Math.Max(1, TextFontSize), Fill, VisualTreeHelper.GetDpi(this).PixelsPerDip)
        {
            TextAlignment = Alignment,
            Trimming = TextTrimming.None
        };
        if (maxW > 0) _ft.MaxTextWidth = maxW;
        _geo = null;

        double width = maxW > 0 ? available.Width : _ft.WidthIncludingTrailingWhitespace + pad * 2;
        return new Size(width, _ft.Height + pad * 2);
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (_ft == null) return;
        double pad = StrokeThickness;
        _geo ??= _ft.BuildGeometry(new Point(pad, pad));

        if (pad > 0 && Stroke != null)
        {
            // stroke 2x lebar: separuh bagian dalam tertutup fill, jadi outline terlihat setebal 'pad'
            var pen = new Pen(Stroke, pad * 2) { LineJoin = PenLineJoin.Round };
            pen.Freeze();
            dc.DrawGeometry(null, pen, _geo);
        }
        dc.DrawGeometry(Fill, null, _geo);
    }
}
