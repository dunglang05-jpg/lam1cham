using System.Globalization;
using LamMotCham.Core.Tasks;
using LamMotCham.Windows.Infrastructure;
using LamMotCham.Windows.Native;
using Microsoft.Win32;

namespace LamMotCham.Windows.Config;

/// <summary>Một giá trị DWORD trong HKEY_CURRENT_USER.</summary>
public sealed record RegistryDword(string SubKey, string Name, int Desired)
{
    internal string StoreKey => SubKey + "|" + Name;
}

/// <summary>
/// Đặt một nhóm giá trị DWORD cho người dùng hiện tại. Lưu giá trị cũ trước khi đổi, đọc lại để xác minh
/// và hoàn tác được. Không động vào HKEY_LOCAL_MACHINE.
/// </summary>
public sealed class RegistryValuesTask : ITaskModule, IDescribesCurrentState
{
    private readonly IReadOnlyList<RegistryDword> _values;
    private readonly RollbackStore _store;
    private readonly Func<IReadOnlyList<int?>, string> _describe;
    private readonly Func<PreconditionResult>? _precondition;

    public RegistryValuesTask(
        TaskDescriptor descriptor,
        IReadOnlyList<RegistryDword> values,
        RollbackStore store,
        Func<IReadOnlyList<int?>, string> describe,
        Func<PreconditionResult>? precondition = null)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(describe);
        if (values.Count == 0)
        {
            throw new ArgumentException("Cần ít nhất một giá trị.", nameof(values));
        }

        Descriptor = descriptor;
        _values = values;
        _store = store;
        _describe = describe;
        _precondition = precondition;
    }

    public TaskDescriptor Descriptor { get; }

    public Task<PreconditionResult> CheckPreconditionsAsync(TaskContext context, CancellationToken cancellationToken)
    {
        if (_precondition?.Invoke() is { Outcome: not PreconditionOutcome.Met } blocked)
        {
            return Task.FromResult(blocked);
        }

        return Task.FromResult(AllMatch() ? PreconditionResult.AlreadyInDesiredState("Đã đúng sẵn.") : PreconditionResult.Met());
    }

    public Task<ExecuteResult> ExecuteAsync(TaskContext context, CancellationToken cancellationToken)
    {
        var old = _values.ToDictionary(v => v.StoreKey, v => Read(v)?.ToString(CultureInfo.InvariantCulture));
        _store.SaveIfAbsent(Descriptor.Id, old);

        foreach (var v in _values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var key = Registry.CurrentUser.CreateSubKey(v.SubKey, writable: true);
            key.SetValue(v.Name, v.Desired, RegistryValueKind.DWord);
        }

        NativeMethods.NotifyShellSettingsChanged();
        return Task.FromResult(ExecuteResult.Completed());
    }

    public Task<VerifyResult> VerifyAsync(TaskContext context, CancellationToken cancellationToken)
    {
        var wrong = _values.Where(v => Read(v) != v.Desired).Select(v => v.Name).ToList();
        return Task.FromResult(wrong.Count == 0
            ? VerifyResult.Passed("Đã đổi và đọc lại khớp.")
            : VerifyResult.Failed("Giá trị chưa đúng sau khi ghi: " + string.Join(", ", wrong) + "."));
    }

    public Task<VerifyResult> RollbackAsync(TaskContext context, CancellationToken cancellationToken)
    {
        var saved = _store.Load(Descriptor.Id);
        if (saved is null)
        {
            return Task.FromResult(VerifyResult.Inconclusive("Không có giá trị cũ đã lưu để hoàn tác."));
        }

        foreach (var v in _values)
        {
            using var key = Registry.CurrentUser.CreateSubKey(v.SubKey, writable: true);
            if (saved.TryGetValue(v.StoreKey, out var text) && text is not null &&
                int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var oldValue))
            {
                key.SetValue(v.Name, oldValue, RegistryValueKind.DWord);
            }
            else
            {
                key.DeleteValue(v.Name, throwOnMissingValue: false);
            }
        }

        NativeMethods.NotifyShellSettingsChanged();
        _store.Clear(Descriptor.Id);
        return Task.FromResult(VerifyResult.Passed("Đã khôi phục giá trị cũ."));
    }

    public Task<string> DescribeCurrentStateAsync(CancellationToken cancellationToken) =>
        Task.FromResult("Hiện tại: " + _describe([.. _values.Select(Read)]));

    private bool AllMatch() => _values.All(v => Read(v) == v.Desired);

    private static int? Read(RegistryDword v)
    {
        using var key = Registry.CurrentUser.OpenSubKey(v.SubKey, writable: false);
        return key?.GetValue(v.Name) is int i ? i : null;
    }
}
