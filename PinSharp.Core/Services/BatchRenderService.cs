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
        if (inputRows.Count == 0)
        {
            throw new InvalidOperationException("Upload an input file with title|code rows.");
        }

        // When every row has its own image path, pairedCount = all rows.
        // Otherwise pair with the folder image list.
        var allRowsHaveOwnPath = inputRows.All(row => !string.IsNullOrWhiteSpace(row.ImagePath));
        var pairedCount = allRowsHaveOwnPath ? inputRows.Count : Math.Min(imagePaths.Count, inputRows.Count);

        if (pairedCount == 0)
        {
            throw new InvalidOperationException("No source images were selected.");
        }

        var rows = NormalizeCodes(inputRows.Take(pairedCount));

        Directory.CreateDirectory(outputDirectory);
        var logPath = Path.Combine(outputDirectory, "pinsharp-run.log");

        // ── Supervisor pre-pass: validate every image path before rendering ──
        var supervisorBad = new List<string>();
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var imgPath = !string.IsNullOrWhiteSpace(row.ImagePath) ? row.ImagePath : (i < imagePaths.Count ? imagePaths[i] : string.Empty);
            if (string.IsNullOrWhiteSpace(imgPath) || !File.Exists(imgPath))
            {
                supervisorBad.Add($"[SUPERVISOR] Row {i + 1} skipped – image not found: {imgPath}  (code={row.Code})");
            }
        }

        if (supervisorBad.Count > 0)
        {
            await File.AppendAllLinesAsync(logPath, supervisorBad, cancellationToken);
            Console.WriteLine($"[Supervisor] {supervisorBad.Count} rows skipped – image file not found (see log).");
        }

        // Build valid items only
        var items = rows
            .Select((row, index) =>
            {
                var imgPath = !string.IsNullOrWhiteSpace(row.ImagePath) ? row.ImagePath : (index < imagePaths.Count ? imagePaths[index] : string.Empty);
                return (row, imgPath, index);
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.imgPath) && File.Exists(x.imgPath))
            .Select(x => new BatchRenderItem(x.imgPath, x.row.Title, x.row.Code, x.index))
            .ToArray();

        var totalValid = items.Length;
        await File.AppendAllTextAsync(logPath, $"[{DateTimeOffset.Now:O}] Starting {totalValid} pins ({supervisorBad.Count} skipped) with {options.ThreadCount} threads.{Environment.NewLine}", cancellationToken);

        if (totalValid == 0)
        {
            throw new InvalidOperationException($"Supervisor rejected all {pairedCount} rows – no image files were found. Check paths in your input file and see pinsharp-run.log.");
        }

        var results = new RenderedPinResult[totalValid];
        var failures = new ConcurrentBag<string>();
        var completedCount = 0;

        await Parallel.ForEachAsync(items, new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Max(1, options.ThreadCount),
            CancellationToken = cancellationToken
        }, async (item, token) =>
        {
            token.ThrowIfCancellationRequested();
            // Map item.Index → results array position
            var resultIndex = Array.IndexOf(items, item);
            try
            {
                var layout = SelectLayout(item);
                var fontPath = (options.FontFiles is { Count: > 0 } fonts)
                    ? fonts[item.Index % fonts.Count]
                    : options.FontFilePath;
                var itemOptions = fontPath != options.FontFilePath
                    ? options with { FontFilePath = fontPath }
                    : options;
                var fileName = SafeFileName(item.Code) + "." + options.Format.ToLowerInvariant();
                var outputPath = Path.Combine(outputDirectory, fileName);
                await _renderer.RenderToFileAsync(item.ImagePath, item.Title, outputPath, itemOptions, layout, token);
                results[resultIndex] = new RenderedPinResult(item.Title, item.Code, fileName, fileName, layout.Kind);
                var current = Interlocked.Increment(ref completedCount);
                var line = $"{current}/{totalValid} completed {fileName}";
                await File.AppendAllTextAsync(logPath, line + Environment.NewLine, token);
                options.Progress?.Invoke(new BatchProgress(current, totalValid, item.Code, fileName, true));
            }
            catch (Exception ex)
            {
                var error = $"{ex.GetType().Name}: {ex.Message}";
                failures.Add($"{item.Code}: {Path.GetFileName(item.ImagePath)} - {error}");
                var current = Interlocked.Increment(ref completedCount);
                var fileName = SafeFileName(item.Code) + "." + options.Format.ToLowerInvariant();
                var line = $"{current}/{totalValid} failed {fileName} - {error}";
                await File.AppendAllTextAsync(logPath, line + Environment.NewLine, token);
                options.Progress?.Invoke(new BatchProgress(current, totalValid, item.Code, fileName, false, error));
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
            if (File.Exists(zipPath)) File.Delete(zipPath);
            ZipFile.CreateFromDirectory(outputDirectory, zipPath);
            zipName = Path.GetFileName(zipPath);
        }

        await File.AppendAllTextAsync(logPath, $"[{DateTimeOffset.Now:O}] Completed {completed.Length}/{totalValid} pins. Failed: {failures.Count}. Skipped by supervisor: {supervisorBad.Count}.{Environment.NewLine}", cancellationToken);

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
