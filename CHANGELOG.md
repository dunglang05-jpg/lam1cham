# Nhật ký thay đổi

Ghi theo bản ứng dụng, chia theo mô-đun (xem `docs/decisions/QD-002-Cau-Truc-Mo-Dun-Va-Cap-Nhat.md`).

## 0.4.0 — 18/10/2026

**Khung ứng dụng (`shell`) 0.3.0**
- Cửa sổ không còn tràn màn hình: màn hình nhỏ hơn 1440×900 (ví dụ 1366×768, hoặc 1920×1080 ở tỉ lệ 150%) thì mở toàn màn hình; kích thước tối thiểu 900×560.
- Cửa sổ hẹp hơn 1200 px: thanh bên thu gọn chỉ còn icon (rê chuột để xem tên), lề trang nhỏ hơn, ẩn nhãn "Chạy trực tiếp".
- Hộp Đang thực hiện / Kết quả và hộp thoại tự vừa chiều cao màn hình.
- Thêm khung bố cục co giãn dùng chung: SplitPanel (hai khối, tự xếp chồng khi hẹp) và AdaptiveGrid (lưới thẻ tự chọn số cột). Tiêu đề trang tự xuống dòng.

**Tổng quan (`overview`) 0.3.1**, **Phần mềm (`software`) 0.2.2**, **Ổ đĩa (`disk`) 0.2.2**, **Bàn giao (`handover`) 0.3.1**
- Bố cục co giãn: khung bên phải xuống dưới khi hẹp; 4 thẻ gói tự chia 4/2/1 cột.

## 0.3.0 — 18/10/2026

**Tổng quan & Gói nhanh (`overview`) 0.3.0**
- Khi mở ứng dụng chỉ đọc cấu hình máy (model, CPU, RAM, ổ hệ thống, Windows) và pin; không còn tự chạy toàn bộ kiểm tra (trước đây 20 bước, rất lâu).
- Nút "Đọc thông tin máy" / "Đọc lại" thay cho "Kiểm tra máy".

**Kiểm tra & Bàn giao (`handover`) 0.3.0**
- Các bước kiểm tra chỉ chạy khi bấm "Bắt đầu kiểm tra" (nút chính của trang).

**Lõi điều phối tác vụ (`engine`) 0.2.1** — sửa lỗi
- Bước kiểm tra chỉ đọc không còn chờ ngưỡng RAM trống (máy ít RAM bị treo ở "Đang chờ RAM khả dụng vượt 20%"). Ngưỡng RAM vẫn áp dụng cho cài phần mềm và thay đổi cấu hình.

**Cài đặt & Tối ưu Windows (`windows`) 0.2.1**
- Giá trị hiện tại của các thiết lập được đọc khi mở trang lần đầu, không đọc lúc khởi động.

**Phần mềm thiết yếu (`software`) 0.2.1**
- "Kiểm tra trạng thái cài" chỉ đọc trạng thái phần mềm.

**Ổ đĩa & Tài khoản (`disk`) 0.2.1**, **Khung ứng dụng (`shell`) 0.2.2**
- Sửa chữ hướng dẫn theo cách đọc thông tin mới.

## 0.2.1 — 18/10/2026

**Khung ứng dụng (`shell`) 0.2.1** — sửa lỗi
- Sửa lỗi mở các trang Phần mềm, Windows, Ổ đĩa, Dọn dẹp, Bàn giao, Mô-đun báo "StaticResourceHolder threw an exception": tiêu đề trang dùng kiểu `IconImage` trước khi khai báo trong Theme.xaml.
- Lỗi giao diện lặp lại chỉ báo một lần, không mở chồng nhiều hộp thoại; trang không hiển thị được thì quay về Tổng quan.
- Thêm test tự động kiểm tra thứ tự và sự tồn tại của tài nguyên XAML.

## 0.2.0 — 18/10/2026

**Khung ứng dụng (`shell`) 0.2.0**
- Giao diện v3: màu, thẻ và nút mới; thanh bên có ô "Mô-đun & Cập nhật".
- Hộp "Đang thực hiện" hiện giữa màn hình kèm tiến độ, CPU/RAM/ổ đĩa; khi xong chuyển thành "Kết quả" với số tác vụ thành công, bỏ qua, lỗi. Thay cho thanh tác vụ ở đáy.

**Tổng quan & Gói nhanh (`overview`) 0.2.0**
- Thẻ giới thiệu, thông tin máy, 4 thẻ gói thiết lập nhanh; ô "Gói này gồm" hiện logo ứng dụng trong gói.

**Phần mềm thiết yếu (`software`) 0.2.0**
- Logo thật của ứng dụng; lọc theo nhóm; khung bên phải hiện ứng dụng đã chọn và số gói sẽ bỏ qua.

**Cài đặt & Tối ưu Windows (`windows`), Ổ đĩa & Tài khoản (`disk`), Dọn dẹp & Hiệu năng (`cleanup`), Kiểm tra & Bàn giao (`handover`) 0.2.0**
- Tiêu đề trang theo v3, có nhãn tên và phiên bản mô-đun.

**Lõi điều phối tác vụ (`engine`) 0.2.0**
- Không đổi hành vi; đánh số phiên bản lần đầu.

**Mô-đun & Cập nhật (`modules`) 0.2.0** — mô-đun mới
- Danh sách mô-đun và phiên bản; sao chép thông tin phiên bản để gửi khi báo lỗi.
- Chưa có máy chủ cập nhật: trang nói rõ điều này, không hiển thị bản mới giả.

**Danh mục phần mềm (`catalog`) 2026.10.18**
- Logo cho Chrome, Brave, Acrobat Reader, 7-Zip, VLC, Microsoft 365, Zalo, Zoom.

## 0.1.0 — 17/10/2026

- Bản thử nghiệm đầu tiên.
