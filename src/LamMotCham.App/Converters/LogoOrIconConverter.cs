using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LamMotCham.App.Converters;

/// <summary>
/// Ưu tiên logo thật của ứng dụng (Assets/Logos/{LogoKey}.png). Khi gói chưa có logo thì dùng icon theo danh mục (IconKey).
/// Giá trị đầu vào: [0] LogoKey (có thể null), [1] IconKey.
/// </summary>
public sealed class LogoOrIconConverter : IMultiValueConverter
{
    private static readonly Dictionary<string, ImageSource> LogoCache = new(StringComparer.Ordinal);

    public object? Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        var logoKey = values.Length > 0 ? values[0] as string : null;
        var iconKey = values.Length > 1 ? values[1] as string : null;

        if (!string.IsNullOrEmpty(logoKey) && LoadLogo(logoKey) is { } logo)
        {
            return logo;
        }

        return IconConverter.Get(iconKey);
    }

    public object?[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static ImageSource? LoadLogo(string key)
    {
        if (LogoCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        // Chỉ chấp nhận khóa chữ thường/số để không ghép đường dẫn lạ.
        if (!key.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9'))
        {
            return null;
        }

        var uri = new Uri($"pack://application:,,,/Assets/Logos/{key}.png", UriKind.Absolute);
        var image = new BitmapImage(uri);
        image.Freeze();
        LogoCache[key] = image;
        return image;
    }
}
