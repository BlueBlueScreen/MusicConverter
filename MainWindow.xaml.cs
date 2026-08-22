using Microsoft.Win32;
using MusicConverter.Services;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace MusicConverter;

public partial class MainWindow : Window
{
    private readonly ToolLocator _toolLocator = new();
    private readonly ConverterService _converterService = new();
    private readonly List<string> _inputFiles = [];
    private string? _lastOutputFolder;
    private bool _isBusy;

    public MainWindow()
    {
        InitializeComponent();
        OutputFolderTextBox.Text = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
        LoadDetectedTools();
        AppendLog("应用已就绪。可一次选择或拖入多个 MGG 文件。", false);
    }

    private void LoadDetectedTools()
    {
        QmdecPathTextBox.Text = _toolLocator.Find("qmdec.exe") ?? string.Empty;
        FfmpegPathTextBox.Text = _toolLocator.Find("ffmpeg.exe") ?? string.Empty;
    }

    private void SelectInputFiles()
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择一个或多个 QQ 音乐加密文件",
            Filter = "QQ 音乐加密文件|*.mgg;*.mgg0;*.mgg1;*.mggl;*.qmcogg|所有文件|*.*",
            Multiselect = true,
            CheckFileExists = true
        };

        if (dialog.ShowDialog(this) == true)
            SetInputFiles(dialog.FileNames);
    }

    private void SetInputFiles(IEnumerable<string> paths)
    {
        var allowedExtensions = new[] { ".mgg", ".mgg0", ".mgg1", ".mggl", ".qmcogg" };
        var candidates = paths
            .Where(File.Exists)
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var validFiles = candidates
            .Where(path => allowedExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            .ToList();

        if (validFiles.Count == 0)
        {
            ShowError("没有找到支持的 MGG 文件。请选择 .mgg、.mgg0、.mgg1、.mggl 或 .qmcogg 文件。", "没有可转换文件");
            return;
        }

        _inputFiles.Clear();
        _inputFiles.AddRange(validFiles);

        var totalBytes = validFiles.Sum(path => new FileInfo(path).Length);
        FileTitleText.Text = validFiles.Count == 1
            ? Path.GetFileName(validFiles[0])
            : $"已选择 {validFiles.Count} 首歌曲";
        FileHintText.Text = validFiles.Count == 1
            ? $"{FormatFileSize(totalBytes)}  ·  点击可重新选择"
            : $"共 {FormatFileSize(totalBytes)}  ·  点击可重新选择";
        OutputFolderTextBox.Text = Path.GetDirectoryName(validFiles[0]) ?? OutputFolderTextBox.Text;
        ConvertButton.Content = validFiles.Count == 1 ? "开始转换" : $"开始转换（{validFiles.Count} 首）";
        ConvertButton.IsEnabled = !_isBusy;
        _lastOutputFolder = null;
        OpenOutputButton.Visibility = Visibility.Collapsed;
        UpdateStatus($"已选择 {validFiles.Count} 首歌曲", "就绪", 0, StatusKind.Idle);
        AppendLog($"已选择 {validFiles.Count} 个文件：");
        foreach (var file in validFiles) AppendLog($"  {file}", false);

        var skipped = candidates.Count - validFiles.Count;
        if (skipped > 0) AppendLog($"已忽略 {skipped} 个不支持的文件。", false);
    }

    private async void Convert_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy || _inputFiles.Count == 0) return;

        var qmdec = ResolveTool(QmdecPathTextBox.Text, "qmdec.exe");
        var ffmpeg = ResolveTool(FfmpegPathTextBox.Text, "ffmpeg.exe");
        if (qmdec is null || ffmpeg is null)
        {
            var missing = qmdec is null && ffmpeg is null ? "qmdec 和 FFmpeg" : qmdec is null ? "qmdec" : "FFmpeg";
            ShowError($"未找到 {missing}。请展开“工具设置”并选择对应的 exe，或将它放入应用的 tools 文件夹。", "缺少转换工具");
            return;
        }

        if (string.IsNullOrWhiteSpace(OutputFolderTextBox.Text))
        {
            ShowError("请选择输出目录。", "缺少输出目录");
            return;
        }

        _isBusy = true;
        SetControlsForBusy(true);
        LogTextBox.Clear();
        _lastOutputFolder = null;
        OpenOutputButton.Visibility = Visibility.Collapsed;

        try
        {
            var files = _inputFiles.ToArray();
            var successes = new List<ConversionResult>();
            var failures = new List<(string File, string Error)>();
            AppendLog($"开始批量转换，共 {files.Length} 首。", false);

            for (var index = 0; index < files.Length; index++)
            {
                var currentIndex = index;
                var currentFile = files[index];
                var currentNumber = index + 1;
                AppendLog($"──── [{currentNumber}/{files.Length}] {Path.GetFileName(currentFile)} ────", false);

                var request = new ConversionRequest(currentFile, OutputFolderTextBox.Text, qmdec, ffmpeg);
                var progress = new Progress<ConversionProgress>(p =>
                {
                    var overallPercent = ((currentIndex + p.Percent / 100d) / files.Length) * 100d;
                    var badge = files.Length == 1 ? p.Badge : $"{currentNumber}/{files.Length}";
                    var message = files.Length == 1 ? p.Message : $"{Path.GetFileName(currentFile)}\n{p.Message}";
                    UpdateStatus(message, badge, overallPercent, StatusKind.Working);
                    if (!string.IsNullOrWhiteSpace(p.LogLine)) AppendLog(p.LogLine);
                });

                try
                {
                    var result = await _converterService.ConvertAsync(request, progress);
                    successes.Add(result);
                    AppendLog($"[{currentNumber}/{files.Length}] 完成：{result.OutputFile}");
                }
                catch (Exception ex)
                {
                    failures.Add((currentFile, ex.Message));
                    AppendLog($"[{currentNumber}/{files.Length}] 失败：{ex.Message}", false);
                }
            }

            _lastOutputFolder = OutputFolderTextBox.Text;
            ConversionProgress.Value = 100;
            OpenOutputButton.Visibility = successes.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

            if (failures.Count == 0)
            {
                UpdateStatus($"全部转换完成，共 {successes.Count} 首", "完成", 100, StatusKind.Success);
                AppendLog($"批量转换完成：成功 {successes.Count} 首，失败 0 首。", false);
            }
            else if (successes.Count > 0)
            {
                UpdateStatus($"批量转换结束：成功 {successes.Count} 首，失败 {failures.Count} 首", "部分完成", 100, StatusKind.Error);
                AppendLog($"批量转换结束：成功 {successes.Count} 首，失败 {failures.Count} 首。", false);
                ShowWarning(BuildFailureSummary(failures), "部分文件转换失败");
            }
            else
            {
                UpdateStatus($"全部转换失败，共 {failures.Count} 首", "失败", 0, StatusKind.Error);
                ShowError(BuildFailureSummary(failures), "批量转换失败");
            }
        }
        catch (Exception ex)
        {
            UpdateStatus("转换失败，请查看运行日志", "失败", 0, StatusKind.Error);
            AppendLog(ex.Message, false);
            ShowError(ex.Message, "转换失败");
        }
        finally
        {
            _isBusy = false;
            SetControlsForBusy(false);
        }
    }

    private async void Auth_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy) return;
        var qmdec = ResolveTool(QmdecPathTextBox.Text, "qmdec.exe");
        if (qmdec is null)
        {
            ShowError("未找到 qmdec.exe。请在“工具设置”中选择它。", "缺少 qmdec");
            return;
        }

        _isBusy = true;
        SetControlsForBusy(true);
        LogTextBox.Clear();
        UpdateStatus("正在读取 QQ 音乐登录信息…", "授权中", 35, StatusKind.Working);
        AppendLog("请确保 QQ 音乐客户端已启动，并已登录拥有相应权益的账号。", false);

        try
        {
            var result = await ProcessRunner.RunAsync(qmdec, ["auth"], null, AppendLog);
            if (result.ExitCode != 0)
                throw new InvalidOperationException($"qmdec auth 失败（退出码 {result.ExitCode}）。\n{result.CombinedOutput.Trim()}");

            UpdateStatus("授权信息已保存，可以开始转换", "已授权", 100, StatusKind.Success);
        }
        catch (Exception ex)
        {
            UpdateStatus("授权失败，请查看日志", "失败", 0, StatusKind.Error);
            ShowError(ex.Message, "授权失败");
        }
        finally
        {
            _isBusy = false;
            SetControlsForBusy(false);
        }
    }

    private void SetControlsForBusy(bool busy)
    {
        ConvertButton.IsEnabled = !busy && _inputFiles.Count > 0;
        AuthButton.IsEnabled = !busy;
        DropZone.IsEnabled = !busy;
    }

    private string? ResolveTool(string configuredPath, string fileName)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
            return Path.GetFullPath(configuredPath);
        return _toolLocator.Find(fileName);
    }

    private void BrowseOutput_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "选择 MP3 输出目录",
            InitialDirectory = Directory.Exists(OutputFolderTextBox.Text) ? OutputFolderTextBox.Text : null
        };
        if (dialog.ShowDialog(this) == true) OutputFolderTextBox.Text = dialog.FolderName;
    }

    private void BrowseQmdec_Click(object sender, RoutedEventArgs e) => BrowseTool(QmdecPathTextBox, "qmdec.exe", "选择 qmdec.exe");
    private void BrowseFfmpeg_Click(object sender, RoutedEventArgs e) => BrowseTool(FfmpegPathTextBox, "ffmpeg.exe", "选择 ffmpeg.exe");

    private void BrowseTool(System.Windows.Controls.TextBox target, string fileName, string title)
    {
        var dialog = new OpenFileDialog { Title = title, Filter = $"{fileName}|{fileName}|可执行文件|*.exe", CheckFileExists = true };
        if (dialog.ShowDialog(this) == true) target.Text = dialog.FileName;
    }

    private void OpenOutput_Click(object sender, RoutedEventArgs e)
    {
        var folder = _lastOutputFolder ?? OutputFolderTextBox.Text;
        if (folder is not null && Directory.Exists(folder))
            Process.Start(new ProcessStartInfo("explorer.exe", folder) { UseShellExecute = true });
    }

    private void DropZone_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_isBusy) SelectInputFiles();
    }

    private void DropZone_DragEnter(object sender, DragEventArgs e)
    {
        if (!_isBusy && e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            DropZone.Background = new SolidColorBrush(Color.FromRgb(239, 236, 255));
            DropZone.BorderBrush = new SolidColorBrush(Color.FromRgb(123, 108, 246));
        }
        else e.Effects = DragDropEffects.None;
        e.Handled = true;
    }

    private void DropZone_DragLeave(object sender, DragEventArgs e) => ResetDropZone();

    private void DropZone_Drop(object sender, DragEventArgs e)
    {
        ResetDropZone();
        if (_isBusy || !e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0) SetInputFiles(files);
    }

    private void ResetDropZone()
    {
        DropZone.Background = new SolidColorBrush(Color.FromRgb(247, 246, 243));
        DropZone.BorderBrush = new SolidColorBrush(Color.FromRgb(217, 215, 208));
    }

    private void UpdateStatus(string message, string badge, double percent, StatusKind kind)
    {
        StatusText.Text = message;
        StatusBadgeText.Text = badge;
        ConversionProgress.Value = percent;

        (StatusBadge.Background, StatusBadgeText.Foreground) = kind switch
        {
            StatusKind.Success => (BrushFrom("#E4F5EB"), BrushFrom("#237A49")),
            StatusKind.Error => (BrushFrom("#FCE9E7"), BrushFrom("#B33B31")),
            StatusKind.Working => (BrushFrom("#ECE9FF"), BrushFrom("#5F4ED4")),
            _ => (BrushFrom("#EDEEF1"), BrushFrom("#6D717B"))
        };
    }

    private void AppendLog(string line) => AppendLog(line, true);

    private void AppendLog(string line, bool includeTimestamp)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => AppendLog(line, includeTimestamp));
            return;
        }

        if (string.IsNullOrWhiteSpace(line)) return;
        var prefix = includeTimestamp ? $"[{DateTime.Now:HH:mm:ss}] " : string.Empty;
        LogTextBox.AppendText(prefix + line.TrimEnd() + Environment.NewLine);
        LogTextBox.ScrollToEnd();
    }

    private static Brush BrushFrom(string color) => (Brush)new BrushConverter().ConvertFromString(color)!;

    private static string FormatFileSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        var value = (double)bytes;
        var index = 0;
        while (value >= 1024 && index < units.Length - 1) { value /= 1024; index++; }
        return $"{value:0.#} {units[index]}";
    }

    private void ShowError(string message, string title) => MessageBox.Show(this, message, title, MessageBoxButton.OK, MessageBoxImage.Error);
    private void ShowWarning(string message, string title) => MessageBox.Show(this, message, title, MessageBoxButton.OK, MessageBoxImage.Warning);

    private static string BuildFailureSummary(IReadOnlyList<(string File, string Error)> failures)
    {
        const int maxDisplayed = 5;
        var lines = failures.Take(maxDisplayed)
            .Select(item =>
            {
                var error = item.Error.Length > 500 ? item.Error[..500] + "…" : item.Error;
                return $"• {Path.GetFileName(item.File)}\n  {error}";
            })
            .ToList();
        if (failures.Count > maxDisplayed)
            lines.Add($"……另有 {failures.Count - maxDisplayed} 个失败文件，请查看运行日志。");
        return string.Join(Environment.NewLine + Environment.NewLine, lines);
    }
    private void ToolsExpander_Expanded(object sender, RoutedEventArgs e) => LoadDetectedToolsIfEmpty();
    private void LoadDetectedToolsIfEmpty()
    {
        if (string.IsNullOrWhiteSpace(QmdecPathTextBox.Text)) QmdecPathTextBox.Text = _toolLocator.Find("qmdec.exe") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(FfmpegPathTextBox.Text)) FfmpegPathTextBox.Text = _toolLocator.Find("ffmpeg.exe") ?? string.Empty;
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2) WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        else DragMove();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private enum StatusKind { Idle, Working, Success, Error }
}
