using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using PinCreator.Models;
using PinCreator.Services;

namespace PinCreator;

public partial class MainWindow : Window
{
    private sealed record ThreadOption(string Label, int Degree)
    {
        public override string ToString() => Label;
    }

    private static readonly string ImageFilter = "Images|*.jpg;*.jpeg;*.png;*.bmp;*.tif;*.tiff|All files|*.*";
    private static readonly HashSet<LayoutKind> SpaciousLayouts =
    [
        LayoutKind.TopSheet,
        LayoutKind.TopCenter,
        LayoutKind.CenterCard,
        LayoutKind.LowerCard,
        LayoutKind.GradientBottom,
        LayoutKind.SoftPanel,
        LayoutKind.FullVeil,
        LayoutKind.BorderFrame,
        LayoutKind.QuoteFocus,
        LayoutKind.MinimalPoster,
        LayoutKind.Cinematic
    ];
    private readonly PinRenderer _renderer = new();
    private readonly ObservableCollection<BatchItem> _batchItems = [];
    private readonly List<(string Title, string Code)> _inputRows = [];
    private readonly DispatcherTimer _previewTimer;
    private string? _imagePath;
    private string _inputFilePath = string.Empty;
    private bool _isLoaded;
    private int _previewVersion;

    public MainWindow()
    {
        InitializeComponent();

        SizeBox.ItemsSource = new[]
        {
            new PinSize("Pinterest standard", 1000, 1500),
            new PinSize("Pinterest tall", 1000, 1600),
            new PinSize("Portrait social", 1080, 1350),
            new PinSize("Square", 1080, 1080)
        };
        ThreadBox.ItemsSource = BuildThreadOptions();
        SizeBox.SelectedIndex = 0;
        ThreadBox.SelectedIndex = 0;
        BatchList.ItemsSource = _batchItems;
        OutputBox.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Pin Creator Output");

        var settings = UserSettingsStore.Load();
        if (settings is not null)
        {
            OutputBox.Text = string.IsNullOrWhiteSpace(settings.OutputFolder) ? OutputBox.Text : settings.OutputFolder;
            SizeBox.SelectedIndex = Math.Clamp(settings.SizeIndex, 0, SizeBox.Items.Count - 1);
            FormatBox.SelectedIndex = Math.Clamp(settings.FormatIndex, 0, FormatBox.Items.Count - 1);
            QualityBox.Text = settings.Quality;
            SelectThreadCount(settings.ThreadCount);
            if (File.Exists(settings.InputFilePath)) LoadInputFile(settings.InputFilePath);
        }

        _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(240) };
        _previewTimer.Tick += async (_, _) =>
        {
            _previewTimer.Stop();
            await RefreshPreviewAsync();
        };
        _isLoaded = true;
    }

    private void SetImage(string path)
    {
        if (!File.Exists(path)) return;
        _imagePath = path;
        EmptyState.Visibility = Visibility.Collapsed;
        StatusText.Text = $"Loaded {Path.GetFileName(path)}";
        SchedulePreview();
    }

    private void Input_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoaded) SchedulePreview();
    }

    private void SchedulePreview()
    {
        if (_imagePath is null) return;
        _previewTimer.Stop();
        _previewTimer.Start();
    }

    private async Task RefreshPreviewAsync()
    {
        if (_imagePath is null || BatchList.SelectedItem is not BatchItem item || SizeBox.SelectedItem is not PinSize size) return;

        var layout = LayoutForItem(item);
        var content = new PinContent(item.ImagePath, item.Title, item.Subtitle, string.Empty, string.Empty, string.Empty, string.Empty);
        var version = ++_previewVersion;
        StatusText.Text = "Rendering preview...";
        try
        {
            var bitmap = await Task.Run(() => _renderer.Render(content, layout, size));
            if (version != _previewVersion) return;
            PreviewImage.Source = bitmap;
            PreviewMeta.Text = $"{layout.Name}  /  {size.Width} x {size.Height}";
            StatusText.Text = "Preview ready";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Preview failed";
            MessageBox.Show(this, ex.Message, "Could not render image", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ChooseOutput_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Choose batch output folder", InitialDirectory = OutputBox.Text, Multiselect = false };
        if (dialog.ShowDialog(this) == true)
        {
            OutputBox.Text = dialog.FolderName;
            SaveSettings();
        }
    }

    private void AddBatchImages_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = ImageFilter, Multiselect = true, Title = "Add images to batch" };
        if (dialog.ShowDialog(this) == true) AddBatchFiles(dialog.FileNames);
    }

    private void AddBatchFiles(IEnumerable<string> files)
    {
        var firstAdded = -1;
        foreach (var file in files.Where(IsSupportedImage))
        {
            if (_batchItems.Any(item => item.ImagePath.Equals(file, StringComparison.OrdinalIgnoreCase))) continue;
            if (firstAdded < 0) firstAdded = _batchItems.Count;
            _batchItems.Add(new BatchItem { ImagePath = file, Title = TitleFromFile(file) });
        }
        ApplyInputRows();
        UpdateQueueCount();
        if (BatchList.SelectedIndex < 0 && firstAdded >= 0) BatchList.SelectedIndex = firstAdded;
        StatusText.Text = $"{_batchItems.Count} images ready";
    }

    private void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Add an image folder", Multiselect = false };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            AddBatchFiles(Directory.EnumerateFiles(dialog.FolderName, "*.*", SearchOption.AllDirectories));
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Could not read folder", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ImportCsv_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "CSV files|*.csv", Title = "Import batch CSV" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            foreach (var item in CsvBatchParser.Parse(dialog.FileName)) _batchItems.Add(item);
            UpdateQueueCount();
            if (BatchList.SelectedIndex < 0 && _batchItems.Count > 0) BatchList.SelectedIndex = 0;
            StatusText.Text = $"Imported {_batchItems.Count} batch rows";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "CSV import failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ImportTitles_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "input.txt|input.txt|Text files|*.txt|All files|*.*", Title = "Select input.txt" };
        if (dialog.ShowDialog(this) != true) return;
        LoadInputFile(dialog.FileName);
        ApplyInputRows();
        SaveSettings();
        StatusText.Text = _batchItems.Count == 0
            ? $"Loaded {_inputRows.Count} rows. Add an image folder next."
            : $"Paired {Math.Min(_inputRows.Count, _batchItems.Count)} input rows with images";
    }

    private void LoadInputFile(string path)
    {
        var lines = File.ReadLines(path).Where(line => !string.IsNullOrWhiteSpace(line)).ToArray();
        _inputRows.Clear();
        foreach (var line in lines)
        {
            var parts = line.Split('|', 2, StringSplitOptions.TrimEntries);
            _inputRows.Add((parts[0], parts.Length > 1 ? parts[1] : string.Empty));
        }
        _inputFilePath = path;
        InputFileBox.Text = path;
        ApplyInputRows();
        InputSummaryText.Text = $"{_inputRows.Count} input rows loaded and saved";
    }

    private void ApplyInputRows()
    {
        var count = Math.Min(_inputRows.Count, _batchItems.Count);
        foreach (var item in _batchItems)
        {
            item.Title = TitleFromFile(item.ImagePath);
            item.Code = string.Empty;
        }
        for (var i = 0; i < count; i++)
        {
            _batchItems[i].Title = _inputRows[i].Title;
            _batchItems[i].Code = _inputRows[i].Code;
        }
        BatchList.Items.Refresh();
        SchedulePreview();
    }

    private void RemoveSelected_Click(object sender, RoutedEventArgs e)
    {
        var index = BatchList.SelectedIndex;
        if (index < 0) return;
        _batchItems.RemoveAt(index);
        UpdateQueueCount();
        if (_batchItems.Count > 0) BatchList.SelectedIndex = Math.Min(index, _batchItems.Count - 1);
        else ResetSelection();
    }

    private void ClearBatch_Click(object sender, RoutedEventArgs e)
    {
        _batchItems.Clear();
        UpdateQueueCount();
        ResetSelection();
        StatusText.Text = "Batch queue cleared";
    }

    private async void RefreshApp_Click(object sender, RoutedEventArgs e)
    {
        await RefreshAppStateAsync();
    }

    private void BatchList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isLoaded) return;
        if (BatchList.SelectedItem is BatchItem item) LoadSelectedItem(item);
        else if (_batchItems.Count == 0) ResetSelection();
    }

    private void LoadSelectedItem(BatchItem item)
    {
        SetImage(item.ImagePath);
    }

    private void ResetSelection()
    {
        _previewVersion++;
        _imagePath = null;
        PreviewImage.Source = null;
        EmptyState.Visibility = Visibility.Visible;
        PreviewMeta.Text = "No item selected";
    }

    private void UpdateQueueCount()
    {
        QueueCountText.Text = _batchItems.Count == 0
            ? "Build a queue to begin"
            : $"{_batchItems.Count} image{(_batchItems.Count == 1 ? string.Empty : "s")} ready for production";
    }

    private async Task RefreshAppStateAsync()
    {
        _previewTimer.Stop();
        _previewVersion++;

        var settings = UserSettingsStore.Load();
        if (settings is not null)
        {
            OutputBox.Text = string.IsNullOrWhiteSpace(settings.OutputFolder) ? OutputBox.Text : settings.OutputFolder;
            SizeBox.SelectedIndex = Math.Clamp(settings.SizeIndex, 0, SizeBox.Items.Count - 1);
            FormatBox.SelectedIndex = Math.Clamp(settings.FormatIndex, 0, FormatBox.Items.Count - 1);
            QualityBox.Text = settings.Quality;
            SelectThreadCount(settings.ThreadCount);
            if (!string.IsNullOrWhiteSpace(settings.InputFilePath))
            {
                _inputFilePath = settings.InputFilePath;
            }
        }

        foreach (var item in _batchItems)
        {
            item.Status = "Queued";
        }

        if (!string.IsNullOrWhiteSpace(_inputFilePath) && File.Exists(_inputFilePath))
        {
            LoadInputFile(_inputFilePath);
        }
        else
        {
            _inputRows.Clear();
            InputFileBox.Text = "No input file selected";
            InputSummaryText.Text = "Waiting for a title bank";
            ApplyInputRows();
        }

        UpdateQueueCount();
        BatchList.Items.Refresh();

        if (BatchList.SelectedItem is BatchItem selected)
        {
            await RefreshPreviewAsyncFor(selected);
        }
        else if (_batchItems.Count > 0)
        {
            BatchList.SelectedIndex = 0;
        }
        else
        {
            ResetSelection();
        }

        StatusText.Text = string.IsNullOrWhiteSpace(_inputFilePath) || !File.Exists(_inputFilePath)
            ? "App refreshed. Select input.txt to load new rows."
            : $"App refreshed. Reloaded {_inputRows.Count} input rows.";
    }

    private async Task RefreshPreviewAsyncFor(BatchItem item)
    {
        if (!File.Exists(item.ImagePath))
        {
            ResetSelection();
            return;
        }

        _imagePath = item.ImagePath;
        EmptyState.Visibility = Visibility.Collapsed;
        await RefreshPreviewAsync();
    }

    private async void ExportBatch_Click(object sender, RoutedEventArgs e)
    {
        if (_batchItems.Count == 0)
        {
            MessageBox.Show(this, "Add images or import a CSV first.", "Batch is empty", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (string.IsNullOrWhiteSpace(_inputFilePath) || !File.Exists(_inputFilePath))
        {
            MessageBox.Show(this, "Select a valid input.txt before running the batch.", "Input file required", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        LoadInputFile(_inputFilePath);
        if (_inputRows.Count == 0)
        {
            MessageBox.Show(this, "The selected input file has no title|code rows.", "Input file is empty", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (SizeBox.SelectedItem is not PinSize size) return;

        var outputFolder = OutputBox.Text;
        Directory.CreateDirectory(outputFolder);
        var pairedCount = Math.Min(_batchItems.Count, _inputRows.Count);
        var items = _batchItems.Take(pairedCount).ToArray();
        var invalidRow = items.FirstOrDefault(item => string.IsNullOrWhiteSpace(item.Title) || string.IsNullOrWhiteSpace(item.Code));
        if (invalidRow is not null)
        {
            MessageBox.Show(this, "Every input row must use title|code. One or more paired rows has a missing title or code.", "Invalid input row", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var duplicateCode = items.GroupBy(item => item.Code, StringComparer.OrdinalIgnoreCase).FirstOrDefault(group => group.Count() > 1);
        if (duplicateCode is not null)
        {
            MessageBox.Show(this, $"The code '{duplicateCode.Key}' appears more than once. Every output code must be unique.", "Duplicate output code", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        foreach (var unpaired in _batchItems.Skip(pairedCount)) unpaired.Status = "No input";
        BatchList.Items.Refresh();
        var extension = SelectedExtension();
        var quality = Quality();
        var completed = 0;
        var threadCount = SelectedThreadCount();

        BatchExportButton.IsEnabled = false;
        Progress.Value = 0;
        StatusText.Text = $"Rendering {items.Length} pins with {threadCount} thread{(threadCount == 1 ? string.Empty : "s")}...";

        try
        {
            await Parallel.ForEachAsync(items, new ParallelOptions { MaxDegreeOfParallelism = threadCount }, async (item, _) =>
            {
                await Dispatcher.InvokeAsync(() => { item.Status = "Rendering"; BatchList.Items.Refresh(); });
                try
                {
                    var itemIndex = Array.IndexOf(items, item);
                    var layout = SelectLayout(item, itemIndex);
                    var content = new PinContent(item.ImagePath, item.Title, item.Subtitle, string.Empty, string.Empty, string.Empty, string.Empty);
                    var bitmap = _renderer.Render(content, layout, size);
                    var output = Path.Combine(outputFolder, SafeFileName(item.Code) + extension);
                    _renderer.Save(bitmap, output, quality);
                    await Dispatcher.InvokeAsync(() => item.Status = "Saved");
                }
                catch
                {
                    await Dispatcher.InvokeAsync(() => item.Status = "Failed");
                }
                finally
                {
                    var count = Interlocked.Increment(ref completed);
                    await Dispatcher.InvokeAsync(() =>
                    {
                        Progress.Value = count * 100.0 / items.Length;
                        StatusText.Text = $"Rendered {count} of {items.Length}";
                        BatchList.Items.Refresh();
                    });
                }
            });
            StatusText.Text = $"Batch complete: {items.Count(item => item.Status == "Saved")} saved to {outputFolder}";
        }
        finally
        {
            BatchExportButton.IsEnabled = true;
        }
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files) return;
        var droppedFiles = new List<string>();
        foreach (var path in files)
        {
            if (File.Exists(path) && IsSupportedImage(path)) droppedFiles.Add(path);
            else if (Directory.Exists(path)) droppedFiles.AddRange(Directory.EnumerateFiles(path, "*.*", SearchOption.AllDirectories));
        }
        AddBatchFiles(droppedFiles);
    }

    private string SelectedExtension() => FormatBox.SelectedIndex == 1 ? ".jpg" : ".png";

    private LayoutDefinition LayoutForItem(BatchItem item)
    {
        var index = _batchItems.IndexOf(item);
        return SelectLayout(item, Math.Max(0, index));
    }

    private static LayoutDefinition SelectLayout(BatchItem item, int index)
    {
        var wordCount = item.Title.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        var choices = wordCount > 8
            ? LayoutCatalog.All.Where(layout => SpaciousLayouts.Contains(layout.Kind)).ToArray()
            : LayoutCatalog.All.ToArray();
        return choices[index % choices.Length];
    }

    private void SaveSettings()
    {
        UserSettingsStore.Save(new UserSettings(
            _inputFilePath,
            OutputBox.Text,
            SizeBox.SelectedIndex,
            FormatBox.SelectedIndex,
            QualityBox.Text,
            SelectedThreadOption().Degree));
    }

    protected override void OnClosed(EventArgs e)
    {
        SaveSettings();
        base.OnClosed(e);
    }

    private int Quality() => int.TryParse(QualityBox.Text, out var value) ? Math.Clamp(value, 1, 100) : 85;

    private IReadOnlyList<ThreadOption> BuildThreadOptions()
    {
        var cpuCount = Math.Max(1, Environment.ProcessorCount);
        var autoDegree = DefaultThreadCount();
        var options = new List<ThreadOption> { new($"Auto ({autoDegree} threads)", 0) };
        for (var i = 1; i <= cpuCount; i++)
        {
            options.Add(new ThreadOption($"{i} thread{(i == 1 ? string.Empty : "s")}", i));
        }
        return options;
    }

    private int DefaultThreadCount() => Math.Max(1, Environment.ProcessorCount / 2);

    private ThreadOption SelectedThreadOption() =>
        ThreadBox.SelectedItem as ThreadOption ?? (ThreadBox.Items.OfType<ThreadOption>().FirstOrDefault() ?? new ThreadOption($"Auto ({DefaultThreadCount()} threads)", 0));

    private int SelectedThreadCount()
    {
        var selected = SelectedThreadOption().Degree;
        return selected <= 0 ? DefaultThreadCount() : selected;
    }

    private void SelectThreadCount(int threadCount)
    {
        var selected = ThreadBox.Items.OfType<ThreadOption>().FirstOrDefault(option => option.Degree == threadCount);
        ThreadBox.SelectedItem = selected ?? ThreadBox.Items.OfType<ThreadOption>().FirstOrDefault();
    }

    private static bool IsSupportedImage(string path) => new[] { ".jpg", ".jpeg", ".png", ".bmp", ".tif", ".tiff" }.Contains(Path.GetExtension(path).ToLowerInvariant());

    private static string TitleFromFile(string path) => Path.GetFileNameWithoutExtension(path).Replace('_', ' ').Replace('-', ' ').Trim();

    private static string SafeFileName(string title)
    {
        var cleaned = string.Join("-", title.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries))
            .Trim().Replace(' ', '-');
        return string.IsNullOrWhiteSpace(cleaned) ? "pin" : cleaned[..Math.Min(80, cleaned.Length)];
    }

    private static string UniquePath(string folder, string name, string extension)
    {
        var path = Path.Combine(folder, name + extension);
        for (var i = 2; File.Exists(path); i++) path = Path.Combine(folder, $"{name}-{i}{extension}");
        return path;
    }
}
