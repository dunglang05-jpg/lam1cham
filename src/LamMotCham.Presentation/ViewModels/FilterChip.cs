using LamMotCham.Presentation.Mvvm;

namespace LamMotCham.Presentation.ViewModels;

/// <summary>Nút lọc dạng viên (thiết kế canvas: .pill) — dùng chung cho các trang có bộ lọc nhóm.</summary>
public sealed class FilterChip(string label, Func<string> current, Action<string> select) : ObservableObject
{
    public string Label { get; } = label;

    public bool IsSelected
    {
        get => string.Equals(current(), Label, StringComparison.Ordinal);
        set
        {
            if (value)
            {
                select(Label);
            }
        }
    }

    internal void Refresh() => OnPropertyChanged(nameof(IsSelected));
}
