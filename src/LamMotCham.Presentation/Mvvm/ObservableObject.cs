using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace LamMotCham.Presentation.Mvvm;

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(name);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>Báo mọi lệnh đánh giá lại CanExecute (thay cho CommandManager của WPF).</summary>
public static class Commands
{
    public static event EventHandler? Invalidated;

    public static void InvalidateAll() => Invalidated?.Invoke(null, EventArgs.Empty);
}

public sealed class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Func<object?, bool>? _canExecute;

    public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
        if (canExecute is not null)
        {
            Commands.Invalidated += (_, _) => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
        : this(_ => execute(), canExecute is null ? null : _ => canExecute())
    {
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

    public void Execute(object? parameter)
    {
        if (CanExecute(parameter))
        {
            _execute(parameter);
        }
    }
}

/// <summary>Lệnh bất đồng bộ: không cho bấm lại khi đang chạy; lỗi được chuyển cho handler chung.</summary>
public sealed class AsyncCommand : ICommand
{
    private readonly Func<Task> _execute;
    private readonly Func<bool>? _canExecute;
    private bool _running;

    public AsyncCommand(Func<Task> execute, Func<bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
        Commands.Invalidated += (_, _) => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }

    public static Action<Exception>? ErrorHandler { get; set; }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => !_running && (_canExecute?.Invoke() ?? true);

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter))
        {
            return;
        }

        _running = true;
        Commands.InvalidateAll();
        try
        {
            await ExecuteAsync();
        }
#pragma warning disable CA1031 // Lỗi không mong đợi phải hiện cho người dùng thay vì làm sập ứng dụng.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            ErrorHandler?.Invoke(ex);
        }
        finally
        {
            _running = false;
            Commands.InvalidateAll();
        }
    }

    public Task ExecuteAsync() => _execute();
}
