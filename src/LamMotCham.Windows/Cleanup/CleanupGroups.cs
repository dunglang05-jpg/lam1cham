using System.Runtime.InteropServices;
using LamMotCham.Core.Tasks;
using LamMotCham.Windows.Native;

namespace LamMotCham.Windows.Cleanup;

public sealed record CleanupScan(long FileCount, long Bytes, long SkippedCount, string? Error);

/// <summary>
/// Một nhóm có thể dọn. Phạm vi cố định trong code (allowlist); không bao giờ nhận đường dẫn từ người dùng.
/// </summary>
public abstract class CleanupGroup
{
    public abstract string Key { get; }

    public abstract string Name { get; }

    public abstract string Scope { get; }

    public abstract bool IsSafe { get; }

    public abstract bool NeedsAdmin { get; }

    public abstract CleanupScan Scan(CancellationToken cancellationToken);

    /// <summary>Xóa và trả về kết quả quét lại sau khi xóa.</summary>
    public abstract CleanupScan Clean(CancellationToken cancellationToken);
}

/// <summary>File cũ trong một thư mục tạm cố định. Bỏ qua liên kết (reparse point) và file đang dùng.</summary>
public sealed class OldFilesGroup : CleanupGroup
{
    private readonly Func<string> _root;
    private readonly Func<string, bool> _fileFilter;
    private readonly TimeSpan _minAge;
    private readonly bool _recursive;

    public OldFilesGroup(string key, string name, string scope, Func<string> root, TimeSpan minAge, bool needsAdmin, bool recursive = true, Func<string, bool>? fileFilter = null)
    {
        Key = key;
        Name = name;
        Scope = scope;
        _root = root;
        _minAge = minAge;
        NeedsAdmin = needsAdmin;
        _recursive = recursive;
        _fileFilter = fileFilter ?? (_ => true);
    }

    public override string Key { get; }

    public override string Name { get; }

    public override string Scope { get; }

    public override bool IsSafe => true;

    public override bool NeedsAdmin { get; }

    public static OldFilesGroup UserTemp() => new(
        "utemp", "File tạm của người dùng", "%LOCALAPPDATA%\\Temp — chỉ file cũ hơn 2 ngày",
        SafeUserTemp, TimeSpan.FromDays(2), needsAdmin: false);

    public static OldFilesGroup WindowsTemp() => new(
        "wtemp", "File tạm của Windows", "C:\\Windows\\Temp — chỉ file cũ hơn 2 ngày, cần quyền quản trị",
        () => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp"), TimeSpan.FromDays(2), needsAdmin: true);

    public static OldFilesGroup ThumbnailCache() => new(
        "thumb", "Bộ nhớ đệm hình thu nhỏ", "Explorer tự tạo lại khi cần; file đang dùng sẽ được bỏ qua",
        () => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Windows", "Explorer"),
        TimeSpan.Zero, needsAdmin: false, recursive: false,
        fileFilter: f => Path.GetFileName(f).StartsWith("thumbcache_", StringComparison.OrdinalIgnoreCase) &&
                         f.EndsWith(".db", StringComparison.OrdinalIgnoreCase));

    /// <summary>Chỉ chấp nhận thư mục Temp nằm trong hồ sơ người dùng hiện tại.</summary>
    private static string SafeUserTemp()
    {
        var temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar);
        var profile = Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)).TrimEnd(Path.DirectorySeparatorChar);
        if (profile.Length < 4 ||
            !temp.StartsWith(profile + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            !temp.EndsWith("Temp", StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException("Thư mục Temp nằm ngoài phạm vi cho phép.");
        }

        return temp;
    }

    public override CleanupScan Scan(CancellationToken cancellationToken) => Walk(delete: false, cancellationToken);

    public override CleanupScan Clean(CancellationToken cancellationToken)
    {
        var deleted = Walk(delete: true, cancellationToken);
        var after = Walk(delete: false, cancellationToken);
        return after with { SkippedCount = deleted.SkippedCount, Error = deleted.Error ?? after.Error };
    }

    private CleanupScan Walk(bool delete, CancellationToken cancellationToken)
    {
        string root;
        try
        {
            root = _root();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return new CleanupScan(0, 0, 0, "Không xác định được thư mục.");
        }

        if (!Directory.Exists(root))
        {
            return new CleanupScan(0, 0, 0, null);
        }

        long count = 0, bytes = 0, skipped = 0;
        string? error = null;
        var cutoff = DateTime.UtcNow - _minAge;
        var stack = new Stack<string>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var dir = stack.Pop();
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(dir).ToList();
                if (_recursive)
                {
                    foreach (var sub in Directory.EnumerateDirectories(dir))
                    {
                        if (!new DirectoryInfo(sub).Attributes.HasFlag(FileAttributes.ReparsePoint))
                        {
                            stack.Push(sub);
                        }
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
                error ??= NeedsAdmin ? "Cần quyền quản trị để đọc thư mục này." : "Một số thư mục không đọc được.";
                continue;
            }
            catch (IOException)
            {
                continue;
            }

            foreach (var file in files)
            {
                FileInfo info;
                try
                {
                    info = new FileInfo(file);
                    if (info.Attributes.HasFlag(FileAttributes.ReparsePoint) || info.LastWriteTimeUtc > cutoff || !_fileFilter(file))
                    {
                        continue;
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    continue;
                }

                if (!delete)
                {
                    count++;
                    bytes += info.Length;
                    continue;
                }

                try
                {
                    if (info.IsReadOnly)
                    {
                        info.IsReadOnly = false;
                    }

                    info.Delete();
                    count++;
                    bytes += info.Length;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    skipped++;
                }
            }
        }

        return new CleanupScan(count, bytes, skipped, error);
    }
}

/// <summary>Thùng rác của mọi ổ. Xóa vĩnh viễn — không chọn sẵn.</summary>
public sealed class RecycleBinGroup : CleanupGroup
{
    public override string Key => "recycle";

    public override string Name => "Thùng rác";

    public override string Scope => "Xóa vĩnh viễn các file đang nằm trong Thùng rác";

    public override bool IsSafe => false;

    public override bool NeedsAdmin => false;

    public override CleanupScan Scan(CancellationToken cancellationToken)
    {
        var info = new NativeMethods.ShQueryRbInfo { Size = (uint)Marshal.SizeOf<NativeMethods.ShQueryRbInfo>() };
        var hr = NativeMethods.SHQueryRecycleBinW(null, ref info);
        return hr == 0
            ? new CleanupScan(info.NumItems, info.SizeBytes, 0, null)
            : new CleanupScan(0, 0, 0, $"Không đọc được Thùng rác (0x{hr:X8}).");
    }

    public override CleanupScan Clean(CancellationToken cancellationToken)
    {
        var before = Scan(cancellationToken);
        if (before.FileCount == 0)
        {
            return before;
        }

        var hr = NativeMethods.SHEmptyRecycleBinW(
            IntPtr.Zero, null, NativeMethods.SherbNoConfirmation | NativeMethods.SherbNoProgressUi | NativeMethods.SherbNoSound);
        var after = Scan(cancellationToken);
        return hr == 0 ? after : after with { Error = $"Windows báo lỗi khi làm trống Thùng rác (0x{hr:X8})." };
    }
}

/// <summary>Task dọn một nhóm. Không thể hoàn tác.</summary>
public sealed class CleanupTask(CleanupGroup group) : ITaskModule
{
    private CleanupScan? _before;
    private CleanupScan? _result;

    public CleanupGroup Group { get; } = group;

    public TaskDescriptor Descriptor { get; } = new()
    {
        Id = "clean." + group.Key,
        DisplayName = "Dọn " + group.Name.ToLowerInvariant(),
        Version = 1,
        Risk = RiskLevel.Reversible,
        Privilege = group.NeedsAdmin ? PrivilegeLevel.Administrator : PrivilegeLevel.User,
        Kind = TaskKind.Configuration,
        Timeout = TimeSpan.FromMinutes(10),
        Rollback = RollbackSupport.NotPossible,
        RollbackNote = "File đã xóa không khôi phục được. Chỉ xóa trong phạm vi đã hiển thị khi xem trước.",
    };

    public Task<PreconditionResult> CheckPreconditionsAsync(TaskContext context, CancellationToken cancellationToken)
    {
        _before = Group.Scan(cancellationToken);
        return Task.FromResult(_before.FileCount == 0
            ? PreconditionResult.AlreadyInDesiredState("Không có gì để dọn.")
            : PreconditionResult.Met());
    }

    public Task<ExecuteResult> ExecuteAsync(TaskContext context, CancellationToken cancellationToken)
    {
        _result = Group.Clean(cancellationToken);
        return Task.FromResult(ExecuteResult.Completed());
    }

    public Task<VerifyResult> VerifyAsync(TaskContext context, CancellationToken cancellationToken)
    {
        var after = _result ?? Group.Scan(cancellationToken);
        var before = _before ?? after;
        var freed = Math.Max(0, before.Bytes - after.Bytes);
        var text = $"Đã giải phóng {FormatBytes(freed)}.";
        if (after.SkippedCount > 0)
        {
            text += $" Bỏ qua {after.SkippedCount} file đang được dùng.";
        }

        if (after.Error is not null)
        {
            return Task.FromResult(VerifyResult.Attention(text + " " + after.Error));
        }

        return Task.FromResult(after.FileCount <= after.SkippedCount
            ? VerifyResult.Passed(text)
            : VerifyResult.Attention(text + $" Còn {after.FileCount} file chưa xóa được."));
    }

    public Task<VerifyResult> RollbackAsync(TaskContext context, CancellationToken cancellationToken) =>
        Task.FromResult(VerifyResult.Inconclusive("Không thể hoàn tác việc xóa file."));

    public static string FormatBytes(long bytes)
    {
        var vi = System.Globalization.CultureInfo.GetCultureInfo("vi-VN");
        return bytes switch
        {
            >= 1L << 30 => (bytes / (double)(1L << 30)).ToString("0.0", vi) + " GB",
            >= 1L << 20 => (bytes / (double)(1L << 20)).ToString("0", vi) + " MB",
            >= 1L << 10 => (bytes / (double)(1L << 10)).ToString("0", vi) + " KB",
            _ => bytes.ToString(vi) + " B",
        };
    }
}
