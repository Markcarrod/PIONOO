using System.IO;
using System.Text.Json;

namespace PinCreator.Services;

public sealed record UserSettings(
    string InputFilePath,
    string OutputFolder,
    int SizeIndex,
    int FormatIndex,
    string Quality,
    int ThreadCount);

public static class UserSettingsStore
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PinCreator",
        "settings.json");

    public static UserSettings? Load()
    {
        try
        {
            return File.Exists(SettingsPath)
                ? JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(SettingsPath))
                : null;
        }
        catch
        {
            return null;
        }
    }

    public static void Save(UserSettings settings)
    {
        var folder = Path.GetDirectoryName(SettingsPath)!;
        Directory.CreateDirectory(folder);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
    }
}
