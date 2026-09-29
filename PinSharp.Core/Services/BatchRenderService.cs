using System.IO.Compression;
using System.Collections.Concurrent;
using PinSharp.Core.Models;

namespace PinSharp.Core.Services;

public sealed class BatchRenderService
{
    private readonly PinImageRenderer _renderer = new();

    public async Task<BatchRenderSummary> RenderAsync(
        string jobId,
        IReadOnlyList<string> imagePaths,
        IReadOnlyList<BatchInputRow> inputRows,
        BatchRenderOptions options,
        string outputDirectory,
        string zipPath,
        CancellationToken cancellationToken = default)
    {
        if (imagePaths.Count == 0)
        {
            throw new InvalidOperationException("No source images were selected.");
        }

        if (inputRows.Count == 0)
        {
            throw new InvalidOperationException("Upload an input file with title|code rows.");
        }

        var pairedCount = Math.Min(imagePaths.Count, inputRows.Count);
        var rows = NormalizeCodes(inputRows.Take(pairedCount));

        Directory.CreateDirectory(outputDirectory);
        var logPath = Path.Combine(outputDirectory, "pinsharp-run.log");
        await File.AppendAllTextAsync(logPath, $"[{DateTimeOffset.Now:O}] Starting {pairedCount} pins with {options.ThreadCount} threads.{Environment.NewLine}", cancellationToken);

        var items = Enumerable.Range(0, pairedCount)
            .Select(index =>
            {
                var row = rows[index];
                var imagePath = !string.IsNullOrWhiteSpace(row.ImagePath) ? row.ImagePath : imagePaths[index];
                return new BatchRenderItem(imagePath, row.Title, row.Code, index);
            })
            .ToArray();

        var results = new RenderedPinResult[items.Length];
        var failures = new ConcurrentBag<string>();
        var completedCount = 0;
        await Parallel.ForEachAsync(items, new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Max(1, options.ThreadCount),
            CancellationToken = cancellationToken
        }, async (item, token) =>
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var layout = SelectLayout(item);
                var fileName = SafeFileName(item.Code) + "." + options.Format.ToLowerInvariant();
                var outputPath = Path.Combine(outputDirectory, fileName);
                await _renderer.RenderToFileAsync(item.ImagePath, item.Title, outputPath, options, layout, token);
                results[item.Index] = new RenderedPinResult(item.Title, item.Code, fileName, fileName, layout.Kind);
                var current = Interlocked.Increment(ref completedCount);
                var line = $"{current}/{pairedCount} completed {fileName}";
                await File.AppendAllTextAsync(logPath, line + Environment.NewLine, token);
                options.Progress?.Invoke(new BatchProgress(current, pairedCount, item.Code, fileName, true));
            }
            catch (Exception ex)
            {
                var error = $"{ex.GetType().Name}: {ex.Message}";
                failures.Add($"{item.Code}: {Path.GetFileName(item.ImagePath)} - {error}");
                var current = Interlocked.Increment(ref completedCount);
                var fileName = SafeFileName(item.Code) + "." + options.Format.ToLowerInvariant();
                var line = $"{current}/{pairedCount} failed {fileName} - {error}";
                await File.AppendAllTextAsync(logPath, line + Environment.NewLine, token);
                options.Progress?.Invoke(new BatchProgress(current, pairedCount, item.Code, fileName, false, error));
            }
        });

        if (!failures.IsEmpty)
        {
            await File.AppendAllLinesAsync(logPath, failures.OrderBy(line => line, StringComparer.OrdinalIgnoreCase), cancellationToken);
        }

        var completed = results.OfType<RenderedPinResult>().ToArray();
        if (completed.Length == 0)
        {
            var sample = failures.Take(5).ToArray();
            throw new InvalidOperationException("No pins rendered. Check pinsharp-run.log in the output folder. " + string.Join(" | ", sample));
        }

        var zipName = string.Empty;
        if (options.CreateZip)
        {
            if (File.Exists(zipPath))
            {
                File.Delete(zipPath);
            }

            ZipFile.CreateFromDirectory(outputDirectory, zipPath);
            zipName = Path.GetFileName(zipPath);
        }

        await File.AppendAllTextAsync(logPath, $"[{DateTimeOffset.Now:O}] Completed {completed.Length}/{pairedCount} pins. Failed: {failures.Count}.{Environment.NewLine}", cancellationToken);

        return new BatchRenderSummary(
            jobId,
            imagePaths.Count,
            inputRows.Count,
            completed.Length,
            options.ThreadCount,
            zipName,
            completed);
    }

    // Weighted layout sequence: 70% Top Banner, 20% Center Card, 10% Poster.
    // Repeating 10-slot cycle: slots 0-6 → TopBanner, 7-8 → CenterCard, 9 → Poster.
    private static readonly LayoutKind[] WeightedSequence =
    [
        LayoutKind.TopBanner,
        LayoutKind.TopBanner,
        LayoutKind.TopBanner,
        LayoutKind.TopBanner,
        LayoutKind.TopBanner,
        LayoutKind.TopBanner,
        LayoutKind.TopBanner,
        LayoutKind.CenterCard,
        LayoutKind.CenterCard,
        LayoutKind.Poster,
    ];

    private static LayoutDefinition SelectLayout(BatchRenderItem item)
    {
        var kind = WeightedSequence[item.Index % WeightedSequence.Length];
        return LayoutCatalog.All.First(layout => layout.Kind == kind);
    }

    private static IReadOnlyList<BatchInputRow> NormalizeCodes(IEnumerable<BatchInputRow> inputRows)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var normalized = new List<BatchInputRow>();

        foreach (var row in inputRows)
        {
            if (string.IsNullOrWhiteSpace(row.Title) || string.IsNullOrWhiteSpace(row.Code))
            {
                continue;
            }

            var baseCode = row.Code.Trim();
            counts.TryGetValue(baseCode, out var current);
            current++;
            counts[baseCode] = current;

            var code = current == 1 ? baseCode : $"{baseCode}_{current:00}";
            normalized.Add(row with { Code = code });
        }

        return normalized;
    }

    private static string SafeFileName(string value)
    {
        var cleaned = string.Join("-", value.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries))
            .Trim()
            .Replace(' ', '-');
        return string.IsNullOrWhiteSpace(cleaned) ? "pin" : cleaned[..Math.Min(80, cleaned.Length)];
    }
}
