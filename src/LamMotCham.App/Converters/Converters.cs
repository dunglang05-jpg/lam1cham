using System.Collections;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LamMotCham.App.Converters;

/// <summary>Khóa icon (giống bản thiết kế) → ảnh Fluent màu đóng gói trong ứng dụng.</summary>
public sealed class IconConverter : IValueConverter
{
    private static readonly Dictionary<string, string> Files = new(StringComparer.OrdinalIgnoreCase)
    {
        ["home"] = "home",
        ["gear"] = "settings",
        ["download"] = "apps",
        ["disk"] = "database",
        ["sparkle"] = "wrench_screwdriver",
        ["clipboard"] = "clipboard_task",
        ["laptop"] = "laptop",
        ["listcheck"] = "text_bullet_list_square",
        ["clock"] = "history",
        ["globe"] = "globe",
        ["doc"] = "document_text",
        ["keyboard"] = "text_edit_style",
        ["archive"] = "document_folder",
        ["play"] = "headphones",
        ["grid"] = "document_edit",
        ["chat"] = "chat",
        ["video"] = "video",
        ["refresh"] = "arrow_sync",
        ["monitor"] = "paint_brush",
        ["lock"] = "lock_shield",
        ["bolt"] = "arrow_trending_lines",
        ["wifi"] = "wifi",
        ["bluetooth"] = "phone_laptop",
        ["speaker"] = "headphones",
        ["mic"] = "mic",
        ["camera"] = "camera",
        ["touchpad"] = "laptop",
        ["plug"] = "link",
        ["badge"] = "certificate",
        ["chip"] = "puzzle_piece",
        ["cpu"] = "gauge",
        ["battery"] = "gauge",
        ["shield"] = "shield_checkmark",
        ["user"] = "person",
        ["pie"] = "data_pie",
        ["trash"] = "dismiss_circle",
        ["broom"] = "drafts",
        ["report"] = "data_bar_vertical_ascending",
        ["folder"] = "document_folder",
        ["terminal"] = "code",
        ["store"] = "building_store",
        ["search"] = "search_sparkle",
        ["list"] = "list_bar",
        ["eye"] = "content_view",
        ["xcircle"] = "error_circle",
        ["check"] = "checkmark_circle",
        ["alert"] = "warning",
        ["info"] = "lightbulb",
        ["memory"] = "data_area",
        ["external"] = "link",
        ["power"] = "arrow_sync",
        ["puzzle"] = "puzzle_piece",
        ["history"] = "history",
    };

    private static readonly Dictionary<string, ImageSource> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static ImageSource? Get(string? key)
    {
        if (string.IsNullOrEmpty(key) || !Files.TryGetValue(key, out var file))
        {
            file = "checkmark_circle";
        }

        if (Cache.TryGetValue(file, out var cached))
        {
            return cached;
        }

        var image = new BitmapImage();
        image.BeginInit();
        image.UriSource = new Uri($"pack://application:,,,/Assets/Icons/png/{file}.png", UriKind.Absolute);
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.EndInit();
        image.Freeze();
        Cache[file] = image;
        return image;
    }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Get(value as string ?? parameter as string);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>true → Visible. Tham số "invert" để đảo.</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var flag = value is true;
        if (parameter is string s && s == "invert")
        {
            flag = !flag;
        }

        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Chuỗi rỗng, null, hoặc danh sách rỗng → Collapsed. Tham số "invert" để đảo.</summary>
public sealed class EmptyToCollapsedConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var empty = value switch
        {
            null => true,
            string s => string.IsNullOrWhiteSpace(s),
            ICollection c => c.Count == 0,
            IEnumerable e => !e.GetEnumerator().MoveNext(),
            int i => i == 0,
            _ => false,
        };
        if (parameter is string p && p == "invert")
        {
            empty = !empty;
        }

        return empty ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>true → <see cref="TrueBrush"/>, ngược lại → <see cref="FalseBrush"/>.</summary>
public sealed class BoolToBrushConverter : IValueConverter
{
    public Brush TrueBrush { get; set; } = Brushes.Transparent;

    public Brush FalseBrush { get; set; } = Brushes.Transparent;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? TrueBrush : FalseBrush;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>true → <see cref="TrueText"/>, ngược lại → <see cref="FalseText"/>.</summary>
public sealed class BoolToTextConverter : IValueConverter
{
    public string TrueText { get; set; } = "";

    public string FalseText { get; set; } = "";

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? TrueText : FalseText;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
