# UI refresh — Làm 1 Chạm

Nhánh `ui/redesign-refresh` cập nhật giao diện desktop WPF và thêm prototype Web Admin để review UX.

## Desktop app
- Thống nhất palette xanh dương, text và bề mặt card.
- Làm rõ phần giới thiệu trong Dashboard theo luồng chuẩn bị laptop.
- Cải thiện danh sách phần mềm: heading/description rõ hơn, icon tile, selected state, hover state, cảnh báo cho dòng gặp vấn đề.
- Thêm entry “Khám phá Pro” trên top bar ở chế độ cửa sổ rộng và dialog giới thiệu Pro.
- Dialog Pro hiện là trang giới thiệu tính năng dự kiến. Không có checkout, không xác minh giấy phép và không bật khóa tính năng.

## Web Admin
Prototype tại `web-admin-prototype/index.html`. Có các trang mẫu:
- Tổng quan/KPI và hoạt động
- Thiết bị
- Danh mục phần mềm
- Feature flags
- Phát hành & cập nhật
- Người dùng & quyền
- Pro & giấy phép
- Nhật ký kiểm toán
- Cấu hình

Prototype dùng HTML/CSS/JavaScript thuần, mở trực tiếp trong trình duyệt. Dữ liệu và thao tác đều demo, chưa kết nối backend.

## Nguyên tắc không được phá vỡ
- Không báo tính năng hoạt động khi chưa có backend tương ứng.
- Không tin trạng thái Pro do client tự khai; entitlement phải xác minh phía server.
- Không phân phối mã thực thi tùy ý qua feature flags.
- Thay đổi quản trị phải có RBAC và audit log.
- Không tắt Defender, firewall hoặc Windows Update theo mặc định.
- Không đưa bí mật, thông tin cá nhân không cần thiết hoặc khóa khôi phục BitLocker vào log.

## Kiểm thử cần chạy
CI của repository chạy build/test Windows và publish portable EXE khi có pull request. Sau khi merge, cần mở ứng dụng trên Windows để đánh giá trực quan ở 1366×768, 1440×900, scale 100%, 125% và 150%. Prototype Web Admin cần review riêng trên desktop và mobile.
