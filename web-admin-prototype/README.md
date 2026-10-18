# Web Admin Prototype — Làm 1 Chạm

Đây là **prototype giao diện tĩnh** để duyệt UX trước khi triển khai Web Admin thật.

## Mở prototype
Mở trực tiếp `index.html` bằng Chrome, Edge hoặc Firefox. Không cần cài package hay chạy server.

## Những gì có thể thử
- Chuyển giữa Tổng quan, Thiết bị, Danh mục phần mềm, Feature flags, Phát hành, Người dùng & quyền, Pro & giấy phép, Nhật ký kiểm toán và Cấu hình.
- Lọc bảng thiết bị/catalog/audit bằng ô tìm kiếm tại từng trang.
- Bật/tắt switch feature flag để xem trạng thái giao diện.
- Dùng nút thao tác để xem thông báo trạng thái demo.

## Giới hạn
- Dữ liệu đều là ví dụ tĩnh; không có API, cơ sở dữ liệu, đăng nhập, phân quyền thật, lưu trạng thái hay thanh toán.
- Không gửi dữ liệu đến máy chủ và không điều khiển client desktop.
- Không dùng prototype làm hệ thống quản trị production.

## Trước khi triển khai thật
Cần xác định stack web/backend, xác thực và RBAC/MFA, schema thiết bị/catalog, feature-flag policy, audit log, phát hành có chữ ký, và entitlement Pro do backend xác minh. Feature flag chỉ được bật tính năng đã có trong client, không tải/chạy script tùy ý.
