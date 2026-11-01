using LamMotCham.Windows.Software;

namespace LamMotCham.Presentation.Modules;

/// <summary>Một mô-đun của ứng dụng: phạm vi chức năng có phiên bản riêng (QĐ-002).</summary>
/// <param name="Id">Mã mô-đun, dùng trong CHANGELOG và báo lỗi.</param>
/// <param name="Name">Tên đầy đủ.</param>
/// <param name="ShortName">Tên ngắn trên nhãn trang.</param>
/// <param name="Version">Phiên bản mô-đun (SemVer; dữ liệu dùng ngày yyyy.MM.dd).</param>
/// <param name="IconKey">Khóa icon.</param>
/// <param name="Summary">Mô tả một dòng.</param>
/// <param name="IsData">Mô-đun dữ liệu (không chứa mã lệnh).</param>
public sealed record AppModule(string Id, string Name, string ShortName, string Version, string IconKey, string Summary, bool IsData = false)
{
    /// <summary>Nhãn hiển thị ở tiêu đề trang, ví dụ "Mô-đun Phần mềm · v0.2.0".</summary>
    public string Label => $"Mô-đun {ShortName} · v{Version}";

    public string KindText => IsData ? "Dữ liệu" : "Mã nguồn";
}

/// <summary>
/// Nguồn sự thật về tên và phiên bản mô-đun. Khi thêm tính năng hoặc sửa lỗi, tăng phiên bản đúng mô-đun ở đây
/// và ghi CHANGELOG.md (xem docs/decisions/QD-002-Cau-Truc-Mo-Dun-Va-Cap-Nhat.md).
/// </summary>
public static class ModuleCatalog
{
    public static AppModule Shell { get; } = new("shell", "Khung ứng dụng", "Khung", "0.12.3", "laptop", "Thanh tiêu đề, điều hướng, hộp thoại, tiến độ chạy");

    public static AppModule Account { get; } = new("account", "Tài khoản & Đăng nhập", "Tài khoản", "0.3.0", "user", "Đăng nhập (hoặc vào thẳng qua Wi-Fi cửa hàng), lấy danh sách ứng dụng, gửi báo cáo cài; phiên chỉ trong lần chạy");

    public static AppModule Overview { get; } = new("overview", "Tổng quan & Gói nhanh", "Tổng quan", "0.7.3", "home", "Thông tin máy, chọn gói thiết lập");

    public static AppModule Software { get; } = new("software", "Phần mềm thiết yếu", "Phần mềm", "0.7.1", "download", "Danh sách từ Web Admin; ưu tiên link có chữ ký số (tải trước khi đang cài), WinGet dự phòng; xác minh sau khi cài");

    public static AppModule Windows { get; } = new("windows", "Cài đặt & Tối ưu Windows", "Windows", "0.8.0", "gear", "Thiết lập Windows có hoàn tác, gồm tắt BitLocker");

    public static AppModule Disk { get; } = new("disk", "Ổ đĩa & Tài khoản", "Ổ đĩa", "0.6.2", "disk", "Ổ đĩa, chia ổ, đổi tên máy, sửa tài khoản");

    public static AppModule Cleanup { get; } = new("cleanup", "Dọn dẹp & Hiệu năng", "Dọn dẹp", "0.4.1", "sparkle", "Quét và xem trước trước khi xóa");

    public static AppModule Handover { get; } = new("handover", "Kiểm tra & Bàn giao", "Bàn giao", "0.5.1", "clipboard", "Kiểm tra máy, thử phần cứng, báo cáo");

    public static AppModule Engine { get; } = new("engine", "Lõi điều phối tác vụ", "Lõi", "0.4.0", "listcheck", "Hàng đợi, xác minh, nhật ký");

    public static AppModule Modules { get; } = new("modules", "Mô-đun & Cập nhật", "Cập nhật", "0.3.0", "refresh", "Phiên bản các mô-đun");

    public static AppModule Catalog { get; } = new("catalog", "Danh mục phần mềm", "Danh mục", SoftwareCatalog.DataVersion, "list", "Danh mục dự phòng, logo ứng dụng, danh sách hãng tin cậy", IsData: true);

    public static IReadOnlyList<AppModule> All { get; } = [Shell, Account, Overview, Software, Windows, Disk, Cleanup, Handover, Engine, Modules, Catalog];

    /// <summary>Số mô-đun mã nguồn (không tính dữ liệu).</summary>
    public static int CodeModuleCount => All.Count(m => !m.IsData);
}
