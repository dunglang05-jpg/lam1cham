# QĐ-002 — Cấu trúc mô-đun và quy tắc cập nhật

- Trạng thái: **Đang áp dụng** từ 18/10/2026 (bản 0.2.0).
- Căn cứ: PO yêu cầu thiết kế theo mô-đun riêng để sau này cập nhật từng phần, và mỗi khi có tính năng mới thì cập nhật đúng mô-đun. Giao diện tham chiếu: canvas thiết kế v3.
- Liên quan: tài liệu 02 (kiến trúc, ranh giới tin cậy), 05 (cập nhật), QĐ-001.

## 1. Danh mục mô-đun

Nguồn sự thật về tên và phiên bản: `src/LamMotCham.Presentation/Modules/ModuleCatalog.cs`. Trang **Mô-đun & Cập nhật** trong ứng dụng đọc từ đây.

| Id | Mô-đun | File thuộc mô-đun |
|---|---|---|
| `shell` | Khung ứng dụng | `App/MainWindow.xaml`, `App/Themes/Theme.xaml`, `App/Views/RunOverlay.xaml`, `Presentation/ViewModels/MainViewModel.cs` |
| `overview` | Tổng quan & Gói nhanh | `App/Views/Pages/OverviewPage.xaml`, `Presentation/ViewModels/OverviewViewModel.cs`, `MachineViewModel.cs`, `Windows/Probe/*` |
| `software` | Phần mềm thiết yếu | `App/Views/Pages/SoftwarePage.xaml`, `Presentation/ViewModels/SoftwareViewModel.cs`, `Windows/Software/*` (trừ danh mục) |
| `windows` | Cài đặt & Tối ưu Windows | `App/Views/Pages/WindowsPage.xaml`, `Presentation/ViewModels/WindowsViewModel.cs`, `Windows/Config/*` (trừ đổi tên máy) |
| `disk` | Ổ đĩa & Tài khoản | `App/Views/Pages/DiskPage.xaml`, `Presentation/ViewModels/DiskViewModel.cs`, `Windows/Config/RenameComputerTask.cs` |
| `cleanup` | Dọn dẹp & Hiệu năng | `App/Views/Pages/CleanupPage.xaml`, `Presentation/ViewModels/CleanupViewModel.cs`, `Windows/Cleanup/*` |
| `handover` | Kiểm tra & Bàn giao | `App/Views/Pages/HandoverPage.xaml`, `Presentation/ViewModels/HandoverViewModel.cs`, `Windows/Checks/*`, `Core/Reporting/*` |
| `engine` | Lõi điều phối tác vụ | `Core/Orchestration/*`, `Core/Tasks/*`, `Core/Logging/*`, `Core/Processes/*`, `Core/Resources/*` |
| `modules` | Mô-đun & Cập nhật | `App/Views/Pages/ModulesPage.xaml`, `Presentation/ViewModels/ModulesViewModel.cs`, `Presentation/Modules/*` |
| `catalog` *(dữ liệu)* | Danh mục phần mềm | `Windows/Software/SoftwareCatalog.cs`, `App/Assets/Logos/*` — phiên bản dạng ngày `yyyy.MM.dd` |

Thành phần dùng chung (không thuộc riêng mô-đun nào): `App/Converters/*`, `App/Controls/*`, `Presentation/Mvvm/*`, `Presentation/Services/*`, icon trong `App/Assets/Icons`. Sửa phần dùng chung thì ghi vào CHANGELOG mục **Dùng chung** và tăng bản vá của `shell`.

## 2. Quy tắc khi thêm tính năng hoặc sửa lỗi

1. Xác định tính năng thuộc mô-đun nào theo bảng trên. Chỉ sửa file của mô-đun đó; nếu buộc phải sửa phần dùng chung, ghi rõ lý do.
2. Tăng phiên bản mô-đun trong `ModuleCatalog.cs` theo SemVer:
   - sửa lỗi, chỉnh chữ, chỉnh giao diện nhỏ → tăng **bản vá** (0.2.0 → 0.2.1);
   - thêm tính năng hoặc tác vụ mới → tăng **bản phụ** (0.2.0 → 0.3.0);
   - đổi hành vi làm hỏng cách dùng cũ → tăng **bản chính**.
   Danh mục phần mềm đổi phiên bản theo ngày cập nhật.
3. Tăng phiên bản ứng dụng (`Version` trong `Directory.Build.props`) — bằng mức tăng lớn nhất trong các mô-đun đã đổi.
4. Ghi `CHANGELOG.md`: một mục cho bản ứng dụng, chia theo mô-đun.
5. Tính năng đủ lớn để có màn hình riêng → tạo **mô-đun mới**: thêm dòng vào `ModuleCatalog`, trang XAML + ViewModel riêng, mục điều hướng, và cập nhật bảng ở mục 1.
6. Cập nhật canvas thiết kế: sửa đúng file mô-đun tương ứng (`Mod*.dc.html`).
7. Build EXE qua GitHub Actions; chỉ báo xong khi CI build và test đạt.

## 3. Cách phân phối bản cập nhật

- Mọi mô-đun mã nguồn đi cùng **một file EXE**. Không tải DLL hay script rời từ máy chủ rồi chạy (tài liệu 02, 05).
- Khi có máy chủ cập nhật (giai đoạn 3): manifest có phiên bản, SHA-256, chữ ký; kiểm tra trước khi áp dụng; không cập nhật khi đang chạy tác vụ; giữ bản cũ để khôi phục.
- Danh mục phần mềm có thể cập nhật riêng dưới dạng **dữ liệu có chữ ký**, chỉ gồm gói đã duyệt — không chứa lệnh.
- Trước khi có máy chủ: trang Mô-đun & Cập nhật chỉ hiển thị phiên bản hiện tại, nói rõ chưa kết nối máy chủ; không hiển thị "có bản mới" giả.
