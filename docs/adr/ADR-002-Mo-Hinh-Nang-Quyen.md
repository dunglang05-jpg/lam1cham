# ADR-002 — Mô hình nâng quyền

> **Cập nhật 24/10/2026:** từ bản 1.0.1, PO quyết định ứng dụng luôn chạy quyền quản trị (`requireAdministrator`) — xem [QĐ-005](../decisions/QD-005-Luon-Chay-Quyen-Quan-Tri.md). Mô hình helper dưới đây vẫn là hướng đích lâu dài.

- Trạng thái: **Đề xuất — chờ Tech Lead và Security duyệt (Q-05)**
- Ngày: 18/10/2026
- Liên quan: tài liệu 02 (Ranh giới tin cậy), 05 (Bảo mật desktop)

## Bối cảnh

Một số task cần quyền quản trị (đổi múi giờ, cài phần mềm cho toàn máy, đọc trạng thái BitLocker). Tài liệu 02 yêu cầu không nâng quyền toàn bộ UI nếu không cần và dùng elevated helper được ký số với IPC có kiểm tra.

## Đề xuất

1. UI chạy với quyền người dùng thường (`asInvoker`).
2. Khi người dùng bấm Bắt đầu và danh sách có task cần quyền quản trị, ứng dụng khởi chạy **chính file EXE đã ký** ở chế độ helper qua UAC (một lần cho cả lượt chạy).
3. UI và helper giao tiếp qua named pipe:
   - Tên pipe ngẫu nhiên cho mỗi lượt, truyền qua tham số khởi chạy.
   - ACL chỉ cho phép SID người dùng hiện tại.
   - Helper kiểm tra tiến trình phía kia là tiến trình cha đã khởi chạy nó.
4. Helper chỉ nhận **lệnh định nghĩa sẵn** (ID task + tham số có kiểu, được validate). Không nhận chuỗi lệnh shell, đường dẫn script hay DLL.
5. Helper tự thoát khi UI đóng, khi pipe đứt hoặc sau thời gian rảnh có giới hạn.
6. UAC bị từ chối → các task cần quyền quản trị chuyển `SKIPPED` với lý do "Người dùng không cấp quyền quản trị"; task không cần quyền vẫn chạy.

## Phương án thay thế

- **Chạy toàn bộ app với quyền quản trị (`requireAdministrator`)**: đơn giản nhất, nhưng trái tài liệu 02 và mở rộng bề mặt tấn công (UI, parser, log đều chạy elevated). Chỉ cân nhắc nếu helper làm chậm MVP quá nhiều và phải có ADR phê duyệt riêng.

## Việc cần làm nếu duyệt

- Định nghĩa giao thức IPC (schema, phiên bản).
- Test bảo mật: client lạ kết nối pipe, tham số ngoài allowlist, helper bị kill giữa chừng.
