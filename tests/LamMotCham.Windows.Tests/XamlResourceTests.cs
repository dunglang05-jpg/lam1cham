using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace LamMotCham.Windows.Tests;

/// <summary>
/// Bắt lỗi XAML mà trình biên dịch không thấy, chỉ lộ ra khi chạy ("StaticResourceHolder threw an exception"):
/// tham chiếu tới tài nguyên khai báo sau, hoặc khóa không tồn tại.
/// </summary>
public sealed partial class XamlResourceTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void Theme_only_references_resources_declared_above()
    {
        var theme = XDocument.Load(Path.Combine(AppDir(), "Themes", "Theme.xaml"));
        var declared = new HashSet<string>(StringComparer.Ordinal);
        var problems = new List<string>();

        foreach (var resource in theme.Root!.Elements())
        {
            foreach (var key in References(resource.ToString()))
            {
                if (!declared.Contains(key))
                {
                    problems.Add($"{key} (dùng trong {Name(resource)})");
                }
            }

            if (resource.Attribute(Xaml + "Key")?.Value is { } own)
            {
                declared.Add(own);
            }
        }

        Assert.True(problems.Count == 0, "Theme.xaml dùng tài nguyên trước khi khai báo: " + string.Join(", ", problems));
    }

    [Fact]
    public void Every_static_resource_key_exists()
    {
        var dir = AppDir();
        var shared = new HashSet<string>(Keys(XDocument.Load(Path.Combine(dir, "Themes", "Theme.xaml"))), StringComparer.Ordinal);
        shared.UnionWith(Keys(XDocument.Load(Path.Combine(dir, "App.xaml"))));
        var problems = new List<string>();

        foreach (var file in Directory.EnumerateFiles(dir, "*.xaml", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            var text = File.ReadAllText(file);
            var local = Keys(XDocument.Parse(text)).ToHashSet(StringComparer.Ordinal);
            foreach (var key in References(text).Distinct(StringComparer.Ordinal))
            {
                if (!shared.Contains(key) && !local.Contains(key))
                {
                    problems.Add($"{key} trong {Path.GetFileName(file)}");
                }
            }
        }

        Assert.True(problems.Count == 0, "Khóa tài nguyên không tồn tại: " + string.Join(", ", problems));
    }

    private static IEnumerable<string> Keys(XDocument doc) =>
        doc.Descendants().Select(e => e.Attribute(Xaml + "Key")?.Value).OfType<string>();

    private static IEnumerable<string> References(string xaml) =>
        StaticResourceRegex().Matches(xaml).Select(m => m.Groups[1].Value);

    private static string Name(XElement e) =>
        e.Attribute(Xaml + "Key")?.Value ?? e.Attribute("TargetType")?.Value ?? e.Name.LocalName;

    /// <summary>Thư mục src/LamMotCham.App, tìm ngược từ thư mục chạy test lên tới gốc repo.</summary>
    private static string AppDir()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "LamMotCham.sln")))
            {
                return Path.Combine(dir.FullName, "src", "LamMotCham.App");
            }
        }

        throw new DirectoryNotFoundException("Không tìm thấy LamMotCham.sln từ " + AppContext.BaseDirectory);
    }

    [GeneratedRegex(@"\{StaticResource\s+([A-Za-z0-9_.]+)\s*\}", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex StaticResourceRegex();
}
