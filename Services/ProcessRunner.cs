using System.Diagnostics;
using System.IO;
using System.Text;

namespace MusicConverter.Services;

internal sealed record ProcessResult(int ExitCode, string CombinedOutput);

internal static class ProcessRunner
{
    public static async Task<ProcessResult> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        Action<string>? onOutput = null,
        CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = workingDirectory ?? AppContext.BaseDirectory
        };

        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var output = new StringBuilder();
        var stdoutClosed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stderrClosed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) { stdoutClosed.TrySetResult(); return; }
            lock (output) output.AppendLine(e.Data);
            onOutput?.Invoke(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) { stderrClosed.TrySetResult(); return; }
            lock (output) output.AppendLine(e.Data);
            onOutput?.Invoke(e.Data);
        };

        try
        {
            if (!process.Start()) throw new InvalidOperationException($"无法启动 {Path.GetFileName(executable)}。");
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using var registration = cancellationToken.Register(() =>
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
            });

            await process.WaitForExitAsync(cancellationToken);
            await Task.WhenAll(stdoutClosed.Task, stderrClosed.Task).WaitAsync(cancellationToken);

            string combined;
            lock (output) combined = output.ToString();
            return new ProcessResult(process.ExitCode, combined);
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new InvalidOperationException($"无法运行 {Path.GetFileName(executable)}：{ex.Message}", ex);
        }
    }
}
