using PinSharp.Core.Models;

namespace PinSharp.Core.Services;

public static class LayoutCatalog
{
    public static IReadOnlyList<LayoutDefinition> All { get; } =
    [
        new("right-rail", "Right Rail", LayoutKind.RightRail, "#F4B544", "#13202A", .82f, "DejaVu Sans", true),
        new("center-card", "Center Card", LayoutKind.CenterCard, "#FF7A59", "#F8F2E8", .92f, "DejaVu Sans", true),
        new("lower-slate", "Lower Slate", LayoutKind.LowerSlate, "#F4B544", "#10171D", .88f, "DejaVu Sans", true),
        new("left-panel", "Left Panel", LayoutKind.LeftPanel, "#FF7A59", "#F6EFE4", .9f, "DejaVu Sans", true),
        new("poster", "Poster", LayoutKind.Poster, "#F4B544", "#111921", .58f, "DejaVu Sans", true),
        new("frame", "Frame", LayoutKind.Frame, "#F4B544", "#0E151C", .26f, "DejaVu Sans", true),
        new("top-banner", "Top Banner", LayoutKind.TopBanner, "#FF7A59", "#F6EFE4", .9f, "DejaVu Sans", true),
        new("side-split", "Side Split", LayoutKind.SideSplit, "#FF7A59", "#131A20", .78f, "DejaVu Sans", true)
    ];
}
