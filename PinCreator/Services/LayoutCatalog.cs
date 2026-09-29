using System.Windows;
using PinCreator.Models;

namespace PinCreator.Services;

public static class LayoutCatalog
{
    public static LayoutDefinition WhiteSheet { get; } =
        new("top-sheet-white", "Top Sheet (White)", "Top-middle upper third white sheet with black text", LayoutKind.TopSheetWhite, "#111111", "#FFFFFF", "#000000", "#333333", "Bahnschrift", FontWeights.Bold);

    public static LayoutDefinition BlackSheet { get; } =
        new("top-sheet-black", "Top Sheet (Black)", "Top-middle upper third black sheet with white text", LayoutKind.TopSheetBlack, "#FFFFFF", "#000000", "#FFFFFF", "#E0E0E0", "Bahnschrift", FontWeights.Bold, true);

    public static IReadOnlyList<LayoutDefinition> All { get; } =
    [
        WhiteSheet,
        BlackSheet
    ];
}
