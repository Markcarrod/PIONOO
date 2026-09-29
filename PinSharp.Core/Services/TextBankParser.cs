using PinSharp.Core.Models;

namespace PinSharp.Core.Services;

public static class TextBankParser
{
    public static IReadOnlyList<BatchInputRow> Parse(string content)
    {
        var rows = new List<BatchInputRow>();
        foreach (var rawLine in content.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (string.IsNullOrWhiteSpace(rawLine) || !rawLine.Contains('|'))
            {
                continue;
            }

            var parts = rawLine.Split('|', StringSplitOptions.None);

            string title, code, imagePath;

            if (parts.Length >= 4)
            {
                // 4-column format: product|imagetitle|code|filepath
                // imagetitle (col 2) is the text rendered on the pin
                title     = parts[1].Trim();
                code      = parts[2].Trim();
                imagePath = parts[3].Trim();
            }
            else
            {
                // Legacy 2-column format: title|code
                title     = parts[0].Trim();
                code      = parts[1].Trim();
                imagePath = string.Empty;
            }

            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(code))
            {
                continue;
            }

            rows.Add(new BatchInputRow(title, code, imagePath));
        }

        return rows;
    }
}
