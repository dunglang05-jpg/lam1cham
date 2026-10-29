using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LamMotCham.Windows.Software;

/// <summary>Kết quả đọc danh sách ứng dụng từ Web Admin.</summary>
/// <param name="Packages">Ứng dụng đã kiểm tra (kể cả ứng dụng hiện mờ kèm lý do).</param>
/// <param name="Warnings">Ghi chú cho log: mục bị bỏ qua, trường không hợp lệ.</param>
public sealed record RemoteCatalogResult(IReadOnlyList<SoftwarePackage> Packages, IReadOnlyList<string> Warnings);

/// <summary>
/// Đọc mảng "apps" của api.php (listApps, định dạng ứng dụng của Web Admin — QĐ-006) thành danh mục phần mềm.
/// Máy chủ đã kiểm tra một lần (validate_app); ở đây kiểm tra lại toàn bộ vì dữ liệu từ mạng không được tin cậy:
/// chỉ link https, mã WinGet an toàn, tham số cài chỉ gồm ký tự an toàn, đường dẫn nhận biết chỉ trong thư mục cho phép.
/// Mục sai được bỏ hoặc hiện mờ kèm lý do; không bao giờ làm hỏng cả danh sách.
/// </summary>
public static partial class RemoteCatalog
{
    public const int MaxApps = 100;
    public const int MaxVariants = 12;

    private const int MaxJsonChars = 2 * 1024 * 1024;

    /// <summary>Gói WinGet có logo đóng gói sẵn trong ứng dụng.</summary>
    private static readonly Dictionary<string, string> LogoByWinGetId = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Google.Chrome"] = "chrome",
        ["Brave.Brave"] = "brave",
        ["Adobe.Acrobat.Reader.64-bit"] = "acrobat",
        ["7zip.7zip"] = "7zip",
        ["VideoLAN.VLC"] = "vlc",
        ["Microsoft.Office"] = "m365",
        ["VNGCorp.Zalo"] = "zalo",
        ["Zoom.Zoom"] = "zoom",
    };

    private static readonly Dictionary<string, string> LogoByName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Microsoft 365"] = "m365",
        ["Microsoft Office"] = "m365",
    };

    private static readonly string[] AllowedPathRoots =
    [
        "%ProgramFiles%\\", "%ProgramFiles(x86)%\\", "%ProgramW6432%\\", "%LocalAppData%\\", "%AppData%\\", "%ProgramData%\\",
    ];

    public static RemoteCatalogResult Parse(string? appsJson)
    {
        var warnings = new List<string>();
        var list = new List<SoftwarePackage>();
        if (string.IsNullOrWhiteSpace(appsJson) || appsJson.Length > MaxJsonChars)
        {
            warnings.Add("Danh sách ứng dụng rỗng hoặc quá lớn.");
            return new RemoteCatalogResult(list, warnings);
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(appsJson, new JsonDocumentOptions { MaxDepth = 16 });
        }
        catch (JsonException)
        {
            warnings.Add("Danh sách ứng dụng không phải JSON hợp lệ.");
            return new RemoteCatalogResult(list, warnings);
        }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                warnings.Add("Danh sách ứng dụng không phải mảng.");
                return new RemoteCatalogResult(list, warnings);
            }

            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (list.Count >= MaxApps)
                {
                    warnings.Add($"Chỉ dùng {MaxApps} ứng dụng đầu tiên.");
                    break;
                }

                var pkg = ParseOne(el, warnings);
                if (pkg is null)
                {
                    continue;
                }

                if (!names.Add(pkg.Name))
                {
                    warnings.Add($"Bỏ ứng dụng trùng tên: {pkg.Name}.");
                    continue;
                }

                var key = pkg.Key;
                for (var i = 2; !keys.Add(key); i++)
                {
                    key = string.Create(CultureInfo.InvariantCulture, $"{pkg.Key}-{i}");
                }

                list.Add(key == pkg.Key ? pkg : pkg with { Key = key });
            }
        }

        return new RemoteCatalogResult(list, warnings);
    }

    private static SoftwarePackage? ParseOne(JsonElement el, List<string> warnings)
    {
        if (el.ValueKind != JsonValueKind.Object)
        {
            warnings.Add("Bỏ một mục không phải đối tượng.");
            return null;
        }

        var name = Str(el, "Name");
        if (!NameRegex().IsMatch(name))
        {
            warnings.Add("Bỏ một ứng dụng có tên không hợp lệ.");
            return null;
        }

        var problems = new List<string>();
        var kind = Str(el, "Kind") switch
        {
            "Exe" => InstallKind.Exe,
            "Msi" => InstallKind.Msi,
            "Zip" => InstallKind.Zip,
            "Web" => InstallKind.Web,
            _ => (InstallKind?)null,
        };
        if (kind is null)
        {
            problems.Add("loại cài đặt không hợp lệ");
        }

        var winget = Str(el, "WingetId");
        string? wingetId = null;
        if (winget.Length > 0)
        {
            if (SoftwareCatalog.IsSafeId(winget))
            {
                wingetId = winget;
            }
            else
            {
                warnings.Add($"{name}: mã WinGet không hợp lệ, bỏ qua.");
            }
        }

        var publisher = Text(el, "Publisher", 80);
        var initial = Str(el, "Initial");
        if (!InitialRegex().IsMatch(initial))
        {
            initial = name[..1].ToUpperInvariant();
        }

        var color = Str(el, "Color");
        if (!ColorRegex().IsMatch(color))
        {
            color = "#3B5BDB";
        }

        var silent = Str(el, "SilentArgs");
        if (!SilentArgsRegex().IsMatch(silent))
        {
            warnings.Add($"{name}: tham số cài không hợp lệ, bỏ link tải.");
            silent = "";
            problems.Add("tham số cài không hợp lệ");
        }

        var direct = HttpsUrl(Str(el, "DirectUrl"));
        var page = HttpsUrl(Str(el, "PageUrl"));
        var regex = Str(el, "PageRegex");
        if (regex.Length == 0 || regex.Length > 300 || !IsValidRegex(regex))
        {
            regex = "";
        }

        var website = HttpsUrl(Str(el, "Website"));
        var zipFolder = Str(el, "ZipFolder");
        var zipExe = Str(el, "ZipExe");
        if (!ZipFolderRegex().IsMatch(zipFolder) || !ZipExeRegex().IsMatch(zipExe))
        {
            zipFolder = "";
            zipExe = "";
        }

        var variants = new List<PackageVariant>();
        if (el.TryGetProperty("Variants", out var vs) && vs.ValueKind == JsonValueKind.Array)
        {
            foreach (var v in vs.EnumerateArray().Take(MaxVariants))
            {
                var label = Str(v, "Label");
                var vkind = Str(v, "Kind") switch { "Exe" => InstallKind.Exe, "Msi" => InstallKind.Msi, "Web" => InstallKind.Web, _ => (InstallKind?)null };
                var url = HttpsUrl(Str(v, "DirectUrl"));
                var args = Str(v, "SilentArgs");
                if (!VariantLabelRegex().IsMatch(label) || vkind is null || url is null || !SilentArgsRegex().IsMatch(args))
                {
                    warnings.Add($"{name}: bỏ một phiên bản không hợp lệ.");
                    continue;
                }

                variants.Add(new PackageVariant(label, vkind.Value, url, args));
            }
        }

        var pkg = new SoftwarePackage
        {
            Key = KeyFor(name, wingetId),
            Name = name,
            Description = Text(el, "Description", 120),
            Category = Text(el, "Category", 30) is { Length: > 0 } c ? c : SoftwareCatalog.OtherCategory,
            WinGetId = wingetId,
            ExpectedPublisher = wingetId is null ? "" : publisher,
            Recommended = el.TryGetProperty("Checked", out var ch) && ch.ValueKind == JsonValueKind.True,
            IconKey = kind == InstallKind.Web ? "globe" : "grid",
            MachineWide = el.TryGetProperty("MachineWide", out var mw) && mw.ValueKind == JsonValueKind.True,
            Initial = initial,
            Color = color,
            Kind = kind ?? InstallKind.Exe,
            DirectUrl = direct,
            PageUrl = regex.Length > 0 ? page : null,
            PageRegex = regex.Length > 0 && page is not null ? regex : null,
            SilentArgs = silent,
            Website = website,
            ZipFolder = zipFolder.Length > 0 ? zipFolder : null,
            ZipExe = zipExe.Length > 0 ? zipExe : null,
            Variants = variants,
            DetectNames = Lines(el, "DetectNames", 80, s => !s.Any(char.IsControl)),
            DetectPaths = Lines(el, "DetectPaths", 260, IsAllowedDetectPath),
        };

        return pkg with { UnavailableReason = problems.Count > 0 ? "Dữ liệu trên Admin chưa đúng: " + string.Join(", ", problems) + "." : Availability(pkg) };
    }

    /// <summary>Lý do không cài được (hiện mờ), hoặc null nếu cài được.</summary>
    internal static string? Availability(SoftwarePackage p)
    {
        if (p.Kind == InstallKind.Web)
        {
            return p.Website is null ? "Thiếu trang web để tạo lối tắt." : null;
        }

        if (p.HasVariants || p.UsesWinGet)
        {
            return null;
        }

        if (p.HasLink)
        {
            return p.Kind == InstallKind.Zip && (p.ZipFolder is null || p.ZipExe is null) ? "Gói ZIP thiếu thư mục cài hoặc file chạy." : null;
        }

        if (p.WinGetId is not null)
        {
            return "Thiếu nhà phát hành WinGet trên Admin nên chưa cài bằng WinGet được.";
        }

        return string.IsNullOrWhiteSpace(p.Description) ? "Chưa có cách cài tự động (không có link tải hay mã WinGet)." : p.Description;
    }

    /// <summary>Khóa ổn định: logo có sẵn nếu là app quen, ngược lại là tên viết không dấu.</summary>
    internal static string KeyFor(string name, string? wingetId)
    {
        if (wingetId is not null && LogoByWinGetId.TryGetValue(wingetId, out var logo))
        {
            return logo;
        }

        if (LogoByName.TryGetValue(name, out var byName))
        {
            return byName;
        }

        var sb = new StringBuilder();
        foreach (var c in name.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            var ch = c is 'đ' or 'Đ' ? 'd' : char.ToLowerInvariant(c);
            if (ch is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                sb.Append(ch);
            }
            else if (sb.Length > 0 && sb[^1] != '-')
            {
                sb.Append('-');
            }
        }

        var key = sb.ToString().Trim('-');
        return key.Length == 0 ? "app" : key;
    }

    internal static Uri? HttpsUrl(string value)
    {
        if (value.Length == 0 || value.Length > 400 || value.Any(c => char.IsWhiteSpace(c) || char.IsControl(c) || c is '"' or '\'' or '<' or '>'))
        {
            return null;
        }

        return Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && uri.Host.Length > 0 && string.IsNullOrEmpty(uri.UserInfo)
            ? uri
            : null;
    }

    /// <summary>Chỉ cho đường dẫn bắt đầu bằng thư mục cài đặt/ứng dụng chuẩn, không có "..".</summary>
    internal static bool IsAllowedDetectPath(string path) =>
        path.Length >= 3 && !path.Contains("..", StringComparison.Ordinal) && !path.Any(c => char.IsControl(c) || c is '"' or '<' or '>' or '|' or '*' or '?') &&
        AllowedPathRoots.Any(r => path.StartsWith(r, StringComparison.OrdinalIgnoreCase));

    private static bool IsValidRegex(string pattern)
    {
        try
        {
            _ = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static string Str(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? (v.GetString() ?? "").Trim() : "";

    private static string Text(JsonElement el, string name, int max)
    {
        var s = Str(el, name);
        return s.Length <= max && !s.Any(c => char.IsControl(c) || c is '<' or '>' or '"') ? s : "";
    }

    private static List<string> Lines(JsonElement el, string name, int max, Func<string, bool> ok)
    {
        var list = new List<string>();
        if (el.TryGetProperty(name, out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var x in arr.EnumerateArray().Take(10))
            {
                var s = x.ValueKind == JsonValueKind.String ? (x.GetString() ?? "").Trim() : "";
                if (s.Length > 0 && s.Length <= max && ok(s))
                {
                    list.Add(s);
                }
            }
        }

        return list;
    }

    [GeneratedRegex(@"^[\p{L}\p{N}][\p{L}\p{N} .+\-_()]{0,39}\z", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex NameRegex();

    [GeneratedRegex(@"^[\p{L}\p{N}]{1,2}\z", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex InitialRegex();

    [GeneratedRegex(@"^#[0-9A-Fa-f]{6}\z", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex ColorRegex();

    [GeneratedRegex(@"^[A-Za-z0-9 /\-_=.]{0,100}\z", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    internal static partial Regex SilentArgsRegex();

    [GeneratedRegex(@"^(?:[A-Za-z0-9][A-Za-z0-9 ._-]{0,40})?\z", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex ZipFolderRegex();

    [GeneratedRegex(@"^(?:[A-Za-z0-9][A-Za-z0-9 ._-]{0,40}\.exe)?\z", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 100)]
    private static partial Regex ZipExeRegex();

    [GeneratedRegex(@"^[\p{L}\p{N}][\p{L}\p{N} .+\-_()/]{0,59}\z", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex VariantLabelRegex();
}
