# QĐ-001 — Quyết định tạm thời cho bản thử nghiệm 0.1.0

- Trạng thái: **Tạm thời**. Có hiệu lực cho bản thử nghiệm nội bộ. Phải xem lại trước bản phát hành nội bộ (M5).
- Căn cứ: PO đồng ý toàn bộ các mục cấp phép và quyết định cho bản thử nghiệm, để có EXE "đầy đủ tính năng để test thử". Ngoại lệ: không chạy thực thi file trong quá trình phát triển.

| Câu hỏi | Quyết định tạm thời | Cách thể hiện trong mã | Rủi ro còn lại |
|---|---|---|---|
| Q-01 Phạm vi Windows | Windows 10 2004+ và Windows 11, x64 | `SupportedOSPlatformVersion 10.0.19041` | Windows 10 đã hết hỗ trợ thông thường; chưa có ma trận test |
| Q-02 Danh mục phần mềm | 10 gói (Chrome, Brave, UniKey, 7-Zip, Adobe Reader, SumatraPDF, VLC, Microsoft 365, Zalo, Zoom); publisher đã đối chiếu với winget-pkgs | `SoftwareCatalog` (allowlist) | PO chưa ký duyệt từng ID |
| Q-03 Thiếu WinGet | Báo rõ, nút mở Microsoft Store (App Installer), không tự tải bộ cài ngoài | `SoftwareViewModel.WinGetMissing`, `OpenStoreCommand` | — |
| Q-04 Phạm vi cài | Toàn máy khi gói hỗ trợ, ngược lại cài cho người dùng | `SoftwarePackage.MachineWide` | UAC có thể hỏi nhiều lần |
| Q-05 Nâng quyền | **Lệch ADR-002**: nâng quyền cả ứng dụng khi người dùng bấm "Nâng quyền…" (khởi động lại với `runas`), thay cho helper riêng | `MainViewModel.Elevate`, `ShellActions.RestartElevated` | Toàn bộ UI chạy quyền cao sau khi nâng; helper riêng vẫn là hướng đích |
| Q-07 Tên máy | Quy tắc NetBIOS: 1–15 ký tự, chữ không dấu/số/gạch ngang, không toàn số; hỏi trước khi khởi động lại | `RenameComputerTask.IsValidName` | Chưa có quy ước đặt tên của công ty |
| Q-08 Nơi lưu | `%LOCALAPPDATA%\LamMotCham\{Logs,Reports,Rollback}`; không tự tải lên | `WindowsEnvironment` | Chưa có chính sách thời gian lưu |
| Q-09 Ngôn ngữ | Chỉ tiếng Việt | — | Chuỗi chưa tách ra file tài nguyên |
| Q-11 Lỗi tạm thời WinGet | Retry tối đa 3 lần cho lỗi mạng/tải/đang bận cài; không retry lỗi hash, policy, không tìm thấy gói | `WinGetClient.Describe` | Cần đối chiếu thêm trên máy thật |
| Q-12 Nguồn điện | Cắm sạc: tắt màn hình 15 phút, ngủ 30 phút. Pin: tắt màn hình 5 phút, ngủ 15 phút | `PowerTimeoutsTask` | — |
| Q-13 Quyền riêng tư | Chỉ 3 thiết lập HKCU có hoàn tác: ID quảng cáo, mẹo/gợi ý, trải nghiệm theo dữ liệu chẩn đoán | `WindowsCatalog` nhóm Quyền riêng tư | Không động tới telemetry cấp máy |
| Q-16 Điều khoản | Chấp nhận điều khoản nguồn và gói thay người dùng, **chỉ sau khi** kỹ thuật viên đánh dấu ô đồng ý trong hộp xác nhận cài | `ConfirmDialogViewModel.RequireAgreement` | Trách nhiệm pháp lý cần PO/Legal xác nhận trước phát hành |

## Những gì vẫn giữ nguyên (không thay đổi bởi quyết định này)

- Không tắt Defender, Tường lửa, Windows Update.
- Không tự chia ổ, định dạng, giải mã/tắt BitLocker, xóa tài khoản, khởi động lại khi chưa xác nhận.
- Không tải script hay DLL từ máy chủ.
- Chỉ báo thành công sau khi xác minh.
- Không ghi mật khẩu, token, khóa khôi phục BitLocker, product key vào log.
- Hủy không ép tắt trình cài đặt bên ngoài.
- EXE thử nghiệm **chưa ký số** (Q-06 còn mở). Không phân phối ra ngoài nhóm thử nghiệm.

## Cập nhật 18/10/2026 — Không áp dụng tài liệu 06

PO quyết định: từ nay **không áp dụng tài liệu 06 (QA, CI/CD và phát hành)**.

- Không còn bắt buộc: ghim phiên bản/lock file, kiểm tra định dạng chặn build, ký số, SBOM, quy trình phê duyệt phát hành.
- Vẫn giữ: workflow GitHub Actions như công cụ build EXE tiện lợi, kèm test tự động để bắt lỗi sớm.
- Các tài liệu 00–05 vẫn áp dụng, gồm các nguyên tắc an toàn: không tắt Defender/Tường lửa/Windows Update; không tự chia ổ, định dạng, giải mã BitLocker, xóa tài khoản hay khởi động lại khi chưa xác nhận; chỉ báo thành công sau khi xác minh; không ghi dữ liệu nhạy cảm vào log.
