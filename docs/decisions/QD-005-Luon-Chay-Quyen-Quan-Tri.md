# QĐ-005 — Luôn chạy quyền quản trị

- Ngày: 24/10/2026 · Người quyết định: PO (Lăng Dũng). Áp dụng từ bản 1.0.1.
- Thay cho: QĐ-001 Q-05 (nâng quyền khi bấm “Nâng quyền…”) và lệch hẳn ADR-002 (UI chạy `asInvoker` + helper riêng).

## Quyết định
- EXE khai báo `requestedExecutionLevel = requireAdministrator`. Mở phần mềm là Windows hỏi UAC **một lần**; chọn Yes thì toàn bộ phần mềm chạy quyền quản trị, không hỏi lại trong lúc chạy.
- Bỏ nút “Nâng quyền quản trị…”, lệnh khởi động lại bằng `runas` và tham số `--after-elevate`.
- Không thể và không được bỏ qua UAC của Windows: đó là lớp bảo vệ của hệ điều hành. Chọn No thì phần mềm không mở.

## Biện pháp giảm rủi ro
- Mở báo cáo, thư mục, trang web, trang Cài đặt Windows qua `explorer.exe` (quyền người dùng) để trình duyệt và ứng dụng khác không chạy kèm quyền quản trị.
- Giữ cơ chế phát hiện chạy nhầm tài khoản (BC-002 P1-3): máy dùng tài khoản thường mà UAC đăng nhập bằng tài khoản quản trị khác thì thiết lập theo người dùng (HKCU) và gói cài theo người dùng được bỏ qua kèm lý do, không báo thành công nhầm.
- Mọi tác vụ rủi ro vẫn phải qua hộp xác nhận; hàng đợi vẫn tự chặn task cần xác nhận (P3-2).

## Rủi ro còn lại
- Toàn bộ giao diện chạy quyền cao: lỗi trong phần mềm có tác động rộng hơn. Helper riêng theo ADR-002 vẫn là hướng tốt hơn về lâu dài.
- Máy tắt UAC hoặc chính sách cấm quyền quản trị: phần mềm không mở được bằng tài khoản thường.
