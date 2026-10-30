namespace LamMotCham.Windows.Software;

/// <summary>
/// Danh sách hãng tin cậy đóng sẵn trong phần mềm (QĐ-006, PO chọn "Chữ ký số + danh sách hãng").
/// File tải bằng link chỉ được chạy khi có chữ ký số Authenticode hợp lệ và tên tổ chức (O) hoặc tên chung (CN)
/// của chứng chỉ ký khớp một mục dưới đây. Web Admin không thêm được hãng: thêm hãng mới phải ra bản phần mềm mới.
/// Thuộc mô-đun dữ liệu "catalog" — đổi danh sách thì tăng <see cref="SoftwareCatalog.DataVersion"/>.
/// </summary>
public static class TrustedPublishers
{
    private static readonly string[] Names =
    [
        "Microsoft Corporation",
        "Google LLC",
        "Brave Software, Inc.",
        "Adobe Inc.",
        "Adobe Systems Incorporated",
        "Foxit Software Inc.",
        "Foxit Software Incorporated",
        "Zoom Video Communications, Inc.",
        "Zoom Communications, Inc.",
        "win.rar GmbH",
        "VideoLAN",
        "AO Kaspersky Lab",
        "Kaspersky Lab JSC",
        "ESET, spol. s r.o.",
        "Viber Media S.à r.l.",
        "Viber Media S.a r.l.",
        "Rakuten Viber",
        "DUC FABULOUS CO.,LTD",
        "VNG Corporation",
        "Zhuhai Kingsoft Office Software Co., Ltd.",

        // Tác giả UniKey (chứng chỉ cá nhân) — PO duyệt 30/10/2026 sau kiểm tra tự động trên CI.
        "PHAM KIM LONG",
    ];

    private static readonly HashSet<string> Normalized = new(Names.Select(Normalize), StringComparer.Ordinal);

    public static IReadOnlyList<string> All => Names;

    /// <summary>Tên hãng (O hoặc CN của chứng chỉ ký) có trong danh sách tin cậy không.</summary>
    public static bool IsTrusted(string? signerName) => !string.IsNullOrWhiteSpace(signerName) && Normalized.Contains(Normalize(signerName));

    /// <summary>So khớp không phân biệt hoa thường, bỏ khoảng trắng thừa và dấu chấm/phẩy cuối.</summary>
    internal static string Normalize(string value)
    {
        var parts = value.Trim().Trim('"').Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', parts).TrimEnd('.', ',').ToUpperInvariant();
    }
}
