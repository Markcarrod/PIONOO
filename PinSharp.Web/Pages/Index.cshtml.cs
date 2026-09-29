using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PinSharp.Core.Models;
using PinSharp.Core.Services;
using System.Text.RegularExpressions;

namespace PinSharp.Web.Pages;

public class IndexModel : PageModel
{
    private static readonly HashSet<string> SupportedImageExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".bmp", ".webp" };

    private readonly BatchRenderService _batchRenderService;

    public IndexModel(BatchRenderService batchRenderService)
    {
        _batchRenderService = batchRenderService;
    }

    [BindProperty]
    public string ImageFolder { get; set; } = "/home/kayan/Downloads/Universal/";

    [BindProperty]
    public string OutputFolder { get; set; } = "/home/kayan/Downloads/Universal2";

    [BindProperty]
    public string FontFolder { get; set; } = "/home/kayan/Downloads/font/Fonts/";

    [BindProperty]
    public string FontName { get; set; } = "random";

    [BindProperty]
    public IFormFile? InputFile { get; set; }

    [BindProperty]
    public string SizeId { get; set; } = PinSize.Presets[0].Id;

    [BindProperty]
    public string Format { get; set; } = "jpg";

    [BindProperty]
    public int JpegQuality { get; set; } = 85;

    [BindProperty]
    public int ThreadCount { get; set; } = 8;

    [BindProperty]
    public bool CreateZip { get; set; }

    public BatchRenderSummary? Summary { get; private set; }

    public string? ErrorMessage { get; private set; }

    public string? CompletedOutputFolder { get; private set; }

    public string? CompletedZipPath { get; private set; }

    public IReadOnlyList<PinSize> Sizes => PinSize.Presets;

    public IReadOnlyList<int> ThreadOptions => Enumerable.Range(1, Math.Max(1, Environment.ProcessorCount)).ToArray();

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(ImageFolder) || string.IsNullOrWhiteSpace(OutputFolder) ||
            InputFile is null || InputFile.Length == 0)
        {
            ErrorMessage = "Enter the image and output folder paths, then select an input.txt file.";
            return Page();
        }

        try
        {
            var imageFolder = NormalizeExternalPath(ImageFolder);
            var outputRoot = NormalizeExternalPath(OutputFolder);
            if (!Directory.Exists(imageFolder))
            {
                throw new DirectoryNotFoundException($"Image folder not found: {imageFolder}");
            }

            if (imageFolder.Equals(outputRoot, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The image and output folders must be different.");
            }

            string inputContent;
            await using (var stream = InputFile.OpenReadStream())
            using (var reader = new StreamReader(stream))
            {
                inputContent = await reader.ReadToEndAsync(cancellationToken);
            }

            var rows = TextBankParser.Parse(inputContent);
            if (rows.Count == 0)
            {
                throw new InvalidOperationException("The input file has no valid title|code rows.");
            }

            var imagePaths = SelectImages(imageFolder, rows.Count);
            var fontFilePath = ResolveFontFile(FontFolder, FontName);
            var format = Format.Equals("jpg", StringComparison.OrdinalIgnoreCase) ? "jpg" : "png";
            var size = PinSize.FromId(SizeId);
            var quality = Math.Clamp(JpegQuality, 1, 100);
            var threads = Math.Clamp(ThreadCount, 1, Math.Max(1, Environment.ProcessorCount));
            var jobId = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8];
            var outputFolder = Path.Combine(outputRoot, $"pinsharp-{jobId}");
            var zipPath = Path.Combine(outputRoot, $"pinsharp-{jobId}.zip");
            Directory.CreateDirectory(outputRoot);

            Summary = await _batchRenderService.RenderAsync(
                jobId,
                imagePaths,
                rows,
                new BatchRenderOptions(size, format, quality, threads, fontFilePath, CreateZip),
                outputFolder,
                zipPath,
                cancellationToken);
            CompletedOutputFolder = outputFolder;
            CompletedZipPath = CreateZip ? zipPath : null;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }

        return Page();
    }

    private static IReadOnlyList<string> SelectImages(string folder, int requiredCount)
    {
        var selected = new List<string>(requiredCount);
        foreach (var path in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
        {
            if (!SupportedImageExtensions.Contains(Path.GetExtension(path)))
            {
                continue;
            }

            selected.Add(path);
            if (selected.Count == requiredCount)
            {
                break;
            }
        }

        if (selected.Count == 0)
        {
            throw new InvalidOperationException("No supported images were found in the image folder.");
        }

        var availableCount = selected.Count;
        while (selected.Count < requiredCount)
        {
            selected.Add(selected[selected.Count % availableCount]);
        }

        return selected;
    }

    private static string? ResolveFontFile(string fontFolder, string fontName)
    {
        if (string.IsNullOrWhiteSpace(fontFolder) && string.IsNullOrWhiteSpace(fontName))
        {
            return null;
        }

        var candidates = new List<string>();
        var normalizedFolder = NormalizeExternalPath(fontFolder);
        if (System.IO.File.Exists(normalizedFolder) && IsFontFile(normalizedFolder))
        {
            return normalizedFolder;
        }

        if (Directory.Exists(normalizedFolder))
        {
            candidates.AddRange(Directory
                .EnumerateFiles(normalizedFolder, "*.*", SearchOption.AllDirectories)
                .Where(IsFontFile)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase));
        }

        candidates.AddRange(DefaultFontCandidates().Where(System.IO.File.Exists));
        if (candidates.Count == 0)
        {
            if (string.IsNullOrWhiteSpace(fontFolder))
            {
                return null;
            }

            throw new InvalidOperationException($"No .ttf, .otf, or .ttc fonts were found under: {normalizedFolder}");
        }

        var requested = string.IsNullOrWhiteSpace(fontName) ? "random" : fontName.Trim();
        if (requested.Equals("random", StringComparison.OrdinalIgnoreCase))
        {
            return candidates[Random.Shared.Next(candidates.Count)];
        }

        var direct = NormalizeExternalPath(requested);
        if (System.IO.File.Exists(direct) && IsFontFile(direct))
        {
            return direct;
        }

        var normalizedName = NormalizeSearchText(requested);
        var match = candidates.FirstOrDefault(path =>
            NormalizeSearchText(Path.GetFileNameWithoutExtension(path)).Contains(normalizedName, StringComparison.OrdinalIgnoreCase) ||
            NormalizeSearchText(Path.GetDirectoryName(path) ?? string.Empty).Contains(normalizedName, StringComparison.OrdinalIgnoreCase));

        if (match is null)
        {
            throw new InvalidOperationException($"No font matching '{requested}' was found under: {normalizedFolder}");
        }

        return match;
    }

    private static bool IsFontFile(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".ttf", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".otf", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".ttc", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> DefaultFontCandidates()
    {
        yield return "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf";
        yield return "/usr/share/fonts/truetype/liberation2/LiberationSans-Bold.ttf";
        yield return "/usr/share/fonts/truetype/freefont/FreeSansBold.ttf";
        yield return @"C:\Windows\Fonts\arialbd.ttf";
        yield return @"C:\Windows\Fonts\segoeuib.ttf";
    }

    private static string NormalizeSearchText(string value) =>
        Regex.Replace(value, "[^a-z0-9]+", string.Empty, RegexOptions.IgnoreCase).ToLowerInvariant();

    private static string NormalizeExternalPath(string value)
    {
        var path = value.Trim();
        if (path.Length == 0 || !OperatingSystem.IsWindows() || System.IO.File.Exists(path) || Directory.Exists(path))
        {
            return Path.GetFullPath(path);
        }

        var homeMatch = Regex.Match(path, @"^/home/([^/]+)/(.+)$", RegexOptions.IgnoreCase);
        if (homeMatch.Success)
        {
            var windowsPath = Path.Combine(@"C:\Users", homeMatch.Groups[1].Value, homeMatch.Groups[2].Value.Replace('/', Path.DirectorySeparatorChar));
            if (System.IO.File.Exists(windowsPath) || Directory.Exists(windowsPath))
            {
                return Path.GetFullPath(windowsPath);
            }
        }

        var driveMatch = Regex.Match(path, @"^/mnt/([a-z])/(.+)$", RegexOptions.IgnoreCase);
        if (driveMatch.Success)
        {
            var windowsPath = $"{driveMatch.Groups[1].Value.ToUpperInvariant()}:\\{driveMatch.Groups[2].Value.Replace('/', Path.DirectorySeparatorChar)}";
            if (System.IO.File.Exists(windowsPath) || Directory.Exists(windowsPath))
            {
                return Path.GetFullPath(windowsPath);
            }
        }

        return Path.GetFullPath(path);
    }
}
