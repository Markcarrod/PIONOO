using System.Text;
using System.IO;
using PinCreator.Models;

namespace PinCreator.Services;

public static class CsvBatchParser
{
    public static IReadOnlyList<BatchItem> Parse(string path)
    {
        var rows = File.ReadAllLines(path);
        if (rows.Length == 0) return [];

        var headers = Split(rows[0]);
        var imageIndex = Find(headers, "imagePath", "image", "path");
        var titleIndex = Find(headers, "title", "headline");
        var subtitleIndex = Find(headers, "subtitle", "description");
        var codeIndex = Find(headers, "code", "outputCode", "slug");
        if (imageIndex < 0 || titleIndex < 0)
            throw new InvalidDataException("CSV needs imagePath and title columns.");

        var baseFolder = Path.GetDirectoryName(Path.GetFullPath(path))!;
        var result = new List<BatchItem>();
        foreach (var line in rows.Skip(1).Where(line => !string.IsNullOrWhiteSpace(line)))
        {
            var values = Split(line);
            if (values.Count <= Math.Max(imageIndex, titleIndex)) continue;
            var imagePath = values[imageIndex];
            if (!Path.IsPathRooted(imagePath)) imagePath = Path.Combine(baseFolder, imagePath);
            result.Add(new BatchItem
            {
                ImagePath = Path.GetFullPath(imagePath),
                Title = values[titleIndex],
                Code = codeIndex >= 0 && codeIndex < values.Count ? values[codeIndex] : string.Empty,
                Subtitle = subtitleIndex >= 0 && subtitleIndex < values.Count ? values[subtitleIndex] : string.Empty
            });
        }
        return result;
    }

    private static int Find(IReadOnlyList<string> headers, params string[] names)
    {
        for (var i = 0; i < headers.Count; i++)
            if (names.Any(name => headers[i].Equals(name, StringComparison.OrdinalIgnoreCase))) return i;
        return -1;
    }

    private static List<string> Split(string line)
    {
        var values = new List<string>();
        var current = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"' && quoted && i + 1 < line.Length && line[i + 1] == '"') { current.Append('"'); i++; }
            else if (c == '"') quoted = !quoted;
            else if (c == ',' && !quoted) { values.Add(current.ToString().Trim()); current.Clear(); }
            else current.Append(c);
        }
        values.Add(current.ToString().Trim());
        return values;
    }
}
