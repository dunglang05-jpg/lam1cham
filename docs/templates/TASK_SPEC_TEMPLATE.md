# Đặc tả task — <ID> <Tên>

> Sao chép mẫu này vào issue trước khi viết code cho một task mới. Task chỉ được code khi mọi mục đã điền và được duyệt.

| Trường | Giá trị |
|---|---|
| ID | ví dụ `windows.timezone.vn` |
| Tên hiển thị | |
| Phiên bản | 1 |
| Mức rủi ro | R0 / R1 / R2 / R3 |
| Quyền cần thiết | Người dùng / Quản trị |
| Loại | Installer / Cấu hình / Kiểm tra |
| Phụ thuộc | ID các task phải `SUCCESS` trước |
| Timeout | |
| Retry | Số lần tối đa, backoff, điều kiện retry (lỗi tạm thời nào) |

## Precondition
Điều gì phải đúng trước khi chạy. Không đạt thì trạng thái nào (`SKIPPED` / `NOT_SUPPORTED`) và thông báo gì.

## Execute
Thao tác cụ thể (API, tham số). Không ghép input vào shell.

## Verify
Cách đọc lại trạng thái thực tế để khẳng định thành công. Khi nào trả `NEEDS_ATTENTION`.

## Rollback / compensation
Giá trị cũ được lưu ở đâu và khôi phục thế nào, hoặc lý do không thể rollback.

## Chạy lại (idempotency)
Chạy lần hai trên máy đã đúng trạng thái thì làm gì.

## Hủy, mất mạng, reboot
Hành vi trong từng tình huống.

## Log
Được log gì; dữ liệu nào phải redact.

## Tiêu chí nghiệm thu
- [ ] ...

## Test
- Unit:
- Integration:
- Môi trường trong ma trận cần chạy:
