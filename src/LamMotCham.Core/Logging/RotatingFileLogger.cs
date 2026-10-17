using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;

namespace LamMotCham.Core.Logging;

/// <summary>
/// Ghi log dạng JSON Lines, xoay vòng theo dung lượng để tổng log không vượt
/// <c>MaxFileBytes × MaxFiles</c>.
/// </summary>
public sealed class RotatingFileLogger : RedactingRunLogger
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
    };

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly string _directory;
    private readonly string _baseName;
    private readonly Lock _gate = new();

    public RotatingFileLogger(
        string directory,
        string baseName = "lam1cham",
        long maxFileBytes = 1_048_576,
        int maxFiles = 5,
        TimeProvider? timeProvider = null)
        : base(timeProvider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseName);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxFileBytes, 1024);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxFiles, 1);

        _directory = directory;
        _baseName = baseName;
        MaxFileBytes = maxFileBytes;
        MaxFiles = maxFiles;
        Directory.CreateDirectory(directory);
    }

    public long MaxFileBytes { get; }

    public int MaxFiles { get; }

    public string CurrentFilePath => PathFor(0);

    protected override void Write(LogEntry entry)
    {
        var line = JsonSerializer.Serialize(
            new { t = entry.Timestamp, level = entry.Level.ToString(), task = entry.TaskId, msg = entry.Message },
            JsonOptions) + "\n";
        var bytes = Utf8NoBom.GetBytes(line);

        lock (_gate)
        {
            var current = new FileInfo(CurrentFilePath);
            if (current.Exists && current.Length + bytes.Length > MaxFileBytes)
            {
                Rotate();
            }

            using var stream = new FileStream(CurrentFilePath, FileMode.Append, FileAccess.Write, FileShare.Read);
            stream.Write(bytes);
        }
    }

    private void Rotate()
    {
        var oldest = PathFor(MaxFiles - 1);
        if (File.Exists(oldest))
        {
            File.Delete(oldest);
        }

        for (var i = MaxFiles - 2; i >= 0; i--)
        {
            var source = PathFor(i);
            if (File.Exists(source))
            {
                File.Move(source, PathFor(i + 1), overwrite: true);
            }
        }
    }

    private string PathFor(int index) =>
        Path.Combine(_directory, index == 0 ? $"{_baseName}.log" : $"{_baseName}.{index}.log");
}
