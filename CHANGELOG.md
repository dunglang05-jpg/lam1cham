# Nhật ký thay đổi

Ghi theo bản ứng dụng, chia theo mô-đun (xem `docs/decisions/QD-002-Cau-Truc-Mo-Dun-Va-Cap-Nhat.md`).

## 1.0.1 — 24/10/2026

**Khung ứng dụng (`shell`) 0.11.0** — luôn chạy quyền quản trị ([QĐ-005](docs/decisions/QD-005-Luon-Chay-Quyen-Quan-Tri.md))
- EXE yêu cầu quyền quản trị khi mở (`requireAdministrator`): Windows hỏi UAC một lần, không còn nút “Nâng quyền quản trị…” và không phải mở lại phần mềm giữa chừng.
- Báo cáo, thư mục, trang web và Cài đặt Windows được mở qua Explorer (quyền người dùng), không để trình duyệt chạy kèm quyền quản trị.
- Các thông báo “bấm Nâng quyền” đổi thành hướng dẫn mở lại phần mềm và chọn Yes khi Windows hỏi quyền.

**Tài khoản & Đăng nhập (`account`) 0.1.1**
- Màn hình đăng nhập: bảng bên trái thay ba dòng thông tin tài khoản bằng phần giới thiệu ngắn về phần mềm và hình minh họa laptop đang chạy mã thiết lập (vẽ vector, con trỏ nhấp nháy), trên nền vũ trụ: sao, sao nhấp nháy, tinh vân, sao băng và hành tinh trôi nhẹ (chỉ hoạt ảnh độ mờ/vị trí, không làm nặng máy). Biểu mẫu đăng nhập/đăng ký không đổi.

## 1.0.0 — 24/10/2026

Bản chính thức đầu tiên, gồm toàn bộ tính năng và sửa lỗi đến 0.14.0. Không đổi mã chức năng so với 0.14.0; phiên bản các mô-đun giữ nguyên.

- Quyết định phát hành và rủi ro đã biết PO chấp nhận: [QĐ-004](docs/decisions/QD-004-Phat-Hanh-1.0.0.md) — chưa ký số, chưa đổi khóa API, chia ổ/tắt BitLocker chưa thử trên nhiều máy thật.
- CI: gắn tag `vX.Y.Z` thì build lại từ tag và tạo GitHub Release kèm EXE, SHA-256 và ghi chú `docs/release/X.Y.Z.md`.

## 0.14.0 — 24/10/2026

Sửa lỗi theo báo cáo rà soát mã nguồn trước v1 (BC-002), PO đã duyệt danh sách. PO giữ "Tắt BitLocker" được chọn sẵn trong Thiết lập đầy đủ.

**Lõi điều phối (`engine`) 0.4.0**
- Task tự hủy vì lý do riêng (timeout nội bộ, thư viện mạng) không còn làm văng cả lượt chạy: task đó thành *Cần chú ý*, các task sau vẫn chạy (P1-5).
- Hàng đợi tự từ chối task cần xác nhận riêng (`RequiresConfirmation`) nếu lượt chạy không đi qua hộp xác nhận — lớp chặn thứ hai, không chỉ dựa vào giao diện (P3-2).
- Báo cáo: hai lượt trong cùng một giây không còn ghi đè nhau (P3-3).

**Khung ứng dụng (`shell`) 0.10.0**
- Đóng cửa sổ khi đang chạy: hỏi lại; đồng ý thì dừng sau bước hiện tại, ghi báo cáo rồi mới đóng — không bỏ dở cài phần mềm/chia ổ mà không có báo cáo (P1-1).
- Lượt chạy dừng vì lỗi bất ngờ: các tác vụ chưa xong được đánh dấu *Cần chú ý*, vẫn ghi báo cáo và hiện hộp lỗi; không còn dòng treo ở "Đang chạy" (P1-5).

**Tổng quan (`overview`) 0.7.2**
- Hộp xác nhận gói nhanh liệt kê các mục người dùng chọn thêm ngoài gói; mục không hoàn tác được (vd. Thùng rác) có cảnh báo đỏ và bắt buộc tick đồng ý (P1-2).

**Phần mềm thiết yếu (`software`) 0.4.0**
- Gói hỗ trợ cài cho toàn máy được cài với `--scope machine` (QĐ-001 Q-04); gói không có bộ cài toàn máy thì cài theo phạm vi mặc định (P1-4).
- Đối chiếu nhà phát hành trên nguồn WinGet với danh mục trước khi cài; không khớp thì không cài (chống package ID giả, tài liệu 05) (P2-3).
- Gói cài theo người dùng bị bỏ qua (kèm lý do) khi phần mềm đang chạy bằng tài khoản khác với tài khoản đăng nhập máy (P1-3).

**Cài đặt & Tối ưu Windows (`windows`) 0.7.1**
- Thiết lập theo người dùng (HKCU) bị bỏ qua kèm lý do khi phần mềm được nâng quyền bằng tài khoản quản trị khác — trước đây ghi nhầm tài khoản quản trị mà vẫn báo thành công (P1-3).
- Hoàn tác: đọc lại để xác minh trước khi báo "đã khôi phục"; khôi phục thất bại thì giữ giá trị cũ đã lưu để thử lại (múi giờ, nguồn điện, registry); hoàn tác khóa hàng đợi và một mục lỗi không làm dừng các mục khác; file lưu hỏng được báo rõ (P2-1).

**Dọn dẹp & Hiệu năng (`cleanup`) 0.4.1** — làm trống Thùng rác khai báo mức R3 (không hoàn tác được, bắt buộc xác nhận riêng) (P2-2).

**Kiểm tra & Bàn giao (`handover`) 0.5.1** — Wi-Fi chỉ *Thành công* khi đã kết nối; chưa kết nối hoặc Bluetooth thì *Cần kiểm tra tay* (tài liệu 03) (P2-4).

**Ổ đĩa & Tài khoản (`disk`) 0.6.2** — nhãn ổ mới và tên máy không nhận ký tự xuống dòng ở cuối (P3-1).

## 0.13.0 — 24/10/2026

**Tài khoản & Đăng nhập (`account`) 0.1.0** — mô-đun mới
- Màn hình đăng nhập trước khi vào ứng dụng, dùng chung tài khoản với Web Admin (`api.php` trên dunglam.top, giao thức sẵn có: `login`, `register`).
- Đăng ký tài khoản mới ngay trong phần mềm; tài khoản ở trạng thái chờ cho đến khi quản trị viên duyệt. Kiểm tra họ tên, tên đăng nhập, mật khẩu trên máy theo đúng quy tắc của máy chủ.
- Thông báo rõ từng trường hợp: sai mật khẩu (còn bao nhiêu lần thử), tài khoản chờ duyệt, bị khóa, mất mạng, máy chủ lỗi, bản build chưa có khóa máy chủ.
- **Phiên chỉ nằm trong bộ nhớ của lần chạy này**: không lưu xuống đĩa, thoát phần mềm là tự đăng xuất, lần mở sau phải đăng nhập lại (kể cả khi phần mềm tự mở lại để nâng quyền).
- Mật khẩu không qua binding, không ghi log, xóa khỏi ô nhập sau khi gửi; log chỉ ghi tên đăng nhập và kết quả. Mã phiên không bao giờ in ra log.
- Gửi kèm mã máy (`lmc-` + băm SHA-256 của MachineGuid) để quản trị viên biết tài khoản dùng trên máy nào; không gửi mã gốc.
- Khóa truy cập máy chủ nạp lúc build từ secret `LMC_API_KEY` (không nằm trong mã nguồn); địa chỉ máy chủ mặc định `https://dunglam.top/api.php`.

**Khung ứng dụng (`shell`) 0.9.0**
- Khởi động: mở cửa sổ đăng nhập trước, đăng nhập xong mới mở cửa sổ chính.
- Thanh bên: hiện người đang đăng nhập (họ tên, vai trò) và nút **Đăng xuất** (mở lại phần mềm ở màn hình đăng nhập).

## 0.12.1 — 19/10/2026

**Tổng quan (`overview`) 0.7.1**
- Thẻ Một chạm chiếm 60% bề ngang, thẻ thiết bị 40%; thông số (pin, Windows, CPU) xuống dòng thay vì bị cắt "…".
- Nút "Bắt đầu thiết lập đầy đủ" đổi sang xanh lá nổi trên nền xanh dương; nút "Bắt đầu" của các gói đổi sang xanh dương.

**Ổ đĩa & Tài khoản (`disk`) 0.6.1** — nút "Sửa" tài khoản đổi sang xanh dương.

**Khung ứng dụng (`shell`) 0.8.1** — dùng chung
- Nút phụ không còn nền trắng: nền xanh nhạt, chữ xanh (dễ nhận ra là nút bấm).
- `SplitPanel` thêm `SideRatio` (chia cột theo tỷ lệ).

## 0.12.0 — 19/10/2026

**Khung ứng dụng (`shell`) 0.8.0**
- Tự co giãn: cửa sổ nhỏ hơn 1366×768 thì thu nhỏ toàn bộ giao diện (tối thiểu 85%) để thông tin vừa một màn hình.
- Khoảng cách gọn hơn: tiêu đề trang nhỏ hơn, lề trang, thẻ, dòng thiết lập sát hơn; thanh trên thấp hơn.
- Dùng chung: kiểu ô mật khẩu (`PasswordBox`).

**Tổng quan (`overview`) 0.7.0**
- Thẻ **Một chạm** chuyển sang bên trái, to và rõ hơn: biểu tượng lớn, 4 ô bước có icon và số lượng thật (phần mềm, thiết lập Windows, nhóm dọn dẹp, mục kiểm tra), logo phần mềm sẽ cài, nút bắt đầu lớn.
- Thẻ thiết bị bên phải gọn lại, mỗi thông số một dòng có icon.
- Tác vụ nhanh còn 3 gói (gói đầy đủ đã ở thẻ Một chạm), thẻ một hàng.
- Bỏ khu "Tình trạng tổng thể".

**Cài đặt & Tối ưu Windows (`windows`) 0.7.0**
- Một thẻ chiếm cả bề ngang; thiết lập chia **2 cột** khi đủ rộng, cửa sổ hẹp tự về 1 cột. Bộ lọc nhóm nằm cùng hàng tiêu đề; ghi chú và nút ở một hàng.
- Bỏ thẻ "Trạng thái hệ thống" và "Nguyên tắc an toàn" (nguyên tắc ghi gọn ở ghi chú).

**Ổ đĩa & Tài khoản (`disk`) 0.6.0** — tính năng mới: **Sửa tài khoản**
- Thêm icon cho từng thẻ, ổ đĩa và tài khoản (quản trị viên có icon khiên).
- Danh sách tài khoản đang bật trên máy (tên, tên hiển thị, loại, cục bộ/Microsoft, "Đang dùng").
- Nút **Sửa** (chỉ tài khoản cục bộ): đổi tên hiển thị, đổi tên đăng nhập, đặt mật khẩu mới. Chạy qua hàng đợi, cần quyền quản trị, mỗi thay đổi được đọc lại để xác minh (mật khẩu: đăng nhập thử). Đặt mật khẩu bắt buộc tick khách đồng ý và cảnh báo mật khẩu đã lưu có thể không mở được.
- Bảo mật: dùng Windows API (netapi32) trực tiếp — mật khẩu không qua dòng lệnh, file, log hay báo cáo; bộ nhớ chứa mật khẩu được xóa sau khi dùng. Tài khoản Microsoft không sửa ở đây.

**Dọn dẹp (`cleanup`) 0.4.0**
- Gộp thành một thẻ: vòng dung lượng + nút Quét/Dọn trên cùng, nhóm file 2 cột có icon, nguyên tắc an toàn thành dải nhãn ngắn.

## 0.11.0 — 18/10/2026

Giao diện mới theo file thiết kế PO (`lam1cham_canvas.html`) cho 7 trang đang có. Mọi số liệu là dữ liệu thật của máy; không thêm phần minh họa không có chức năng (Pro, Web Admin, thông báo, hồ sơ chưa làm).

**Khung ứng dụng (`shell`) 0.7.0**
- Thanh bên màu navy: thương hiệu, nhóm "Không gian làm việc" (7 mô-đun, gồm Mô-đun & cập nhật), nhóm "Hệ thống" (Nhật ký, Báo cáo), hộp "Cần hỗ trợ?" mở Trợ giúp, phiên bản. Cửa sổ hẹp: chỉ biểu tượng.
- Thanh trên: đường dẫn "Không gian làm việc / trang", ô **Tìm tính năng** (gõ không dấu cũng được; bấm kết quả hoặc Enter để mở đúng trang — tìm cả tên từng tác vụ), trạng thái quyền quản trị.
- Chân trang: phiên bản, "Chạy trực tiếp – Không cần cài đặt" (ẩn trên màn hình thấp).
- Dùng chung: bảng màu, thẻ bo 15 px, nút gradient, nhãn (tag), công tắc (`Switch`), ô icon nhiều màu, ghi chú (`Note`), dòng thiết lập (`SettingRow`), vòng tiến độ (`RingProgress`), tiêu đề trang có nhãn nhỏ (eyebrow), bộ lọc viên (`FilterChip`).

**Tổng quan (`overview`) 0.6.0**
- "Xin chào! Sẵn sàng cho laptop mới." · Thẻ thiết bị: model, tên máy, dải thông số thật (CPU, RAM, ổ, Windows, pin, WinGet, mạng), nút Đọc lại.
- Thẻ xanh **Một chạm**: nội dung thật của gói Thiết lập đầy đủ (số phần mềm, thiết lập, nhóm dọn dẹp, mục kiểm tra) và nút chạy (vẫn qua hộp xác nhận).
- **Tác vụ nhanh** = 4 gói, mỗi thẻ có nút "Bắt đầu gói".
- **Tình trạng tổng thể**: vòng tiến độ 5 bước quy trình + 3 điều kiện (thông tin máy, WinGet, quyền); **Hoạt động gần đây** (4 tác vụ gần nhất, báo cáo gần nhất).

**Phần mềm (`software`) 0.3.0** — thanh lọc nhóm + ô tìm; bảng Chọn · Phần mềm · Nhóm · Đề xuất (Khuyến nghị/Tùy chọn) · Trạng thái; thẻ "Danh sách đã chọn" với nút Cài đặt và Xem kế hoạch.

**Tối ưu Windows (`windows`) 0.6.0** — thẻ "Thiết lập đề xuất": lọc theo nhóm, mỗi dòng có icon, tên, mô tả và **công tắc**; mục chỉ kiểm tra có nút "Kiểm tra". Thẻ "Trạng thái hệ thống" (Windows, số thay đổi, số cần UAC) và "Nguyên tắc an toàn".

**Ổ đĩa & tài khoản (`disk`) 0.5.0** — Dung lượng ổ đĩa | Tài khoản người dùng (kèm BitLocker, hướng dẫn khóa khôi phục); Chia ổ đĩa | Tên máy tính. Nút "Làm mới thông tin".

**Dọn dẹp (`cleanup`) 0.3.0** — "Kết quả phân tích" với vòng dung lượng đã chọn, danh sách nhóm (số file, dung lượng, mức an toàn); "Nguyên tắc an toàn" là các quy tắc luôn áp dụng (không phải công tắc giả).

**Kiểm tra & bàn giao (`handover`) 0.5.0** — "Danh sách kiểm tra" với thanh tiến độ (Hệ thống, Phần cứng thử tay); "Tóm tắt bàn giao" với vòng % đạt, chip tổng hợp, 7 điều kiện, nút Tạo báo cáo.

**Mô-đun & cập nhật (`modules`) 0.3.0** — lưới thẻ mô-đun 3 cột (icon, phiên bản, mô tả, mã); thẻ trạng thái cập nhật nói rõ chưa kết nối máy chủ.

## 0.10.0 — 18/10/2026

Theo bản mẫu PO: **gọn như thiết kế gốc** — menu dọc bên trái, mỗi mục một dòng, nhóm xếp thành thẻ 2 cột. Bỏ mô tả dài, cột "Hiện tại" và chip trạng thái khi chưa chạy; thông tin chi tiết chuyển vào tooltip (rê chuột).

**Khung ứng dụng (`shell`) 0.6.0**
- Trả lại menu dọc bên trái như 0.8.1 (bỏ thanh tab ngang của 0.9.0). "Mở log", "Mở báo cáo" về lại cuối menu.

**Cài đặt & Tối ưu Windows (`windows`) 0.5.0**
- 4 nhóm thành 4 thẻ (2 cột). Mỗi thiết lập một dòng: ô chọn + tên. Kết quả (Thành công/Lỗi, Xem lỗi) chỉ hiện sau khi chạy.
- Rê chuột vào dòng: mô tả, giá trị hiện tại và 6 điểm chi tiết. Bỏ nút "Áp dụng" từng dòng; dùng "Áp dụng n thay đổi" ở trên.

**Ổ đĩa & Tài khoản (`disk`) 0.4.0**
- Bố cục 2×2: Thông tin ổ đĩa (kèm dòng BitLocker) | Chia ổ đĩa (3 ô nhập trên một hàng, xem trước gọn) — Tài khoản người dùng | Tên máy tính.

**Kiểm tra & Bàn giao (`handover`) 0.4.0**
- Điều kiện bàn giao chia 2 cột, mỗi điều kiện một dòng. Hệ thống | Phần cứng (thử tay) mỗi mục một dòng: tên, bằng chứng ngắn, trạng thái.

**Tổng quan (`overview`) 0.5.3**
- Trở lại bố cục 0.8.1 (Thông tin máy 1 cột) do có lại menu bên trái.

## 0.9.0 — 18/10/2026

Theo PO: **mỗi mô-đun một tab ngang**, bỏ tab con trong từng trang, nội dung gọn hơn.

**Khung ứng dụng (`shell`) 0.5.0**
- Bỏ thanh menu dọc bên trái; thay bằng thanh tab ngang dưới thanh tiêu đề: Tổng quan · Phần mềm · Tối ưu Windows · Ổ đĩa & Tài khoản · Dọn dẹp · Bàn giao · Mô-đun & Cập nhật. Cửa sổ hẹp: tab chỉ hiện biểu tượng.
- "Log" và "Báo cáo" chuyển lên thanh tiêu đề. Nội dung trang rộng thêm ~230 px.
- Bỏ kiểu tab con (`PageTabs`) của 0.8.2.

**Cài đặt & Tối ưu Windows (`windows`) 0.4.2**
- Bỏ tab con; các nhóm xếp liền một trang như trước, tiêu đề nhóm một dòng, dòng thiết lập thấp hơn. Chi tiết 6 điểm vẫn đóng sẵn.

**Ổ đĩa & Tài khoản (`disk`) 0.3.2**, **Kiểm tra & Bàn giao (`handover`) 0.3.4**
- Bỏ tab con, trở lại một trang.

**Tổng quan (`overview`) 0.5.2**
- Thông tin máy chia 2 cột (khung rộng hơn) để trang vẫn vừa màn hình khi có thêm thanh tab.

## 0.8.2 — 18/10/2026

Tinh gọn: chia trang dài thành **tab ngang**, mỗi lần chỉ hiện một nhóm.

**Cài đặt & Tối ưu Windows (`windows`) 0.4.1**
- 4 tab theo nhóm: Cập nhật và bảo trì · Trải nghiệm Windows · Quyền riêng tư · Hiệu năng & Nguồn điện (kèm số mục).
- Không tự mở phần chi tiết của dòng đầu; bấm mũi tên để xem 6 điểm. Mô tả trang ngắn hơn.

**Ổ đĩa & Tài khoản (`disk`) 0.3.1**
- 3 tab: Ổ đĩa & Mã hóa · Chia ổ đĩa · Tên máy & Tài khoản.

**Kiểm tra & Bàn giao (`handover`) 0.3.3**
- 3 tab: Điều kiện bàn giao · Hệ thống · Phần cứng (kèm số mục). Chip tóm tắt Đạt/Cần chú ý/Chưa kiểm tra giữ ở trên.

**Khung ứng dụng (`shell`) 0.4.4** — dùng chung
- Kiểu tab ngang `PageTabs`/`PageTab` và nhãn số mục `TabCount` trong Theme.

## 0.8.1 — 18/10/2026

**Tổng quan (`overview`) 0.5.1** — khôi phục giao diện ban đầu, chỉ chỉnh kích cỡ
- Theo yêu cầu PO: bỏ bố cục 0.7.1 và hàng Quy trình/Tài nguyên của 0.8.0; trở lại giao diện của 0.7.0 (thẻ giới thiệu, Thông tin máy, 4 gói, Gói này gồm, nút Bắt đầu ở giữa).
- Chỉ thu nhỏ kích cỡ để vừa laptop 1280×720 không cần cuộn: lề và khoảng cách nhỏ hơn, biểu tượng/chữ tiêu đề nhỏ hơn, thẻ gói thấp hơn (mô tả một dòng, rê chuột xem đủ), Thông tin máy mỗi mục một dòng.

**Khung ứng dụng (`shell`) 0.4.3**
- Bỏ phần giãn trang theo chiều cao cửa sổ (0.4.2).
- Màn hình thấp: thanh trên cao 56 px thay vì 64, lề trang 12 px.

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
