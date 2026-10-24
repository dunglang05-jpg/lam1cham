# Làm 1 Chạm

**Một chạm, sẵn sàng sử dụng.** Ứng dụng Windows portable (một file EXE, không cần cài) giúp kỹ thuật viên thiết lập và kiểm tra laptop mới trước khi bàn giao.

Bản hiện tại: **1.0.0** ([QĐ-004](docs/decisions/QD-004-Phat-Hanh-1.0.0.md)). Chưa ký số, chưa có Pro. Tải EXE ở mục [Releases](../../releases) hoặc https://dunglam.top/download. Đăng nhập dùng tài khoản của Web Admin (dunglam.top).

Ứng dụng chia thành các **mô-đun** có phiên bản riêng (trang "Mô-đun & Cập nhật" trong app). Quy tắc thêm tính năng và tăng phiên bản: [QĐ-002](docs/decisions/QD-002-Cau-Truc-Mo-Dun-Va-Cap-Nhat.md). Thay đổi theo từng bản: [CHANGELOG.md](CHANGELOG.md).

## Chức năng trong bản này

| Trang | Làm gì | Ghi chú an toàn |
|---|---|---|
| Tổng quan | Đọc thông tin máy (Windows, kích hoạt, RAM, ổ đĩa, mạng, WinGet), chọn gói thao tác nhanh: Thiết lập đầy đủ / Chỉ cài phần mềm / Chỉ tối ưu Windows / Chỉ kiểm tra | Chỉ đọc cho đến khi bấm Bắt đầu và xác nhận |
| Cài phần mềm | 10 gói qua WinGet: Chrome, Brave, UniKey, 7-Zip, Adobe Reader, SumatraPDF, VLC, Microsoft 365, Zalo, Zoom | Chỉ ID đã duyệt; tự bỏ qua gói đã có; chỉ báo **Thành công** khi `winget list` thấy gói sau cài; Hủy không ép tắt trình cài đặt |
| Thiết lập Windows | Múi giờ, đồng bộ giờ, hiện đuôi file, Explorer mở "This PC", icon desktop, taskbar trái, tắt quảng cáo/gợi ý, thời gian tắt màn hình/ngủ, tắt BitLocker (chọn sẵn) | Lưu giá trị cũ để hoàn tác; **không** tắt Defender, Tường lửa, Windows Update; tắt BitLocker chỉ chạy sau khi tick "khách đồng ý" trong hộp xác nhận, không đọc khóa khôi phục |
| Ổ đĩa và tài khoản | Danh sách ổ, sức khỏe ổ, trạng thái BitLocker, chia ổ đĩa, đổi tên máy | Chia ổ chỉ sau khi xem trước và xác nhận riêng; không định dạng hay xóa phân vùng/tài khoản |
| Dọn dẹp | Temp người dùng, Temp Windows, bộ nhớ đệm thumbnail, Thùng rác | Quét và xem trước trước khi xóa; Thùng rác không chọn sẵn và có cảnh báo riêng |
| Kiểm tra bàn giao | 7 điều kiện bàn giao, kiểm tra hệ thống, phần cứng thử tay (camera, loa, mic, bàn phím, touchpad, cổng) | Báo cáo HTML/JSON lưu tại máy, không tự tải lên |

Dữ liệu ứng dụng: `%LOCALAPPDATA%\LamMotCham\` (Logs, Reports, Rollback). Log đã che mật khẩu, token, khóa BitLocker, product key.

**Đăng nhập (mô-đun `account`).** Mở phần mềm là vào màn hình đăng nhập, dùng tài khoản của Web Admin (dunglam.top). Nhân viên mới bấm **Đăng ký tài khoản mới** rồi chờ quản trị viên duyệt ở trang Admin › Tài khoản. Phiên chỉ giữ trong lần chạy: thoát phần mềm là đăng xuất, không lưu gì trên máy.

## Lấy file EXE

**Cách 1 — GitHub Actions (khuyến nghị).** Mỗi lần push lên `main`, workflow `ci` chạy trên `windows-latest`, build + test rồi đính kèm artifact `LamMotCham-win-x64` (gồm `LamMotCham.exe` và `.sha256`).

**Phát hành.** Gắn tag `vX.Y.Z` lên commit đã qua CI trên `main` (phiên bản trong `Directory.Build.props` phải trùng tag): workflow `ci` build lại từ tag rồi tạo GitHub Release kèm `LamMotCham.exe`, `.sha256` và ghi chú từ `docs/release/X.Y.Z.md`.

**Khóa máy chủ đăng nhập.** EXE cần khóa truy cập `api.php` (giá trị `API_KEY` trong `config.php` trên hosting). Khóa không nằm trong mã nguồn: đặt secret **`LMC_API_KEY`** ở *Settings › Secrets and variables › Actions* của repo để bản build từ GitHub Actions có khóa. Thiếu khóa thì vẫn build được nhưng màn hình đăng nhập báo "chưa được cấu hình máy chủ đăng nhập".

**Cách 2 — tự build trên Windows.** Cài [.NET SDK 10](https://dotnet.microsoft.com/download/dotnet/10.0), đặt biến môi trường `LMC_API_KEY` (khóa máy chủ, xem trên), rồi chạy:

```cmd
build.cmd
```

Kết quả: `artifacts\publish\win-x64\LamMotCham.exe` (self-contained, khoảng 70–90 MB, chạy trên Windows 10 2004+ / Windows 11 x64).

## Lần chạy đầu

- EXE chưa ký số nên SmartScreen sẽ cảnh báo: chọn **More info → Run anyway**. Chỉ dùng trên máy thử nghiệm cho đến khi có bản ký số.
- Ứng dụng mở ở quyền người dùng. Các tác vụ cần quản trị (cài cho toàn máy, đồng bộ giờ, đổi tên máy, dọn Temp Windows) sẽ nhắc bấm **Nâng quyền…** ở thanh trên.
- Cần WinGet (App Installer) cho trang Cài phần mềm. Nếu thiếu, ứng dụng hiển thị nút mở Microsoft Store.

## Cấu trúc

```
src/
  LamMotCham.Core          Hợp đồng tác vụ, điều phối hàng đợi, log, báo cáo (không phụ thuộc Windows)
  LamMotCham.Windows       Tác vụ Windows: WinGet, registry, giờ, nguồn, dọn dẹp, đọc thông tin máy
  LamMotCham.Presentation  ViewModel (MVVM, không phụ thuộc WPF — build và test được ở mọi nơi)
  LamMotCham.App           WPF: XAML, theme, icon, control; đóng gói EXE
tests/
  LamMotCham.Core.Tests    Điều phối, retry, timeout, hủy, phụ thuộc, che dữ liệu nhạy cảm
  LamMotCham.Windows.Tests Danh mục, ánh xạ mã lỗi WinGet, đánh giá kết quả kiểm tra
docs/                      Kế hoạch MVP, ADR, mẫu đặc tả tác vụ, quyết định tạm thời
```

Quy tắc dự án: xem tài liệu 00–06 trong Project. Mỗi tác vụ theo chu trình **Điều kiện → Thực hiện → Xác minh**; mã thoát 0 không đủ để báo thành công.

## Giấy phép bên thứ ba

Xem [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
