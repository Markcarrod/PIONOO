namespace PinSharp.Core.Models;

public sealed record PinSize(string Id, string Name, int Width, int Height)
{
    public override string ToString() => $"{Name} ({Width} x {Height})";

    public static IReadOnlyList<PinSize> Presets { get; } =
    [
        new("pinterest-standard", "Pinterest standard", 1000, 1500),
        new("pinterest-tall", "Pinterest tall", 1000, 1600),
        new("portrait-social", "Portrait social", 1080, 1350),
        new("square", "Square", 1080, 1080)
    ];

    public static PinSize FromId(string? id) =>
        Presets.FirstOrDefault(size => size.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) ?? Presets[0];
}

public sealed record BatchInputRow(string Title, string Code, string ImagePath = "");

public sealed record BatchRenderItem(string ImagePath, string Title, string Code, int Index);

public sealed record BatchRenderOptions(
    PinSize Size,
    string Format,
    int JpegQuality,
    int ThreadCount,
    string? FontFilePath = null,
    IReadOnlyList<string>? FontFiles = null,
    bool CreateZip = false,
    Action<BatchProgress>? Progress = null);

public sealed record BatchProgress(int Completed, int Total, string Code, string FileName, bool Success, string? ErrorMessage = null);

public sealed record RenderedPinResult(string Title, string Code, string FileName, string RelativePath, LayoutKind Layout);

public sealed record BatchRenderSummary(
    string JobId,
    int QueuedImages,
    int InputRows,
    int RenderedCount,
    int ThreadCount,
    string ZipRelativePath,
    IReadOnlyList<RenderedPinResult> Outputs);

public enum LayoutKind
{
    RightRail,
    CenterCard,
    LowerSlate,
    LeftPanel,
    Poster,
    Frame,
    TopBanner,
    SideSplit
}

public sealed record LayoutDefinition(
    string Id,
    string Name,
    LayoutKind Kind,
    string AccentHex,
    string OverlayHex,
    float OverlayOpacity,
    string HeadingFont,
    bool Bold);
