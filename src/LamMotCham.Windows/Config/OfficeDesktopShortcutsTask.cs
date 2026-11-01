using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using LamMotCham.Core.Logging;
using LamMotCham.Core.Tasks;
using LamMotCham.Windows.Infrastructure;
using Microsoft.Win32;

namespace LamMotCham.Windows.Config;

/// <summary>
/// Đưa Word, Excel, PowerPoint ra màn hình desktop chung của máy (mọi tài khoản đều thấy) — PO yêu cầu 01/11/2026.
/// Ưu tiên chép đúng lối tắt Office đã tạo trong Start Menu (giữ biểu tượng và đường dẫn chuẩn của Office);
/// không có thì tạo lối tắt tới file chạy tìm thấy qua "App Paths" của Windows. Chỉ tạo lối tắt còn thiếu,
/// ghi lại danh sách đã tạo để hoàn tác chỉ xóa đúng các lối tắt đó.
/// </summary>
public sealed class OfficeDesktopShortcutsTask(RollbackStore store, Func<string>? desktop = null) : ITaskModule, IDescribesCurrentState
{
    /// <summary>Ứng dụng Office: tên lối tắt, file chạy, các tên lối tắt cũ (Office 2013–2019 cài kiểu MSI).</summary>
    internal static readonly (string Name, string Exe)[] Apps =
    [
        ("Word", "WINWORD.EXE"),
        ("Excel", "EXCEL.EXE"),
        ("PowerPoint", "POWERPNT.EXE"),
    ];

    private readonly Func<string> _desktop = desktop ?? (() => Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory));

    public TaskDescriptor Descriptor { get; } = new()
    {
        Id = "win.officedesktop",
        DisplayName = "Đưa Word, Excel, PowerPoint ra desktop",
        Version = 1,
        Risk = RiskLevel.Reversible,
        Privilege = PrivilegeLevel.Administrator,
        Kind = TaskKind.Configuration,
        Timeout = TimeSpan.FromSeconds(30),
        Rollback = RollbackSupport.Supported,
        RollbackNote = "Xóa đúng các lối tắt Làm 1 Chạm đã tạo.",
    };

    /// <summary>Nguồn để tạo lối tắt cho một ứng dụng: lối tắt Start Menu (.lnk) hoặc file chạy (.exe).</summary>
    internal sealed record Source(string Name, string Path, bool IsShortcut);

    public Task<PreconditionResult> CheckPreconditionsAsync(TaskContext context, CancellationToken cancellationToken)
    {
        var found = FindSources();
        if (found.Count == 0)
        {
            return Task.FromResult(PreconditionResult.NotSupported("Máy chưa cài Microsoft Office (Word, Excel, PowerPoint). Cài Office trước rồi chạy lại."));
        }

        return Task.FromResult(found.All(s => File.Exists(Target(s.Name)))
            ? PreconditionResult.AlreadyInDesiredState($"{string.Join(", ", found.Select(s => s.Name))} đã có trên desktop.")
            : PreconditionResult.Met());
    }

    public Task<ExecuteResult> ExecuteAsync(TaskContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var created = new Dictionary<string, string?>(store.Load(Descriptor.Id) ?? new Dictionary<string, string?>(), StringComparer.Ordinal);
        var errors = new List<string>();
        Directory.CreateDirectory(_desktop());
        foreach (var source in FindSources())
        {
            var target = Target(source.Name);
            if (File.Exists(target))
            {
                continue;
            }

            try
            {
                if (source.IsShortcut)
                {
                    File.Copy(source.Path, target, overwrite: false);
                }
                else
                {
                    ShellLink.Create(target, source.Path);
                }

                created[source.Name] = target;
                context.Log.Info($"Đã tạo lối tắt {source.Name} trên desktop (từ {System.IO.Path.GetFileName(source.Path)}).", Descriptor.Id);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or COMException)
            {
                errors.Add($"{source.Name}: {ex.Message}");
            }
        }

        store.Save(Descriptor.Id, created);
        return Task.FromResult(errors.Count == 0
            ? ExecuteResult.Completed()
            : ExecuteResult.Failed("Không tạo được lối tắt: " + string.Join("; ", errors)));
    }

    public Task<VerifyResult> VerifyAsync(TaskContext context, CancellationToken cancellationToken)
    {
        var found = FindSources();
        var missing = found.Where(s => !File.Exists(Target(s.Name))).Select(s => s.Name).ToList();
        return Task.FromResult(found.Count == 0
            ? VerifyResult.NotSupported("Máy chưa cài Microsoft Office.")
            : missing.Count == 0
                ? VerifyResult.Passed($"Desktop đã có {string.Join(", ", found.Select(s => s.Name))}.")
                : VerifyResult.Failed($"Chưa thấy lối tắt {string.Join(", ", missing)} trên desktop."));
    }

    public Task<VerifyResult> RollbackAsync(TaskContext context, CancellationToken cancellationToken)
    {
        var created = store.Load(Descriptor.Id);
        if (created is null || created.Count == 0)
        {
            return Task.FromResult(VerifyResult.Passed("Không có lối tắt nào do Làm 1 Chạm tạo."));
        }

        var left = new List<string>();
        foreach (var path in created.Values.OfType<string>())
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                left.Add(System.IO.Path.GetFileName(path));
            }
        }

        if (left.Count > 0)
        {
            return Task.FromResult(VerifyResult.Failed("Không xóa được: " + string.Join(", ", left)));
        }

        store.Clear(Descriptor.Id);
        return Task.FromResult(VerifyResult.Passed("Đã xóa các lối tắt Office do Làm 1 Chạm tạo."));
    }

    public Task<string> DescribeCurrentStateAsync(CancellationToken cancellationToken)
    {
        var found = FindSources();
        return Task.FromResult(found.Count == 0
            ? "chưa cài Office"
            : $"{found.Count(s => File.Exists(Target(s.Name)))}/{found.Count} đã có trên desktop");
    }

    private string Target(string name) => System.IO.Path.Combine(_desktop(), name + ".lnk");

    /// <summary>Máy có ít nhất một trong Word, Excel, PowerPoint.</summary>
    public static bool IsOfficeInstalled() => FindSources().Count > 0;

    /// <summary>Tìm Word, Excel, PowerPoint đã cài: lối tắt Start Menu chung trước, sau đó App Paths.</summary>
    internal static List<Source> FindSources()
    {
        var result = new List<Source>();
        foreach (var (name, exe) in Apps)
        {
            if (FindStartMenuShortcut(name) is { } lnk)
            {
                result.Add(new Source(name, lnk, true));
            }
            else if (FindExe(exe) is { } path)
            {
                result.Add(new Source(name, path, false));
            }
        }

        return result;
    }

    private static string? FindStartMenuShortcut(string name)
    {
        var programs = Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms);
        try
        {
            var direct = System.IO.Path.Combine(programs, name + ".lnk");
            if (File.Exists(direct))
            {
                return direct;
            }

            // Office cài kiểu cũ: "Microsoft Office 2016\Word 2016.lnk".
            return Directory.Exists(programs)
                ? Directory.EnumerateFiles(programs, name + " 20??.lnk", SearchOption.AllDirectories).FirstOrDefault()
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string? FindExe(string exe)
    {
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using var key = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\" + exe);
                if (key?.GetValue(null) is string path && path.Trim('"') is var clean && File.Exists(clean))
                {
                    return clean;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                // Bỏ qua nhánh registry không đọc được.
            }
        }

        return null;
    }

    /// <summary>Tạo file .lnk bằng IShellLink của Windows (không qua PowerShell hay WScript).</summary>
    internal static class ShellLink
    {
        public static void Create(string shortcutPath, string targetPath)
        {
            var link = (IShellLinkW)new ShellLinkCoClass();
            try
            {
                link.SetPath(targetPath);
                link.SetWorkingDirectory(System.IO.Path.GetDirectoryName(targetPath) ?? "");
                link.SetIconLocation(targetPath, 0);
                ((IPersistFile)link).Save(shortcutPath, true);
            }
            finally
            {
                Marshal.FinalReleaseComObject(link);
            }
        }

        [ComImport]
        [Guid("00021401-0000-0000-C000-000000000046")]
        private class ShellLinkCoClass
        {
        }

        [ComImport]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [Guid("000214F9-0000-0000-C000-000000000046")]
        private interface IShellLinkW
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszFile, int cch, IntPtr pfd, uint fFlags);

            void GetIDList(out IntPtr ppidl);

            void SetIDList(IntPtr pidl);

            void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszName, int cch);

            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);

            void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszDir, int cch);

            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);

            void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszArgs, int cch);

            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);

            void GetHotkey(out short pwHotkey);

            void SetHotkey(short wHotkey);

            void GetShowCmd(out int piShowCmd);

            void SetShowCmd(int iShowCmd);

            void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszIconPath, int cch, out int piIcon);

            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);

            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);

            void Resolve(IntPtr hwnd, uint fFlags);

            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
        }
    }
}
