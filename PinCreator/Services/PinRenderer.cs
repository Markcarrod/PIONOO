using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PinCreator.Models;

namespace PinCreator.Services;

public sealed class PinRenderer
{
    private const double Dip = 1.0;
    private const double FontScale = 0.50;

    public BitmapSource LoadImage(string path)
    {
        using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var frame = decoder.Frames[0];
        frame.Freeze();
        return frame;
    }

    public RenderTargetBitmap Render(PinContent content, LayoutDefinition layout, PinSize size)
    {
        var image = LoadImage(content.ImagePath);
        var analysis = ImageAnalyzer.Analyze(image);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var canvas = new Rect(0, 0, size.Width, size.Height);
            dc.DrawRectangle(Brush(layout.Surface), null, canvas);
            DrawLayout(dc, canvas, image, content, layout, analysis);
        }

        var bitmap = new RenderTargetBitmap(size.Width, size.Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    public void Save(BitmapSource bitmap, string path, int jpegQuality = 85)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        BitmapEncoder encoder = Path.GetExtension(path).Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
                                Path.GetExtension(path).Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
            ? new JpegBitmapEncoder { QualityLevel = Math.Clamp(jpegQuality, 1, 100) }
            : new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static void DrawLayout(DrawingContext dc, Rect c, BitmapSource image, PinContent text, LayoutDefinition l, ImageAnalysis analysis)
    {
        switch (l.Kind)
        {
            case LayoutKind.TopSheet:
                DrawCover(dc, image, c);
                DrawRect(dc, new Rect(0, 0, c.Width, c.Height * .39), l.Surface, .95);
                DrawTextBlock(dc, text, l, new Rect(c.Width * .075, c.Height * .065, c.Width * .78, c.Height * .28), TextAlignment.Left);
                break;
            case LayoutKind.TopCenter:
                DrawCover(dc, image, c);
                DrawGradient(dc, new Rect(0, 0, c.Width, c.Height * .48), ParseColor(l.Surface), Colors.Transparent, 90);
                DrawTextBlock(dc, text, l, new Rect(c.Width * .09, c.Height * .06, c.Width * .82, c.Height * .31), TextAlignment.Center);
                break;
            case LayoutKind.CenterCard:
                DrawCover(dc, image, c);
                DrawRounded(dc, new Rect(c.Width * .075, c.Height * .27, c.Width * .85, c.Height * .47), l.Surface, 34, .94, true);
                DrawTextBlock(dc, text, l, new Rect(c.Width * .14, c.Height * .325, c.Width * .72, c.Height * .36), TextAlignment.Center);
                break;
            case LayoutKind.LowerCard:
                DrawCover(dc, image, c);
                DrawRect(dc, new Rect(0, c.Height * .58, c.Width, c.Height * .42), l.Surface, .97);
                dc.DrawRectangle(Brush(l.Accent), null, new Rect(0, c.Height * .58, c.Width, 12));
                DrawTextBlock(dc, text, l, new Rect(c.Width * .08, c.Height * .635, c.Width * .84, c.Height * .31), TextAlignment.Left);
                break;
            case LayoutKind.LeftColumn:
                DrawCover(dc, image, c);
                DrawRect(dc, new Rect(0, 0, c.Width * .67, c.Height), l.Surface, .96);
                DrawTextBlock(dc, text, l, new Rect(c.Width * .065, c.Height * .12, c.Width * .54, c.Height * .76), TextAlignment.Left);
                DrawRule(dc, l.Accent, c.Width * .065, c.Height * .09, c.Width * .18);
                break;
            case LayoutKind.RightColumn:
                DrawCover(dc, image, c);
                DrawGradient(dc, new Rect(c.Width * .2, 0, c.Width * .8, c.Height), Colors.Transparent, ParseColor(l.Surface), 0);
                DrawTextBlock(dc, text, l, new Rect(c.Width * .38, c.Height * .12, c.Width * .55, c.Height * .76), TextAlignment.Right);
                break;
            case LayoutKind.GradientBottom:
                DrawCover(dc, image, c);
                DrawGradient(dc, new Rect(0, c.Height * .32, c.Width, c.Height * .68), Colors.Transparent, ParseColor(l.Surface), 90);
                DrawTextBlock(dc, text, l, new Rect(c.Width * .075, c.Height * .59, c.Width * .82, c.Height * .34), TextAlignment.Left);
                break;
            case LayoutKind.SoftPanel:
                DrawCover(dc, image, c);
                var panelColor = analysis.IsDark ? "#F8F3EA" : l.Surface;
                DrawRounded(dc, new Rect(c.Width * .08, c.Height * .16, c.Width * .74, c.Height * .53), panelColor, 42, .9, true);
                DrawTextBlock(dc, text, l with { Surface = panelColor }, new Rect(c.Width * .14, c.Height * .225, c.Width * .62, c.Height * .4), TextAlignment.Left);
                break;
            case LayoutKind.FullVeil:
                DrawCover(dc, image, c);
                DrawRect(dc, c, l.Surface, analysis.IsDark ? .79 : .68);
                DrawTextBlock(dc, text, l, new Rect(c.Width * .11, c.Height * .25, c.Width * .78, c.Height * .5), TextAlignment.Center);
                break;
            case LayoutKind.BorderFrame:
                DrawCover(dc, image, c);
                DrawRect(dc, c, l.Surface, .38);
                dc.DrawRectangle(null, new Pen(Brush(l.Accent), 7), new Rect(c.Width * .055, c.Height * .04, c.Width * .89, c.Height * .92));
                DrawTextBlock(dc, text, l, new Rect(c.Width * .12, c.Height * .23, c.Width * .76, c.Height * .54), TextAlignment.Center);
                break;
            case LayoutKind.SplitEditorial:
                DrawRect(dc, c, l.Surface, 1);
                DrawCover(dc, image, new Rect(c.Width * .6, 0, c.Width * .4, c.Height));
                dc.DrawRectangle(Brush(l.Accent), null, new Rect(c.Width * .54, c.Height * .09, c.Width * .1, 14));
                DrawTextBlock(dc, text, l, new Rect(c.Width * .06, c.Height * .12, c.Width * .48, c.Height * .76), TextAlignment.Left);
                break;
            case LayoutKind.QuoteFocus:
                DrawCover(dc, image, c);
                DrawRounded(dc, new Rect(c.Width * .07, c.Height * .13, c.Width * .86, c.Height * .73), l.Surface, 28, .94, true);
                DrawSimpleText(dc, "\"", "Georgia", FontWeights.Bold, 190, Brush(l.Accent), new Point(c.Width * .12, c.Height * .13), c.Width * .2, TextAlignment.Left);
                DrawTextBlock(dc, text, l, new Rect(c.Width * .15, c.Height * .3, c.Width * .7, c.Height * .48), TextAlignment.Left);
                break;
            case LayoutKind.DiagonalStatement:
                DrawCover(dc, image, c);
                var shape = Polygon(new Point(0, 0), new Point(c.Width * .86, 0), new Point(c.Width * .58, c.Height), new Point(0, c.Height));
                dc.DrawGeometry(Brush(l.Surface), null, shape);
                var accent = Polygon(new Point(c.Width * .58, 0), new Point(c.Width * .63, 0), new Point(c.Width * .35, c.Height), new Point(c.Width * .3, c.Height));
                dc.DrawGeometry(Brush(l.Accent), null, accent);
                DrawTextBlock(dc, text, l, new Rect(c.Width * .065, c.Height * .13, c.Width * .55, c.Height * .74), TextAlignment.Left);
                break;
            case LayoutKind.MinimalPoster:
                DrawRect(dc, c, l.Surface, 1);
                DrawCover(dc, image, new Rect(c.Width * .08, c.Height * .07, c.Width * .84, c.Height * .38));
                DrawRule(dc, l.Accent, c.Width * .08, c.Height * .5, c.Width * .28);
                DrawTextBlock(dc, text, l, new Rect(c.Width * .08, c.Height * .54, c.Width * .84, c.Height * .38), TextAlignment.Left);
                break;
            case LayoutKind.Cinematic:
                DrawRect(dc, c, l.Surface, 1);
                DrawCover(dc, image, new Rect(0, c.Height * .12, c.Width, c.Height * .58));
                DrawGradient(dc, new Rect(0, c.Height * .42, c.Width, c.Height * .3), Colors.Transparent, ParseColor(l.Surface), 90);
                DrawTextBlock(dc, text, l, new Rect(c.Width * .075, c.Height * .64, c.Width * .85, c.Height * .29), TextAlignment.Center);
                break;
        }
    }

    private static void DrawTextBlock(DrawingContext dc, PinContent content, LayoutDefinition layout, Rect bounds, TextAlignment alignment)
    {
        var title = string.IsNullOrWhiteSpace(content.Title) ? "Your pin title" : content.Title.Trim();
        var palette = ResolveTextPalette(layout.Surface);
        var foreground = new SolidColorBrush(palette.Foreground);
        var secondary = new SolidColorBrush(palette.Secondary);
        var y = bounds.Y;

        if (!string.IsNullOrWhiteSpace(content.Category))
        {
            DrawSimpleText(dc, content.Category.ToUpperInvariant(), "Bahnschrift", FontWeights.SemiBold, ScaledFont(Math.Max(18, bounds.Width * .032)), Brush(layout.Accent), new Point(bounds.X, y), bounds.Width, alignment);
            y += bounds.Height * .1;
        }

        var titleOnly = string.IsNullOrWhiteSpace(content.Subtitle)
                        && string.IsNullOrWhiteSpace(content.Category)
                        && string.IsNullOrWhiteSpace(content.CallToAction)
                        && string.IsNullOrWhiteSpace(content.Badge)
                        && string.IsNullOrWhiteSpace(content.LinkLabel);
        var titleHeight = bounds.Height * (titleOnly ? .92 : string.IsNullOrWhiteSpace(content.Subtitle) ? .62 : .48);
        var wordCount = title.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        var (minimumTitleSize, maximumCap, widthScale) = wordCount switch
        {
            <= 8 => (76d, 230d, .38),
            <= 12 => (48d, 170d, .31),
            _ => (38d, 140d, .27)
        };
        var minimumScaledTitleSize = ScaledFont(minimumTitleSize);
        var maximumTitleSize = ScaledFont(Math.Min(maximumCap, bounds.Width * widthScale));
        var (titleText, titleSize) = FitText(title, layout.HeadingFont, FontWeights.ExtraBold, foreground, bounds.Width, titleHeight, alignment, minimumScaledTitleSize, maximumTitleSize);
        DrawOutlinedText(dc, titleText, new Point(bounds.X, y), palette.Outline, Math.Max(2.2, titleSize * .05));
        y += titleText.Height + Math.Max(18, titleSize * .28);

        if (!string.IsNullOrWhiteSpace(content.Subtitle))
        {
            var (subtitle, _) = FitText(content.Subtitle.Trim(), "Bahnschrift", FontWeights.Normal, secondary, bounds.Width, bounds.Height * .22, alignment, ScaledFont(18), ScaledFont(Math.Min(38, titleSize * .42)));
            DrawOutlinedText(dc, subtitle, new Point(bounds.X, y), palette.SecondaryOutline, Math.Max(1.2, titleSize * .02));
            y += subtitle.Height + 24;
        }

        if (!string.IsNullOrWhiteSpace(content.CallToAction))
        {
            DrawPill(dc, content.CallToAction, layout, bounds, y, alignment);
        }

        if (!string.IsNullOrWhiteSpace(content.Badge))
        {
            DrawBadge(dc, content.Badge, layout, new Point(bounds.Right, bounds.Y), alignment);
        }

        if (!string.IsNullOrWhiteSpace(content.LinkLabel))
        {
            DrawSimpleText(dc, content.LinkLabel, "Bahnschrift", FontWeights.SemiBold, ScaledFont(19), secondary, new Point(bounds.X, bounds.Bottom - 26), bounds.Width, alignment);
        }
    }

    private static (FormattedText Text, double Size) FitText(string text, string family, FontWeight weight, Brush brush, double width, double height, TextAlignment alignment, double min, double max)
    {
        for (var size = max; size >= min; size -= 2)
        {
            var formatted = Format(text, family, weight, size, brush, width, alignment);
            if (formatted.Height <= height && formatted.MinWidth <= width) return (formatted, size);
        }
        return (Format(text, family, weight, min, brush, width, alignment), min);
    }

    private static FormattedText Format(string text, string family, FontWeight weight, double size, Brush brush, double width, TextAlignment alignment)
    {
        var formatted = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily(family), FontStyles.Normal, weight, FontStretches.Normal), size, brush, Dip)
        {
            MaxTextWidth = Math.Max(1, width),
            TextAlignment = alignment,
            LineHeight = size * 1.08,
            Trimming = TextTrimming.WordEllipsis
        };
        return formatted;
    }

    private static void DrawPill(DrawingContext dc, string value, LayoutDefinition layout, Rect bounds, double y, TextAlignment alignment)
    {
        var text = Format(value.ToUpperInvariant(), "Bahnschrift", FontWeights.SemiBold, ScaledFont(19), Brushes.White, bounds.Width, TextAlignment.Left);
        var width = Math.Min(bounds.Width, text.Width + 52);
        var x = alignment switch
        {
            TextAlignment.Center => bounds.X + (bounds.Width - width) / 2,
            TextAlignment.Right => bounds.Right - width,
            _ => bounds.X
        };
        var rect = new Rect(x, y, width, 48);
        dc.DrawRoundedRectangle(Brush(layout.Accent), null, rect, 24, 24);
        text.MaxTextWidth = width - 40;
        dc.DrawText(text, new Point(x + 20, y + 12));
    }

    private static void DrawBadge(DrawingContext dc, string value, LayoutDefinition layout, Point anchor, TextAlignment alignment)
    {
        var text = Format(value.ToUpperInvariant(), "Bahnschrift", FontWeights.Bold, ScaledFont(17), Brushes.White, 200, TextAlignment.Center);
        var width = Math.Max(72, text.Width + 32);
        var x = alignment == TextAlignment.Right ? anchor.X - width : anchor.X;
        if (alignment == TextAlignment.Center) x = anchor.X - width / 2;
        dc.DrawRoundedRectangle(Brush(layout.Accent), null, new Rect(x, anchor.Y - 52, width, 38), 8, 8);
        text.MaxTextWidth = width;
        dc.DrawText(text, new Point(x, anchor.Y - 44));
    }

    private static void DrawSimpleText(DrawingContext dc, string text, string family, FontWeight weight, double size, Brush brush, Point point, double width, TextAlignment alignment)
    {
        var formatted = Format(text, family, weight, size, brush, width, alignment);
        var fill = brush as SolidColorBrush;
        var outline = fill is null
            ? System.Windows.Media.Color.FromArgb(120, 0, 0, 0)
            : OppositeOutline(fill.Color, size <= 22 ? (byte)90 : (byte)110);
        DrawOutlinedText(dc, formatted, point, outline, Math.Max(0.9, size * .03));
    }

    private static double ScaledFont(double size) => size * FontScale;

    private static void DrawCover(DrawingContext dc, BitmapSource image, Rect target)
    {
        dc.PushClip(new RectangleGeometry(target));
        var scale = Math.Max(target.Width / image.PixelWidth, target.Height / image.PixelHeight);
        var width = image.PixelWidth * scale;
        var height = image.PixelHeight * scale;
        var destination = new Rect(target.X + (target.Width - width) / 2, target.Y + (target.Height - height) / 2, width, height);
        dc.DrawImage(image, destination);
        dc.Pop();
    }

    private static void DrawRect(DrawingContext dc, Rect rect, string color, double opacity)
    {
        var brush = Brush(color); brush.Opacity = opacity;
        dc.DrawRectangle(brush, null, rect);
    }

    private static void DrawRounded(DrawingContext dc, Rect rect, string color, double radius, double opacity, bool shadow)
    {
        if (shadow) dc.DrawRoundedRectangle(new SolidColorBrush(System.Windows.Media.Color.FromArgb(45, 0, 0, 0)), null, new Rect(rect.X + 10, rect.Y + 16, rect.Width, rect.Height), radius, radius);
        var brush = Brush(color); brush.Opacity = opacity;
        dc.DrawRoundedRectangle(brush, null, rect, radius, radius);
    }

    private static void DrawGradient(DrawingContext dc, Rect rect, Color start, Color end, double angle)
    {
        var brush = new LinearGradientBrush(start, end, angle);
        dc.DrawRectangle(brush, null, rect);
    }

    private static void DrawRule(DrawingContext dc, string color, double x, double y, double width) =>
        dc.DrawRectangle(Brush(color), null, new Rect(x, y, width, 9));

    private static StreamGeometry Polygon(params Point[] points)
    {
        var geometry = new StreamGeometry();
        using var context = geometry.Open();
        context.BeginFigure(points[0], true, true);
        context.PolyLineTo(points.Skip(1).ToArray(), true, true);
        geometry.Freeze();
        return geometry;
    }

    private static void DrawOutlinedText(DrawingContext dc, FormattedText formatted, Point point, Color outline, double thickness)
    {
        var geometry = formatted.BuildGeometry(point);
        dc.DrawGeometry(null, new Pen(new SolidColorBrush(outline), thickness) { LineJoin = PenLineJoin.Round }, geometry);
        dc.DrawText(formatted, point);
    }

    private static (Color Foreground, Color Secondary, Color Outline, Color SecondaryOutline) ResolveTextPalette(string surface)
    {
        var surfaceColor = ParseColor(surface);
        var isLight = PerceivedBrightness(surfaceColor) >= 168;
        var foreground = isLight ? Colors.Black : Colors.White;
        var secondary = isLight
            ? System.Windows.Media.Color.FromRgb(36, 36, 36)
            : System.Windows.Media.Color.FromRgb(244, 238, 230);
        return (
            foreground,
            secondary,
            OppositeOutline(foreground, 118),
            OppositeOutline(secondary, 90));
    }

    private static Color OppositeOutline(Color color, byte alpha)
    {
        var brightness = PerceivedBrightness(color);
        return brightness >= 160
            ? System.Windows.Media.Color.FromArgb(alpha, 0, 0, 0)
            : System.Windows.Media.Color.FromArgb(alpha, 255, 255, 255);
    }

    private static double PerceivedBrightness(Color color) =>
        (color.R * 299d + color.G * 587d + color.B * 114d) / 1000d;

    private static SolidColorBrush Brush(string value) => new(ParseColor(value));
    private static Color ParseColor(string value) => (Color)ColorConverter.ConvertFromString(value)!;
}
