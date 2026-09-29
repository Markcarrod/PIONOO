using System.Collections.Concurrent;
using SkiaSharp;

namespace PinSharp.Core.Services;

public static class FontResolver
{
    private static readonly ConcurrentDictionary<string, SKTypeface> Cache = new(StringComparer.Ordinal);

    private static readonly string[] PreferredFonts =
    [
        "DejaVu Sans",
        "Arial",
        "Liberation Sans",
        "Noto Sans",
        "Segoe UI"
    ];

    public static SKTypeface Resolve(string preferredFamily, bool bold, string? fontFilePath = null)
    {
        var cacheKey = string.IsNullOrWhiteSpace(fontFilePath)
            ? $"family:{preferredFamily}:{bold}"
            : $"file:{Path.GetFullPath(fontFilePath)}";
        return Cache.GetOrAdd(cacheKey, _ => CreateTypeface(preferredFamily, bold, fontFilePath));
    }

    private static SKTypeface CreateTypeface(string preferredFamily, bool bold, string? fontFilePath)
    {
        if (!string.IsNullOrWhiteSpace(fontFilePath))
        {
            var custom = SKTypeface.FromFile(fontFilePath);
            if (custom is not null)
            {
                return custom;
            }

            throw new InvalidOperationException($"Unable to load font file: {fontFilePath}");
        }

        var weight = bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal;
        var preferred = SKTypeface.FromFamilyName(preferredFamily, weight, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);
        if (preferred is not null)
        {
            return preferred;
        }

        foreach (var family in PreferredFonts)
        {
            var candidate = SKTypeface.FromFamilyName(family, weight, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);
            if (candidate is not null)
            {
                return candidate;
            }
        }

        return SKTypeface.Default;
    }
}
