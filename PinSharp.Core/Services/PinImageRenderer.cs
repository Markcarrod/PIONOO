using SkiaSharp;
using PinSharp.Core.Models;

namespace PinSharp.Core.Services;

public sealed class PinImageRenderer
{
    private const float HeadlineFontScale = 0.50f;

    public async Task RenderToFileAsync(
        string imagePath,
        string title,
        string outputPath,
        BatchRenderOptions options,
        LayoutDefinition layout,
        CancellationToken cancellationToken = default)
    {
        using var bitmap = Render(imagePath, title, options, layout);
        using var image = SKImage.FromBitmap(bitmap);
        var format = options.Format.Equals("jpg", StringComparison.OrdinalIgnoreCase) ? SKEncodedImageFormat.Jpeg : SKEncodedImageFormat.Png;
        var quality = format == SKEncodedImageFormat.Jpeg ? options.JpegQuality : 100;
        using var data = image.Encode(format, quality);
        await using var stream = File.Create(outputPath);
        data.SaveTo(stream);
        await stream.FlushAsync(cancellationToken);
    }

    public SKBitmap Render(string imagePath, string title, BatchRenderOptions options, LayoutDefinition layout)
    {
        var info = new SKImageInfo(options.Size.Width, options.Size.Height);
        var bitmap = new SKBitmap(info);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(ParseColor("#0F151B"));

        using var source = SKBitmap.Decode(imagePath);
        if (source is null)
        {
            throw new InvalidOperationException($"Unsupported or unreadable image: {imagePath}");
        }

        DrawCover(canvas, source, info.Rect);
        DrawLayout(canvas, title, layout, options);
        return bitmap;
    }

    private static void DrawLayout(SKCanvas canvas, string title, LayoutDefinition layout, BatchRenderOptions options)
    {
        var size = options.Size;
        switch (layout.Kind)
        {
            case LayoutKind.RightRail:
                DrawRect(canvas, SKRect.Create(size.Width * .53f, 0, size.Width * .47f, size.Height), layout.OverlayHex, layout.OverlayOpacity);
                DrawHeadline(canvas, title, layout, options.FontFilePath, SKRect.Create(size.Width * .58f, size.Height * .12f, size.Width * .33f, size.Height * .74f), SKTextAlign.Right);
                break;
            case LayoutKind.CenterCard:
                DrawRoundRect(canvas, SKRect.Create(size.Width * .08f, size.Height * .26f, size.Width * .84f, size.Height * .46f), layout.OverlayHex, layout.OverlayOpacity, 36f);
                DrawHeadline(canvas, title, layout, options.FontFilePath, SKRect.Create(size.Width * .14f, size.Height * .31f, size.Width * .72f, size.Height * .36f), SKTextAlign.Center);
                break;
            case LayoutKind.LowerSlate:
                DrawRect(canvas, SKRect.Create(0, size.Height * .58f, size.Width, size.Height * .42f), layout.OverlayHex, layout.OverlayOpacity);
                DrawRect(canvas, SKRect.Create(0, size.Height * .58f, size.Width, 12), layout.AccentHex, 1f);
                DrawHeadline(canvas, title, layout, options.FontFilePath, SKRect.Create(size.Width * .08f, size.Height * .64f, size.Width * .84f, size.Height * .28f), SKTextAlign.Left);
                break;
            case LayoutKind.LeftPanel:
                DrawRect(canvas, SKRect.Create(0, 0, size.Width * .66f, size.Height), layout.OverlayHex, layout.OverlayOpacity);
                DrawRect(canvas, SKRect.Create(size.Width * .07f, size.Height * .08f, size.Width * .18f, 10), layout.AccentHex, 1f);
                DrawHeadline(canvas, title, layout, options.FontFilePath, SKRect.Create(size.Width * .07f, size.Height * .13f, size.Width * .5f, size.Height * .72f), SKTextAlign.Left);
                break;
            case LayoutKind.Poster:
                DrawRect(canvas, SKRect.Create(0, 0, size.Width, size.Height), layout.OverlayHex, layout.OverlayOpacity);
                DrawHeadline(canvas, title, layout, options.FontFilePath, SKRect.Create(size.Width * .1f, size.Height * .2f, size.Width * .8f, size.Height * .56f), SKTextAlign.Center);
                break;
            case LayoutKind.Frame:
                DrawRect(canvas, SKRect.Create(0, 0, size.Width, size.Height), layout.OverlayHex, layout.OverlayOpacity);
                DrawFrame(canvas, size, layout.AccentHex);
                DrawHeadline(canvas, title, layout, options.FontFilePath, SKRect.Create(size.Width * .12f, size.Height * .22f, size.Width * .76f, size.Height * .54f), SKTextAlign.Center);
                break;
            case LayoutKind.TopBanner:
                DrawRoundRect(canvas, SKRect.Create(size.Width * .06f, size.Height * .05f, size.Width * .88f, size.Height * .26f), layout.OverlayHex, layout.OverlayOpacity, 26f);
                DrawHeadline(canvas, title, layout, options.FontFilePath, SKRect.Create(size.Width * .1f, size.Height * .09f, size.Width * .8f, size.Height * .2f), SKTextAlign.Center);
                break;
            case LayoutKind.SideSplit:
                DrawRect(canvas, SKRect.Create(0, 0, size.Width * .53f, size.Height), layout.OverlayHex, layout.OverlayOpacity);
                DrawRect(canvas, SKRect.Create(size.Width * .49f, size.Height * .09f, size.Width * .12f, 12), layout.AccentHex, 1f);
                DrawHeadline(canvas, title, layout, options.FontFilePath, SKRect.Create(size.Width * .06f, size.Height * .11f, size.Width * .4f, size.Height * .76f), SKTextAlign.Left);
                break;
        }
    }

    private static void DrawHeadline(
        SKCanvas canvas,
        string title,
        LayoutDefinition layout,
        string? fontFilePath,
        SKRect box,
        SKTextAlign align)
    {
        var palette = ResolvePalette(ParseColor(layout.OverlayHex), layout.OverlayOpacity);
        var wordCount = title.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        var (minSize, maxSize) = wordCount switch
        {
            <= 8 => (76f, 220f),
            <= 12 => (56f, 170f),
            _ => (42f, 140f)
        };
        minSize *= HeadlineFontScale;
        maxSize *= HeadlineFontScale;

        var typeface = FontResolver.Resolve(layout.HeadingFont, layout.Bold, fontFilePath);
        var (paint, font, lines) = FitText(title, box, palette.Foreground, typeface, minSize, maxSize);
        using (paint)
        using (font)
        {
            var lineHeight = font.Size * 1.05f;
            var totalHeight = lines.Count * lineHeight;
            var startY = box.Top + Math.Max(0, (box.Height - totalHeight) / 2f) + lineHeight;
            foreach (var (line, index) in lines.Select((line, index) => (line, index)))
            {
                var y = startY + index * lineHeight;
                var x = align switch
                {
                    SKTextAlign.Center => box.MidX,
                    SKTextAlign.Right => box.Right,
                    _ => box.Left
                };
                paint.Style = SKPaintStyle.Stroke;
                paint.Color = palette.Outline;
                paint.StrokeWidth = Math.Max(2f, font.Size * .045f);
                canvas.DrawText(line, x, y, align, font, paint);
                paint.Style = SKPaintStyle.Fill;
                paint.Color = palette.Foreground;
                canvas.DrawText(line, x, y, align, font, paint);
            }
        }
    }

    private static (SKPaint Paint, SKFont Font, List<string> Lines) FitText(
        string title,
        SKRect box,
        SKColor foreground,
        SKTypeface typeface,
        float minSize,
        float maxSize)
    {
        for (var size = maxSize; size >= minSize; size -= 2f)
        {
            var paint = CreateTextPaint(typeface, size, foreground);
            var font = CreateFont(typeface, size);
            var lines = WrapText(title, paint, font, box.Width);
            var totalHeight = lines.Count * size * 1.05f;
            if (totalHeight <= box.Height && lines.Count <= 6)
            {
                return (paint, font, lines);
            }
            font.Dispose();
            paint.Dispose();
        }

        var fallbackPaint = CreateTextPaint(typeface, minSize, foreground);
        var fallbackFont = CreateFont(typeface, minSize);
        return (fallbackPaint, fallbackFont, WrapText(title, fallbackPaint, fallbackFont, box.Width));
    }

    private static SKPaint CreateTextPaint(SKTypeface typeface, float size, SKColor color) =>
        new()
        {
            IsAntialias = true,
            Color = color,
            Style = SKPaintStyle.Fill,
            StrokeJoin = SKStrokeJoin.Round
        };

    private static SKFont CreateFont(SKTypeface typeface, float size) =>
        new(typeface, size)
        {
            Edging = SKFontEdging.Antialias,
            Subpixel = true,
            LinearMetrics = true
        };

    private static List<string> WrapText(string text, SKPaint paint, SKFont font, float maxWidth)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var lines = new List<string>();
        var current = string.Empty;

        foreach (var word in words)
        {
            var candidate = string.IsNullOrEmpty(current) ? word : $"{current} {word}";
            if (font.MeasureText(candidate, paint) <= maxWidth || string.IsNullOrEmpty(current))
            {
                current = candidate;
                continue;
            }

            lines.Add(current);
            current = word;
        }

        if (!string.IsNullOrWhiteSpace(current))
        {
            lines.Add(current);
        }

        return lines;
    }

    private static void DrawCover(SKCanvas canvas, SKBitmap source, SKRect target)
    {
        var scale = Math.Max(target.Width / source.Width, target.Height / source.Height);
        var width = source.Width * scale;
        var height = source.Height * scale;
        var destination = SKRect.Create(target.Left + (target.Width - width) / 2f, target.Top + (target.Height - height) / 2f, width, height);
        canvas.DrawBitmap(source, destination);
    }

    private static void DrawRect(SKCanvas canvas, SKRect rect, string hex, float opacity)
    {
        using var paint = new SKPaint
        {
            Color = ParseColor(hex).WithAlpha((byte)(opacity * 255)),
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        };
        canvas.DrawRect(rect, paint);
    }

    private static void DrawRoundRect(SKCanvas canvas, SKRect rect, string hex, float opacity, float radius)
    {
        using var paint = new SKPaint
        {
            Color = ParseColor(hex).WithAlpha((byte)(opacity * 255)),
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        };
        canvas.DrawRoundRect(new SKRoundRect(rect, radius, radius), paint);
    }

    private static void DrawFrame(SKCanvas canvas, PinSize size, string accentHex)
    {
        using var paint = new SKPaint
        {
            Color = ParseColor(accentHex),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 7,
            IsAntialias = true
        };
        canvas.DrawRect(SKRect.Create(size.Width * .055f, size.Height * .04f, size.Width * .89f, size.Height * .92f), paint);
    }

    private static (SKColor Foreground, SKColor Outline) ResolvePalette(SKColor overlayColor, float opacity)
    {
        var brightness = ((overlayColor.Red * opacity) * .299f) + ((overlayColor.Green * opacity) * .587f) + ((overlayColor.Blue * opacity) * .114f);
        return brightness >= 168
            ? (SKColors.Black, SKColors.White.WithAlpha(115))
            : (SKColors.White, SKColors.Black.WithAlpha(140));
    }

    private static SKColor ParseColor(string hex) => SKColor.Parse(hex);
}
