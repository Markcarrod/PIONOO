using System.Windows.Media;
using System.Windows.Media.Imaging;
using PinCreator.Models;

namespace PinCreator.Services;

public static class ImageAnalyzer
{
    public static ImageAnalysis Analyze(BitmapSource source)
    {
        const int sampleWidth = 40;
        var sampleHeight = Math.Max(20, (int)Math.Round(sampleWidth * source.PixelHeight / (double)source.PixelWidth));
        var scaled = new TransformedBitmap(source, new ScaleTransform(sampleWidth / (double)source.PixelWidth, sampleHeight / (double)source.PixelHeight));
        var converted = new FormatConvertedBitmap(scaled, PixelFormats.Bgra32, null, 0);
        var stride = sampleWidth * 4;
        var pixels = new byte[stride * sampleHeight];
        converted.CopyPixels(pixels, stride, 0);

        double total = 0, top = 0, middle = 0, bottom = 0;
        long red = 0, green = 0, blue = 0;
        var topCount = 0;
        var middleCount = 0;
        var bottomCount = 0;

        for (var y = 0; y < sampleHeight; y++)
        {
            for (var x = 0; x < sampleWidth; x++)
            {
                var index = y * stride + x * 4;
                var b = pixels[index];
                var g = pixels[index + 1];
                var r = pixels[index + 2];
                var luminance = 0.2126 * r + 0.7152 * g + 0.0722 * b;
                total += luminance;
                red += r;
                green += g;
                blue += b;

                if (y < sampleHeight / 3) { top += luminance; topCount++; }
                else if (y < sampleHeight * 2 / 3) { middle += luminance; middleCount++; }
                else { bottom += luminance; bottomCount++; }
            }
        }

        var count = sampleWidth * sampleHeight;
        return new ImageAnalysis(
            total / count,
            top / topCount,
            middle / middleCount,
            bottom / bottomCount,
            Color.FromRgb((byte)(red / count), (byte)(green / count), (byte)(blue / count)));
    }
}
