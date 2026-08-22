using System.IO;

namespace MusicConverter.Services;

internal sealed record ConversionRequest(string InputFile, string OutputDirectory, string QmdecPath, string FfmpegPath);
internal sealed record ConversionProgress(double Percent, string Badge, string Message, string? LogLine = null);
internal sealed record ConversionResult(string OutputFile);

internal sealed class ConverterService
{
    public async Task<ConversionResult> ConvertAsync(
        ConversionRequest request,
        IProgress<ConversionProgress> progress,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(request.InputFile)) throw new FileNotFoundException("找不到输入文件。", request.InputFile);
        Directory.CreateDirectory(request.OutputDirectory);

        var workDirectory = Path.Combine(Path.GetTempPath(), "MusicConverter", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDirectory);

        try
        {
            progress.Report(new ConversionProgress(12, "解密中", "正在通过 qmdec 解密 MGG…", "启动 qmdec decrypt"));
            var decryptResult = await ProcessRunner.RunAsync(
                request.QmdecPath,
                ["decrypt", request.InputFile, "-o", workDirectory, "--no-tag"],
                workDirectory,
                line => progress.Report(new ConversionProgress(32, "解密中", "正在通过 qmdec 解密 MGG…", line)),
                cancellationToken);

            if (decryptResult.ExitCode != 0)
                throw new InvalidOperationException(BuildProcessError("qmdec 解密失败", decryptResult));

            var decryptedFile = FindDecryptedAudio(workDirectory);
            if (decryptedFile is null)
                throw new InvalidOperationException("qmdec 已结束，但没有生成 OGG 文件。请确认 QQ 音乐已登录并完成授权；也可在“运行日志”中查看 qmdec 的输出。");

            progress.Report(new ConversionProgress(58, "转码中", "解密完成，正在编码 MP3…", $"已生成临时文件：{Path.GetFileName(decryptedFile)}"));

            var finalOutput = CreateUniqueOutputPath(request.OutputDirectory, Path.GetFileNameWithoutExtension(request.InputFile), ".mp3");
            var stagedOutput = Path.Combine(workDirectory, "converted.mp3");
            var ffmpegResult = await ProcessRunner.RunAsync(
                request.FfmpegPath,
                ["-hide_banner", "-y", "-i", decryptedFile, "-map_metadata", "0", "-vn", "-codec:a", "libmp3lame", "-q:a", "2", "-id3v2_version", "3", stagedOutput],
                workDirectory,
                line => progress.Report(new ConversionProgress(78, "转码中", "FFmpeg 正在编码 MP3…", line)),
                cancellationToken);

            if (ffmpegResult.ExitCode != 0 || !File.Exists(stagedOutput))
                throw new InvalidOperationException(BuildProcessError("FFmpeg 转码失败", ffmpegResult));

            File.Move(stagedOutput, finalOutput);
            progress.Report(new ConversionProgress(100, "完成", "转换完成", $"MP3 已保存：{finalOutput}"));
            return new ConversionResult(finalOutput);
        }
        finally
        {
            try { if (Directory.Exists(workDirectory)) Directory.Delete(workDirectory, recursive: true); } catch { }
        }
    }

    private static string? FindDecryptedAudio(string directory)
    {
        var preferredExtensions = new[] { ".ogg", ".opus", ".flac", ".mp3" };
        var files = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Select(path => new FileInfo(path))
            .Where(info => preferredExtensions.Contains(info.Extension, StringComparer.OrdinalIgnoreCase))
            .OrderByDescending(info => info.LastWriteTimeUtc)
            .ToList();
        return files.FirstOrDefault()?.FullName;
    }

    private static string CreateUniqueOutputPath(string directory, string baseName, string extension)
    {
        var path = Path.Combine(directory, baseName + extension);
        if (!File.Exists(path)) return path;

        for (var index = 1; index < 10_000; index++)
        {
            path = Path.Combine(directory, $"{baseName} ({index}){extension}");
            if (!File.Exists(path)) return path;
        }
        throw new IOException("输出目录中存在过多同名文件。");
    }

    private static string BuildProcessError(string title, ProcessResult result)
    {
        var details = result.CombinedOutput.Trim();
        if (details.Length > 1800) details = details[^1800..];
        return $"{title}（退出码 {result.ExitCode}）。" + (details.Length > 0 ? $"\n\n{details}" : string.Empty);
    }
}
