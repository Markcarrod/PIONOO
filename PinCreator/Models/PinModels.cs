using System.Windows;
using System.Windows.Media;

namespace PinCreator.Models;

public sealed record PinSize(string Name, int Width, int Height)
{
    public override string ToString() => $"{Name} ({Width} x {Height})";
}

public sealed record PinContent(
    string ImagePath,
    string Title,
    string Subtitle,
    string Category,
    string CallToAction,
    string Badge,
    string LinkLabel);

public enum LayoutKind
{
    TopSheetWhite,
    TopSheetBlack,
    TopSheet,
    TopCenter,
    CenterCard,
    LowerCard,
    LeftColumn,
    RightColumn,
    GradientBottom,
    SoftPanel,
    FullVeil,
    BorderFrame,
    SplitEditorial,
    QuoteFocus,
    DiagonalStatement,
    MinimalPoster,
    Cinematic
}

public sealed record LayoutDefinition(
    string Id,
    string Name,
    string Description,
    LayoutKind Kind,
    string Accent,
    string Surface,
    string Foreground,
    string Secondary,
    string HeadingFont,
    FontWeight HeadingWeight,
    bool LightText = false);

public sealed record ImageAnalysis(
    double AverageBrightness,
    double TopBrightness,
    double MiddleBrightness,
    double BottomBrightness,
    Color DominantColor)
{
    public bool IsDark => AverageBrightness < 118;
}

public sealed class BatchItem
{
    public required string ImagePath { get; init; }
    public required string Title { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;
    public string Status { get; set; } = "Ready";
    public string DisplayName => System.IO.Path.GetFileName(ImagePath);
}
