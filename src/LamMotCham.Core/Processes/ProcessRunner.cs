using System.Diagnostics;
using System.Text;

namespace LamMotCham.Core.Processes;

public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError)
{
    /// <summary>Mã thoát dạng HRESULT hex, ví dụ 0x8A150014.</summary>
    public string ExitCodeHex => $"0x{unchecked((uint)ExitCode):X8}";
}

/// <summary>
/// Chạy tiến trình con an toàn: đường dẫn tuyệt đối, tham số tách rời (không qua shell),
/// giới hạn dung lượng output và chỉ dừng cây tiến trình do chính ứng dụng khởi chạy.
/// </summary>
public static class ProcessRunner
{
    public const int DefaultMaxOutputChars = 64_000;

    public static async Task<ProcessResult> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        CancellationToken cancellationToken,
        int maxOutputChars = DefaultMaxOutputChars,
        Encoding? outputEncoding = null,
        bool killOnCancel = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(arguments);

        if (!Path.IsPathFullyQualified(fileName))
        {
            throw new ArgumentException("Phải dùng đường dẫn tuyệt đối để tránh chạy nhầm chương trình cùng tên.", nameof(fileName));
        }

        var startInfo = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            CreateNoWindow = true,
            StandardOutputEncoding = outputEncoding ?? Encoding.UTF8,
            StandardErrorEncoding = outputEncoding ?? Encoding.UTF8,
            WorkingDirectory = Path.GetDirectoryName(fileName) ?? Environment.CurrentDirectory,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        var stdout = new BoundedTextBuffer(maxOutputChars);
        var stderr = new BoundedTextBuffer(maxOutputChars);
        process.OutputDataReceived += (_, e) => stdout.AppendLine(e.Data);
        process.ErrorDataReceived += (_, e) => stderr.AppendLine(e.Data);

        if (!process.Start())
        {
            throw new InvalidOperationException($"Không khởi chạy được {Path.GetFileName(fileName)}.");
        }

        // Không cho tiến trình con chờ nhập liệu.
        process.StandardInput.Close();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            if (killOnCancel)
            {
                TryKill(process);
            }

            throw;
        }

        // Bảo đảm đã đọc hết output bất đồng bộ.
        process.WaitForExit();
        return new ProcessResult(process.ExitCode, stdout.ToString(), stderr.ToString());
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5_000);
            }
        }
        catch (InvalidOperationException)
        {
            // Tiến trình đã thoát.
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Không đủ quyền dừng; orchestrator sẽ xử lý task không phản hồi.
        }
    }

    /// <summary>Giữ phần cuối của output, bỏ phần đầu khi vượt giới hạn.</summary>
    private sealed class BoundedTextBuffer(int maxChars)
    {
        private readonly StringBuilder _builder = new();
        private readonly Lock _gate = new();
        private bool _truncated;

        public void AppendLine(string? line)
        {
            if (line is null)
            {
                return;
            }

            lock (_gate)
            {
                _builder.Append(line).Append('\n');
                if (_builder.Length > maxChars)
                {
                    _builder.Remove(0, _builder.Length - maxChars);
                    _truncated = true;
                }
            }
        }

        public override string ToString()
        {
            lock (_gate)
            {
                return _truncated ? "[...]\n" + _builder : _builder.ToString();
            }
        }
    }
}
