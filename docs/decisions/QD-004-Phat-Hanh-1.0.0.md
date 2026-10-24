# QĐ-004 — Phát hành bản 1.0.0

- Ngày: 24/10/2026 · Người quyết định: PO (Lăng Dũng).
- Căn cứ: báo cáo rà soát mã nguồn BC-002; PR #3 (sửa P1-1..P1-5, P2-1..P2-4, P3-1..P3-3) đã qua CI.

## Quyết định

| Câu hỏi | Quyết định của PO | Đề xuất trước đó |
|---|---|---|
| Kiểm thử trên máy thật trước khi phát hành | **Phát hành 1.0.0 ngay**, không qua bản thử rc | Phát hành 1.0.0-rc1, thử trên 2–3 laptop rồi mới chính thức |
| Nơi phát hành | **GitHub Release + trang tải dunglam.top** (thay bản ToiUuMay 1.0.5 đang tải công khai) | GitHub Release trước |
| Đổi khóa API máy chủ đăng nhập (đã lộ trong Hosting.zip, BC-001) | **Để sau** | Đổi cùng v1 |
| Ký số EXE | **Phát hành chưa ký** (Windows hiện cảnh báo SmartScreen “Unknown publisher”) | Như PO chọn |
| Tắt BitLocker chọn sẵn trong Thiết lập đầy đủ | **Giữ nguyên** (vẫn bắt buộc tick đồng ý) | — |

## Rủi ro đã biết, PO chấp nhận khi phát hành
1. **Chia ổ đĩa** và **tắt BitLocker** chưa được thử trên laptop thật (QĐ-001 ghi “chỉ dùng trên máy thử nghiệm cho đến khi kiểm thử xong”). Kỹ thuật viên chỉ dùng hai tính năng này khi khách đã sao lưu dữ liệu và máy cắm sạc.
2. Các sửa lỗi của PR #3 mới được kiểm bằng test tự động trên CI, chưa thử tay (nâng quyền bằng tài khoản khác, `--scope machine`, đóng cửa sổ khi đang cài).
3. EXE chưa ký số: Windows SmartScreen cảnh báo khi mở lần đầu; khách cần bấm “More info › Run anyway”.
4. Khóa API máy chủ đăng nhập vẫn là khóa cũ đã từng lộ công khai. Bảo vệ chính vẫn là tài khoản + mật khẩu (bcrypt, giới hạn sai, duyệt tài khoản), nhưng cần đổi ở bản sau.
5. Chưa làm P2-5 (trạng thái đổi tên máy), P2-6 (`-EncodedCommand`), P2-7 (xóa mật khẩu khỏi bộ nhớ sớm).
6. Các quyết định tạm thời trong QĐ-001 (danh mục phần mềm Q-02, tự chấp nhận điều khoản WinGet Q-16, quy ước tên máy Q-07, thời gian lưu log Q-08) được dùng nguyên cho 1.0.0.

## Phương án khôi phục
- GitHub: giữ các release cũ; có thể đánh dấu 1.0.0 là “pre-release” hoặc xóa release nếu cần rút.
- Trang tải dunglam.top: bản ToiUuMay 1.0.5 vẫn còn trong lịch sử “Bản phát hành” của Admin; bấm đặt làm hiện hành để quay lại.
- Báo lỗi trên máy thật: dừng hướng dẫn khách tải, ghi phiên bản và log (đã ẩn dữ liệu nhạy cảm), phát hành bản sửa 1.0.x.
