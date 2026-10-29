# QĐ-006 — Đồng bộ danh sách ứng dụng giữa Web Admin và phần mềm

- Ngày: 30/10/2026 · Người quyết định: PO (Lăng Dũng). Áp dụng từ bản 1.1.0.
- Liên quan: tài liệu 02 (ranh giới tin cậy), 05 (allowlist, xác minh chữ ký), QĐ-001 Q-02/Q-04, QĐ-002.

## Hiện trạng trước khi sửa
- Phần mềm 1.0.1 dùng danh mục cố định trong mã (10 gói WinGet), chỉ gọi `api.php` để `login`/`register`.
- `api.php` đã có `listApps` (cần token, trả app đang bật) và `installReport` (ghi Lịch sử cài) cho phần mềm cũ ToiUuMay. Định dạng app: `Kind` Exe/Msi/Zip/Web, `DirectUrl`/`PageUrl`+`PageRegex`, `SilentArgs`, `Variants`, `DetectNames/Paths`, `AllowUnsigned`, `WingetId`, `Website`, `ZipFolder/ZipExe`, `Checked`, `Initial`, `Color`.
- Admin › Ứng dụng có 17 app, phần lớn cài bằng link; danh sách này không tới được laptop chạy Làm 1 Chạm.

## Quyết định của PO
| Câu hỏi | Quyết định |
|---|---|
| Phạm vi | Cài qua WinGet **và** link tải (Exe/Msi/Zip), lối tắt web, chọn phiên bản |
| Ứng dụng laptop không cài được | Hiện mờ kèm lý do |
| Danh sách ban đầu | Gộp 17 app Admin + 7-Zip, Adobe Acrobat Reader, SumatraPDF, VLC, Microsoft 365 |
| Đổi khóa API | Để sau |
| Ngôn ngữ | Mọi link dùng bản tiếng Việt (bộ cài ưu tiên tiếng Việt; đường dẫn web/Admin tiếng Việt, đường dẫn cũ tự chuyển) |
| An toàn khi cài bằng link | Chữ ký số + danh sách hãng tin cậy đóng sẵn trong phần mềm |

## Thiết kế
**Ranh giới tin cậy.** Web Admin chỉ chọn *ứng dụng nào* được cài và lấy file ở đâu; *cách cài và mọi kiểm tra an toàn* nằm trong phần mềm. Máy chủ không thêm được loại tác vụ mới.

**Cài bằng link** (`Windows/Software/LinkDownloader.cs`, `DirectInstallTask.cs`, `Authenticode.cs`, `TrustedPublishers.cs`):
- Chỉ https, kể cả sau chuyển hướng; dò link mới nhất bằng mẫu trên Admin (giới hạn 2 giây); giới hạn 6 GB.
- Lưu vào thư mục tạm mới chỉ Administrators và SYSTEM có quyền; giữ handle chỉ đọc đến khi cài xong.
- Bắt buộc chữ ký Authenticode hợp lệ (WinVerifyTrust) **và** tên hãng ký (O hoặc CN) nằm trong `TrustedPublishers`. Tùy chọn “cho cài file không có chữ ký số” trên Admin không có tác dụng với Làm 1 Chạm (file như vậy bị từ chối khi cài, kèm lý do).
- ZIP: giải nén vào thư mục tạm, kiểm tra chữ ký của file chạy rồi mới chép vào Program Files.
- Tham số cài chỉ gồm chữ, số, `/ - _ = .` và khoảng trắng; truyền tách rời, không qua shell. Msi chạy bằng `msiexec /i … /qn /norestart`.
- Phiên bản loại “Mở link” chỉ mở trang tải bằng Explorer (quyền người dùng); kết quả *Cần chú ý* đến khi phát hiện đã cài.

**WinGet:** dùng khi app có mã WinGet và nhà phát hành (ô “Nhà phát hành” trên Admin là nhà phát hành của gói trên WinGet, phải khớp `winget show`). Ưu tiên `--locale vi-VN`, không có bộ cài tiếng Việt thì cài bản mặc định. Máy thiếu WinGet thì chuyển sang link nếu có; nhà phát hành không khớp thì không chuyển.

**Danh sách** (`RemoteCatalog.cs`): kiểm tra lại toàn bộ dữ liệu (tên, mã WinGet, link, tham số, đường dẫn nhận biết chỉ trong `%ProgramFiles%`, `%LocalAppData%`…); mục sai bị bỏ hoặc hiện mờ; tối đa 100 app. Không lấy được danh sách thì dùng danh mục có sẵn và báo trên trang Phần mềm.

**Xác minh:** WinGet `list`, hoặc tên trong “Installed apps”/đường dẫn file khai báo trên Admin. Không có cách xác minh thì *Cần chú ý*, không báo thành công.

**Báo cáo:** sau mỗi lượt cài gửi `installReport` (tên máy, Windows, từng app: nguồn, dung lượng, thời gian, kết quả, mã lỗi, lý do đã ẩn dữ liệu nhạy cảm).

## Thay đổi trên máy chủ (đã làm 30/10/2026, có bản sao lưu `_Copy_N`)
- `lib.php` `validate_app()`: thêm `Publisher`, `MachineWide`, `Category`, `Description` (không bỏ trường cũ).
- `modules/apps.php`: 4 ô mới trong form; cột “Làm 1 Chạm” (WinGet / Link, cần chữ ký số / Chọn phiên bản / Lối tắt web / Hiện mờ).
- Đường dẫn tiếng Việt: `/admin/tong-quan`, `/admin/tai-khoan`, `/admin/ban-phat-hanh`, `/admin/ung-dung`, `/admin/lich-su-cai`, `/admin/kho-key`, `/admin/ma-kho`, `/admin/nhat-ky`, `/admin/mo-rong`, `/admin/cai-dat`, `/admin/tai-khoan-cua-toi`; trang công khai `/tai-xuong`, `/lay-key`. Đường dẫn cũ chuyển 301. `api.php`, `mirror.php` giữ nguyên.
- Dữ liệu: 22 app; mã WinGet + nhà phát hành đối chiếu kho `microsoft/winget-pkgs` (sửa `Viber.Viber` → `Rakuten.Viber`; WhatsApp chỉ có trên Microsoft Store nên hiện mờ); link tiếng Việt cho WinRAR, UltraViewer, Microsoft 365/Office. Danh sách cũ lưu ở `khong_cong_khai/apps-backup-2026-10-30.json`.

## Rủi ro còn lại
- Tài khoản Admin web bị chiếm: kẻ gian chỉ chọn được bộ cài thật của hãng tin cậy hoặc gói WinGet có nhà phát hành khớp; vẫn có thể cài phần mềm không mong muốn. Khuyến nghị mật khẩu admin mạnh.
- Tên hãng ký trong `TrustedPublishers` chưa đối chiếu đủ trên máy thật; sai thì app báo lỗi rõ (an toàn) và sửa ở bản sau.
- Không kiểm tra thu hồi chứng chỉ qua mạng (tránh treo khi mạng yếu).
- Khóa API cũ đã lộ (BC-001), PO hoãn đổi.

## Khôi phục
- Hosting: khôi phục `lib.php`, `admin.php`, `admin_lib.php`, `modules/apps.php`, `.htaccess`, `index.html` từ bản `_Copy_N`; nhập lại `apps-backup-2026-10-30.json`.
- Phần mềm: đặt 1.0.1 làm bản hiện hành trên Admin.
