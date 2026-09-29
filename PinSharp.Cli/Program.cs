using System.Text.RegularExpressions;
using PinSharp.Core.Models;
using PinSharp.Core.Services;

var cli = CliOptions.Parse(args);
if (cli.ShowHelp)
{
    PrintHelp();
    return 0;
}

try
{
    var imageFolder = NormalizePath(cli.ImageFolder);
    var outputRoot = NormalizePath(cli.OutputFolder);
    var inputFile = NormalizePath(cli.InputFile);
    var fontFile = ResolveFontFile(cli.FontFolder, cli.FontName);

    if (!Directory.Exists(imageFolder))
    {
        throw new DirectoryNotFoundException($"Image folder not found: {imageFolder}");
    }

    if (!File.Exists(inputFile))
    {
        throw new FileNotFoundException($"Input file not found: {inputFile}");
    }

    Directory.CreateDirectory(outputRoot);
    var rows = TextBankParser.Parse(await File.ReadAllTextAsync(inputFile));
    if (rows.Count == 0)
    {
        throw new InvalidOperationException("Input file has no valid rows. Expected format: product|imagetitle|code|filepath");
    }

    var images = SelectImages(imageFolder, rows.Count);
    var jobId = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8];
    var outputFolder = Path.Combine(outputRoot, $"pinsharp-{jobId}");
    var zipPath = Path.Combine(outputRoot, $"pinsharp-{jobId}.zip");
    var progressLock = new object();
    var options = new BatchRenderOptions(
        PinSize.FromId(cli.SizeId),
        cli.Format.Equals("jpg", StringComparison.OrdinalIgnoreCase) ? "jpg" : "png",
        Math.Clamp(cli.JpegQuality, 1, 100),
        Math.Max(1, cli.ThreadCount),
        fontFile,
        cli.CreateZip,
        progress =>
        {
            lock (progressLock)
            {
                Console.WriteLine(progress.Success
                    ? $"{progress.Completed}/{progress.Total} completed {progress.FileName}"
                    : $"{progress.Completed}/{progress.Total} failed {progress.FileName} - {progress.ErrorMessage}");
            }
        });

    Console.WriteLine($"Input rows: {rows.Count}");
    Console.WriteLine($"Image folder: {imageFolder}");
    Console.WriteLine($"Output folder: {outputFolder}");
    Console.WriteLine($"Font: {fontFile ?? "auto"}");
    Console.WriteLine($"Threads: {options.ThreadCount}");
    Console.WriteLine(cli.CreateZip ? "ZIP: on" : "ZIP: off");
    Console.WriteLine("Rendering...");

    var summary = await new BatchRenderService().RenderAsync(
        jobId,
        images,
        rows,
        options,
        outputFolder,
        zipPath);

    Console.WriteLine($"Done: {summary.RenderedCount}/{summary.InputRows} pins rendered.");
    Console.WriteLine($"Images: {outputFolder}");
    Console.WriteLine($"Log: {Path.Combine(outputFolder, "pinsharp-run.log")}");
    if (cli.CreateZip)
    {
        Console.WriteLine($"ZIP: {zipPath}");
    }

    return summary.RenderedCount == summary.InputRows ? 0 : 2;
}
catch (Exception ex)
{
    Console.Error.WriteLine("PinSharp failed:");
    Console.Error.WriteLine(ex.Message);
    return 1;
}

static IReadOnlyList<string> SelectImages(string folder, int requiredCount)
{
    var supported = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".bmp", ".webp" };
    var selected = new List<string>(requiredCount);
    foreach (var path in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
    {
        if (!supported.Contains(Path.GetExtension(path)))
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

static string? ResolveFontFile(string fontFolder, string fontName)
{
    var candidates = new List<string>();
    var normalizedFolder = NormalizePath(fontFolder);
    if (File.Exists(normalizedFolder) && IsFontFile(normalizedFolder))
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

    candidates.AddRange(DefaultFontCandidates().Where(File.Exists));
    if (candidates.Count == 0)
    {
        return null;
    }

    var requested = string.IsNullOrWhiteSpace(fontName) ? "random" : fontName.Trim();
    if (requested.Equals("random", StringComparison.OrdinalIgnoreCase))
    {
        return candidates[Random.Shared.Next(candidates.Count)];
    }

    var direct = NormalizePath(requested);
    if (File.Exists(direct) && IsFontFile(direct))
    {
        return direct;
    }

    var normalizedName = NormalizeSearchText(requested);
    return candidates.FirstOrDefault(path =>
        NormalizeSearchText(Path.GetFileNameWithoutExtension(path)).Contains(normalizedName, StringComparison.OrdinalIgnoreCase) ||
        NormalizeSearchText(Path.GetDirectoryName(path) ?? string.Empty).Contains(normalizedName, StringComparison.OrdinalIgnoreCase));
}

static bool IsFontFile(string path)
{
    var extension = Path.GetExtension(path);
    return extension.Equals(".ttf", StringComparison.OrdinalIgnoreCase) ||
           extension.Equals(".otf", StringComparison.OrdinalIgnoreCase) ||
           extension.Equals(".ttc", StringComparison.OrdinalIgnoreCase);
}

static IEnumerable<string> DefaultFontCandidates()
{
    yield return "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf";
    yield return "/usr/share/fonts/truetype/liberation2/LiberationSans-Bold.ttf";
    yield return "/usr/share/fonts/truetype/freefont/FreeSansBold.ttf";
    yield return @"C:\Windows\Fonts\arialbd.ttf";
    yield return @"C:\Windows\Fonts\segoeuib.ttf";
}

static string NormalizePath(string value)
{
    var path = value.Trim();
    if (path.Length == 0 || !OperatingSystem.IsWindows() || File.Exists(path) || Directory.Exists(path))
    {
        return Path.GetFullPath(path);
    }

    var homeMatch = Regex.Match(path, @"^/home/([^/]+)/(.+)$", RegexOptions.IgnoreCase);
    if (homeMatch.Success)
    {
        var windowsPath = Path.Combine(@"C:\Users", homeMatch.Groups[1].Value, homeMatch.Groups[2].Value.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(windowsPath) || Directory.Exists(windowsPath))
        {
            return Path.GetFullPath(windowsPath);
        }
    }

    var driveMatch = Regex.Match(path, @"^/mnt/([a-z])/(.+)$", RegexOptions.IgnoreCase);
    if (driveMatch.Success)
    {
        var windowsPath = $"{driveMatch.Groups[1].Value.ToUpperInvariant()}:\\{driveMatch.Groups[2].Value.Replace('/', Path.DirectorySeparatorChar)}";
        if (File.Exists(windowsPath) || Directory.Exists(windowsPath))
        {
            return Path.GetFullPath(windowsPath);
        }
    }

    return Path.GetFullPath(path);
}

static string NormalizeSearchText(string value) =>
    Regex.Replace(value, "[^a-z0-9]+", string.Empty, RegexOptions.IgnoreCase).ToLowerInvariant();

static void PrintHelp()
{
    Console.WriteLine("""
PinSharp CLI

Default run:
  dotnet run --project PinSharp.Cli -c Release

Options:
  --input PATH       input.txt path
  --images PATH      source image folder
  --output PATH      output folder
  --fonts PATH       font folder or exact font file
  --font NAME        random, partial font name, or exact font path
  --threads N        worker count
  --format png|jpg   output format (default: jpg)
  --quality N        JPG quality, 1-100 (default: 85)
  --size ID          pinterest-standard, pinterest-tall, portrait-social, square
  --zip              create ZIP after render
  --help             show this help
""");
}

internal sealed record CliOptions(
    string InputFile,
    string ImageFolder,
    string OutputFolder,
    string FontFolder,
    string FontName,
    int ThreadCount,
    string Format,
    int JpegQuality,
    string SizeId,
    bool CreateZip,
    bool ShowHelp)
{
    public static CliOptions Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            var key = arg[2..];
            if (key.Equals("zip", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("help", StringComparison.OrdinalIgnoreCase))
            {
                flags.Add(key);
                continue;
            }

            if (i + 1 >= args.Length)
            {
                throw new ArgumentException($"Missing value for --{key}");
            }

            values[key] = args[++i];
        }

        return new CliOptions(
            values.GetValueOrDefault("input", "/home/kayan/Downloads/Allcookwaress.txt"),
            values.GetValueOrDefault("images", "/home/kayan/Downloads/Universal/"),
            values.GetValueOrDefault("output", "/home/kayan/Desktop/PINOUTPUTS/"),
            values.GetValueOrDefault("fonts", "/home/kayan/Downloads/font/Fonts/"),
            values.GetValueOrDefault("font", "random"),
            int.TryParse(values.GetValueOrDefault("threads"), out var threads) ? threads : 20,
            values.GetValueOrDefault("format", "jpg"),
            int.TryParse(values.GetValueOrDefault("quality"), out var quality) ? quality : 85,
            values.GetValueOrDefault("size", "pinterest-standard"),
            flags.Contains("zip"),
            flags.Contains("help"));
    }
}
