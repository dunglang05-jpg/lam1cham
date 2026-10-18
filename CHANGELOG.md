# Nhật ký thay đổi

Ghi theo bản ứng dụng, chia theo mô-đun (xem `docs/decisions/QD-002-Cau-Truc-Mo-Dun-Va-Cap-Nhat.md`).

## 0.8.0 — 18/10/2026

**Tổng quan (`overview`) 0.5.0** — lấp đầy màn hình
- Hàng dưới mới, giãn theo phần còn lại của cửa sổ (không còn khoảng trống phía dưới):
  - **Quy trình thiết lập**: 5 bước (Kiểm tra máy → Chọn phần mềm → Cài đặt → Tác vụ Windows → Kiểm tra bàn giao) với trạng thái thật; bấm một bước để mở trang tương ứng.
  - **Tài nguyên máy**: CPU đang dùng, RAM còn trống, ổ hệ thống còn trống, cập nhật liên tục.
- Màn hình thấp (laptop 1280×720) hàng dưới co về kích thước nội dung, vẫn không cần cuộn.

**Khung ứng dụng (`shell`) 0.4.2**
- Vùng trang cao tối thiểu bằng vùng nhìn thấy, để trang có hàng giãn lấp hết cửa sổ.

## 0.7.1 — 18/10/2026

**Tổng quan (`overview`) 0.4.2** — vừa một màn hình, không cần cuộn
- Bỏ thẻ giới thiệu lớn; trang chia hai cột: trái là tên ứng dụng (một dòng), 4 gói thiết lập nhanh dạng thẻ gọn (2×2 trên laptop), "Gói này gồm" và nút Bắt đầu; phải là Thông tin máy.
- Nút Bắt đầu nằm cạnh ghi chú và "Xem báo cáo gần nhất" thay vì xếp dọc.
- Thông tin máy mỗi mục một dòng (rê chuột để xem đầy đủ); tên CPU gọn ("Intel Core i3-1005G1 @ 1.20GHz").
- Mục tiêu: vừa laptop 1920×1080 phóng 150% (1280×720) và 1366×768.

**Khung ứng dụng (`shell`) 0.4.1**
- Màn hình thấp (dưới 820 px): lề trên/dưới của trang nhỏ hơn.
- Dùng chung: thẻ gói (`PackCard`) nhận `Padding`, viền không đổi độ dày khi chọn (không giật bố cục).

## 0.7.0 — 18/10/2026

**Cài đặt & Tối ưu Windows (`windows`) 0.4.0** — Tắt BitLocker thành một thiết lập
- Bỏ khung BitLocker riêng. "Tắt BitLocker (giải mã ổ đĩa)" là một dòng trong nhóm Quyền riêng tư & Thông báo, **được chọn sẵn**, nên gói **Thiết lập đầy đủ** (và Chỉ tối ưu Windows) áp dụng luôn. Bỏ chọn dòng này nếu khách muốn giữ mã hóa.
- Một tác vụ cho mọi ổ: tự tìm ổ đang mã hóa, ổ dữ liệu trước, ổ Windows sau; ổ Windows chưa tắt được (ổ dữ liệu còn đang giải mã) thì báo "Cần chú ý" và hướng dẫn chạy lại sau.
- Cột "Hiện tại" hiện trạng thái BitLocker từng ổ. Máy không bật BitLocker → "Đã đúng", không làm gì.
- Hộp xác nhận "Áp dụng" có cảnh báo đỏ và bắt buộc tick "Khách hàng đã đồng ý tắt mã hóa ổ đĩa" khi có dòng này.

**Tổng quan (`overview`) 0.4.1**
- Hộp "Xác nhận trước khi chạy" của gói nhanh: khi gói có tắt BitLocker thì hiện cảnh báo đỏ và ô tick khách đồng ý (gộp cùng ô đồng ý điều khoản WinGet).

## 0.6.0 — 18/10/2026

**Khung ứng dụng (`shell`) 0.4.0**
- Màn hình khởi động có thanh tiến trình và từng bước thật: nạp giao diện, đọc cấu hình máy, đọc pin, kiểm tra mạng, kiểm tra WinGet. Hiện thời gian khởi động.

**Tổng quan (`overview`) 0.4.0** — tăng tốc
- Cấu hình máy và pin đọc bằng API Windows (registry, bộ nhớ, ổ đĩa, nguồn điện), không qua PowerShell: thường dưới 0,1 giây thay vì nhiều giây. WinGet kiểm tra song song.
- Dữ liệu kiểm tra đầy đủ nạp chạy nền ngay sau khi mở: script chia 4 nhóm chạy song song; truy vấn thiết bị lọc bằng WQL thay vì đọc toàn bộ danh sách PnP. Khung Thông tin máy hiện "Đang nạp dữ liệu kiểm tra n/4".

**Kiểm tra & Bàn giao (`handover`) 0.3.2** — tăng tốc
- Lượt kiểm tra chỉ đọc dùng lại dữ liệu đã nạp nền nếu còn mới (3 phút); lượt có thay đổi máy luôn đọc lại.
- Pin: hiện thêm "đang cắm sạc".

**Cài đặt & Tối ưu Windows (`windows`) 0.3.0** — tính năng mới: Tắt BitLocker
- Khung "Mã hóa ổ đĩa (BitLocker)": đọc trạng thái từng ổ khi mở trang (cần quyền quản trị).
- "Tắt BitLocker…" mở hộp xác nhận riêng (cảnh báo đỏ, bắt buộc tick khách đồng ý). Giải mã chạy nền trong Windows; tác vụ xác minh Windows đã bắt đầu giải mã.
- Ổ dữ liệu tắt trước; ổ Windows chỉ tắt khi ổ dữ liệu đã giải mã xong (tránh ổ dữ liệu bị khóa ở lần khởi động sau). Không đọc, hiển thị hay ghi log khóa khôi phục. Không bao giờ nằm trong gói nhanh.

## 0.5.0 — 18/10/2026

**Ổ đĩa & Tài khoản (`disk`) 0.3.0** — tính năng mới: Chia ổ đĩa
- Chọn ổ (NTFS), nhập dung lượng ổ mới (GB) và tên ổ → "Xem trước" hiện con số trước/sau khi chia, dung lượng tối đa tách được và lý do nếu chưa chia được.
- "Chia ổ…" mở hộp xác nhận riêng (cảnh báo đỏ, bắt buộc tick đã sao lưu, máy cắm sạc). Thu nhỏ ổ có sẵn rồi tạo ổ NTFS mới từ phần trống; không xóa hay định dạng phân vùng cũ.
- Quy tắc an toàn: ổ cũ luôn còn trống ít nhất 30 GB (hoặc 15%); ổ mới từ 10 GB; chặn ổ không phải NTFS, ổ MBR đã đủ 4 phân vùng, BitLocker đang mã hóa/giải mã; đọc lại phân vùng ngay trước khi chia; xác minh ổ mới sau khi chia.
- Cần quyền quản trị. Không tự hoàn tác (gộp lại bằng Disk Management).

**Lõi điều phối tác vụ (`engine`) 0.3.0**
- Cho phép tác vụ mức R3 (có thể mất dữ liệu) khi khai báo RequiresConfirmation và không cho cưỡng chế dừng giữa chừng. Tác vụ R3 không bao giờ nằm trong gói nhanh.

**Tổng quan (`overview`) 0.3.2**, **Khung (`shell`) 0.3.1**
- Bớt khoảng trống: Thông tin máy chia 2–3 cột khi rộng; thẻ giới thiệu và thông tin máy nằm cạnh nhau ở cửa sổ hẹp hơn; 4 thẻ gói vừa một hàng sớm hơn.

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
