# 07 — Kế hoạch MVP (Giai đoạn 1)

> Trạng thái: **DỰ THẢO — chờ Product Owner và Tech Lead duyệt.**
> Mọi mục đánh dấu ❓ là câu hỏi mở (xem mục 6). Chưa được viết code cho task thay đổi hệ thống khi câu hỏi liên quan chưa được trả lời.
> Ngày lập: 18/10/2026.

## 1. Mục tiêu MVP

Phát hành nội bộ một file EXE portable cho Windows 11 x64 giúp nhân viên:

1. Xem nhanh thông tin và trạng thái sẵn sàng của laptop.
2. Cài bộ phần mềm thiết yếu qua WinGet, có xác minh sau cài.
3. Áp dụng một số cấu hình Windows cơ bản, an toàn, có xác minh.
4. Chạy các kiểm tra chỉ-đọc (BitLocker, kích hoạt, Defender, Windows Update, ổ đĩa).
5. Xuất báo cáo cục bộ cho từng máy.

MVP **không** có Web Admin, Pro, thanh toán, feature flags từ xa và tự cập nhật. Các phần đó thuộc giai đoạn 3–4.

## 2. Phạm vi

### 2.1. Trong phạm vi MVP

| Nhóm | Nội dung | Loại tác động |
|---|---|---|
| Nền tảng | EXE portable self-contained win-x64, log có cấu trúc, redact, thư mục dữ liệu có giới hạn dung lượng | — |
| Task Engine | Hợp đồng task, 10 trạng thái chuẩn, hàng đợi, phụ thuộc, timeout, retry có backoff, hủy, checkpoint reboot, resource governor | — |
| Tổng quan | Tên máy, model, CPU, RAM, ổ đĩa, phiên bản Windows, WinGet có/không | Chỉ đọc |
| Phần mềm thiết yếu | Danh sách chung một màn hình; cài tuần tự qua WinGet; verify sau cài | Thay đổi hệ thống |
| Cấu hình Windows | Múi giờ Việt Nam + đồng bộ giờ; hiện phần mở rộng file; biểu tượng This PC/Recycle Bin/Control Panel; chế độ nguồn và thời gian tắt màn hình/ngủ | Thay đổi hệ thống |
| Kiểm tra | BitLocker/Device Encryption, Windows Activation, Defender, Firewall, Windows Update (chỉ đọc), dung lượng ổ | Chỉ đọc |
| Gói nhanh | Thiết lập đầy đủ, Chỉ cài phần mềm, Chỉ tối ưu Windows, Chỉ kiểm tra/bàn giao | — |
| Báo cáo | Báo cáo cục bộ (JSON + HTML đọc được), không upload | Ghi file cục bộ |

### 2.2. Ngoài phạm vi MVP (dời sang giai đoạn sau)

- Chia/format ổ đĩa (chỉ hiển thị thông tin).
- Gỡ antivirus dùng thử, dọn dẹp file tạm có xóa.
- Kiểm tra phần cứng tương tác (camera, micro, loa, bàn phím, touchpad) — giai đoạn 2.
- Đổi tên máy — chờ quy tắc đặt tên ❓Q-07.
- Quyền riêng tư/thông báo — chờ định nghĩa "mức an toàn" ❓Q-13.
- Web Admin, feature flags, catalog từ xa, Pro, thanh toán, tự cập nhật.

## 3. Kiến trúc MVP (tóm tắt)

```
LamMotCham.App (WPF, MVVM)        ← chỉ hiển thị và lựa chọn, không gọi Windows API trực tiếp
        │
LamMotCham.Core                    ← không phụ thuộc Windows; test được trên mọi nền tảng
  ├─ Tasks: ITaskModule, TaskState, TaskResult, RetryPolicy
  ├─ Orchestration: TaskOrchestrator (tuần tự, phụ thuộc, timeout, retry, hủy)
  ├─ Resources: ResourceGovernor (ngưỡng + hysteresis + cooldown)
  ├─ Logging: Redactor, giới hạn kích thước
  └─ Reporting: RunReport
        │
LamMotCham.Windows                 ← System Probe, WinGet Adapter, các task Windows
        │
(Giai đoạn sau) Elevated Helper    ← xem ADR-002 (đề xuất)
```

Quyết định công nghệ: xem `docs/adr/ADR-001-Cong-Nghe-Nen.md`. Mô hình nâng quyền: `docs/adr/ADR-002-Mo-Hinh-Nang-Quyen.md`.

## 4. Lộ trình theo mốc

Không gắn ngày cố định; team ước lượng sau khi câu hỏi mở được trả lời. Mỗi mốc kết thúc bằng demo và checklist DoD.

| Mốc | Nội dung | Phụ thuộc câu hỏi |
|---|---|---|
| M0 — Nền móng | Repo, CI, Task Engine lõi, unit test, WPF shell | — (đã bắt đầu) |
| M1 — Chỉ đọc | System Probe, các kiểm tra chỉ-đọc, báo cáo cục bộ | Q-08 |
| M2 — Cài phần mềm | WinGet adapter, catalog allowlist, verify sau cài | Q-02, Q-03, Q-04, Q-05, Q-11, Q-16 |
| M3 — Cấu hình Windows | 4 task cấu hình có verify, idempotent | Q-05, Q-12 |
| M4 — Gói nhanh & UI | Gói nhanh, xem chi tiết/bỏ chọn, cảnh báo, hướng xử lý lỗi | Q-09 |
| M5 — Chất lượng phát hành nội bộ | Ma trận môi trường, đo hiệu năng, ký số, SBOM, notices, bản nội bộ | Q-01, Q-06, Q-10 |

## 5. Backlog MVP và tiêu chí nghiệm thu

Quy ước mức rủi ro: **R0** chỉ đọc · **R1** thay đổi cấu hình có thể hoàn tác · **R2** cài/gỡ phần mềm · **R3** có thể gây mất dữ liệu (không có trong MVP).

### E1 — Nền tảng và Task Engine (M0)

**LMC-001 Hợp đồng task** · R0
- Mỗi task khai báo: ID, tên, phiên bản, mức rủi ro, quyền cần thiết, phụ thuộc, timeout, retry policy, khả năng rollback hoặc lý do không thể rollback, hành vi khi cần reboot.
- Task không khai báo đủ trường bắt buộc thì bị từ chối khi đăng ký vào hàng đợi (test).

**LMC-002 Vòng đời trạng thái** · R0
- Chỉ dùng 10 trạng thái chuẩn trong tài liệu 04.
- `SUCCESS` chỉ được gán sau khi Verify trả về đạt. Execute báo thành công nhưng Verify không đạt → `FAILED` (test).
- Verify không xác định được → `NEEDS_ATTENTION`, không bao giờ `SUCCESS` (test).

**LMC-003 Điều phối tuần tự và phụ thuộc** · R0
- Mặc định chỉ một task loại installer chạy tại một thời điểm (test).
- Task phụ thuộc vào task lỗi/bị hủy/bị bỏ qua → `SKIPPED` kèm lý do nêu tên task gốc (test).
- Task độc lập tiếp tục chạy khi task khác lỗi (test).
- Phụ thuộc vòng hoặc phụ thuộc vào task không tồn tại → từ chối hàng đợi trước khi chạy (test).

**LMC-004 Timeout, retry, hủy** · R0
- Vượt timeout → task bị hủy, trạng thái `FAILED` với lý do timeout (test).
- Retry chỉ cho lỗi được đánh dấu tạm thời, có giới hạn số lần và backoff tăng dần (test).
- Lỗi không tạm thời không bao giờ retry (test).
- Người dùng hủy → task đang chạy nhận cancellation; task chưa chạy → `CANCELLED` (test).
- Một task ném exception không làm dừng orchestrator (test).

**LMC-005 Checkpoint reboot** · R0
- Task báo cần reboot → trạng thái `WAITING_FOR_REBOOT`, hàng đợi dừng tại đó, không tự reboot (test).

**LMC-006 Precondition và chạy lặp** · R0
- Precondition không đạt → `SKIPPED` hoặc `NOT_SUPPORTED` kèm lý do, không gọi Execute (test).
- Task tự phát hiện trạng thái đã đúng (đã cài, đã cấu hình) → không thực hiện lại, kết quả ghi rõ "đã ở trạng thái mong muốn" (test).

**LMC-007 Resource governor** · R0
- Khi RAM khả dụng dưới ngưỡng bắt đầu, orchestrator không khởi chạy task mới; chỉ tiếp tục khi vượt ngưỡng phục hồi (cao hơn ngưỡng bắt đầu) và đã qua cooldown (test).
- Không giết tiến trình nào.
- Ngưỡng là cấu hình, giá trị khởi điểm 15% / 20% chỉ để thử nghiệm (theo tài liệu 04).

**LMC-008 Log có cấu trúc và redact** · R0
- Log không chứa mật khẩu, token, khóa khôi phục BitLocker, chuỗi dạng khóa sản phẩm (test với mẫu dữ liệu).
- Log có giới hạn dung lượng và xoay vòng.

**LMC-009 CI cho mỗi PR** · R0
- Build Release, unit test, kiểm tra format, upload kết quả test. PR không được ký số.
- Bổ sung SAST, dependency/license scan, secret scan trước M5 ❓Q-15.

### E2 — Chỉ đọc và báo cáo (M1)

**LMC-101 System Probe** · R0
- Hiển thị tên máy, nhà sản xuất/model, CPU, RAM, danh sách ổ đĩa và dung lượng trống, phiên bản/build Windows, WinGet có/không và phiên bản.
- Thiếu quyền hoặc API lỗi → hiển thị "Không đọc được" kèm lý do, không làm treo app.

**LMC-102 Kiểm tra chỉ-đọc** · R0
- BitLocker/Device Encryption: Bật / Tắt / Không xác định. Không bao giờ hiển thị hay ghi log khóa khôi phục.
- Windows Activation, Defender, Firewall, Windows Update: chỉ đọc trạng thái.
- Mỗi mục phân biệt rõ: đã tự kiểm tra, cần kiểm tra vật lý, không hỗ trợ, chưa kiểm tra.

**LMC-103 Báo cáo cục bộ** · R0
- Mỗi lần chạy tạo một báo cáo JSON và một bản HTML đọc được, chứa thông tin máy, từng task, trạng thái, thời gian, lý do lỗi.
- Không upload. Vị trí lưu theo ❓Q-08.

### E3 — Cài phần mềm thiết yếu (M2)

**LMC-201 Catalog allowlist** · R2
- Catalog đóng gói trong EXE, mỗi mục có: package ID WinGet, publisher kỳ vọng, nguồn (`winget`), scope, tham số cho phép, có chọn mặc định hay không.
- Package ID và publisher đã được xác minh và PO ký duyệt ❓Q-02.

**LMC-202 WinGet adapter** · R2
- Gọi WinGet bằng danh sách tham số tách rời, không ghép chuỗi vào shell (test bảo mật với ID chứa ký tự đặc biệt).
- Chỉ chấp nhận package ID có trong allowlist (test).
- Kiểm tra WinGet có sẵn và phiên bản tối thiểu trước khi chạy; không có → `NOT_SUPPORTED` kèm hướng dẫn ❓Q-03.
- Việc chấp nhận điều khoản nguồn/gói thay người dùng chờ quyết định ❓Q-16.

**LMC-203 Verify sau cài** · R2
- Sau khi WinGet báo thành công, kiểm tra lại gói thực sự đã cài với đúng package ID. Không xác minh được → không báo `SUCCESS`.
- Gói đã cài sẵn → không cài lại, ghi "đã cài sẵn".
- Mã lỗi WinGet được ánh xạ sang thông báo dễ hiểu và hướng xử lý; danh sách mã được xem là lỗi tạm thời ❓Q-11.

**LMC-204 Tải trước song song (tùy chọn)** · R2
- Chỉ làm nếu đo đạc cho thấy lợi ích; mặc định tắt trong MVP.

### E4 — Cấu hình Windows cơ bản (M3)

Mỗi task: precondition, execute, verify, rollback (ghi lại giá trị cũ trước khi đổi), idempotent.

**LMC-301 Múi giờ Việt Nam và đồng bộ giờ** · R1
- Đặt múi giờ "SE Asia Standard Time" (UTC+07:00); verify bằng cách đọc lại múi giờ hệ thống.
- Đồng bộ giờ: chỉ yêu cầu đồng bộ, không đổi máy chủ thời gian. Mất mạng → `NEEDS_ATTENTION` kèm hướng dẫn.

**LMC-302 Hiện phần mở rộng file** · R1
- Áp dụng cho người dùng hiện tại; verify đọc lại giá trị; rollback về giá trị cũ.

**LMC-303 Biểu tượng desktop** · R1
- Người dùng chọn từng biểu tượng; chạy lại không tạo trùng.

**LMC-304 Nguồn điện và thời gian tắt màn hình/ngủ** · R1
- Giá trị mặc định do PO quyết định ❓Q-12; verify đọc lại giá trị.

### E5 — Giao diện và gói nhanh (M4)

**LMC-401 Màn hình chính**
- Một màn hình chứa: thông tin máy, gói nhanh, danh sách task (có thể bỏ chọn), tiến độ, nút Bắt đầu/Hủy, nút xem log/báo cáo.
- Không có thao tác nào chặn UI thread quá 200 ms (đo bằng công cụ, không ước lượng).

**LMC-402 Gói nhanh**
- 4 gói theo tài liệu 03; chọn gói chỉ thay đổi tập task được chọn, người dùng vẫn bỏ chọn được.

**LMC-403 Cảnh báo và xác nhận**
- Trước khi bắt đầu, hiển thị tóm tắt: số task, task cần quyền quản trị, task R2 trở lên; yêu cầu xác nhận một lần.
- Lỗi hiển thị bằng tiếng Việt dễ hiểu kèm hướng xử lý ❓Q-09.

### E6 — Phát hành nội bộ (M5)

**LMC-501** Build single-file self-contained win-x64, đo cold start và idle RAM trên máy tham chiếu ❓Q-10.
**LMC-502** Ký số EXE trong release workflow được bảo vệ ❓Q-06.
**LMC-503** SBOM, THIRD-PARTY-NOTICES, checksum SHA-256, release notes.
**LMC-504** Chạy ma trận môi trường tối thiểu (tài liệu 06) và QA ký duyệt.

## 6. Câu hỏi mở cần quyết định

| Mã | Câu hỏi | Người quyết | Ảnh hưởng | Đề xuất của team kỹ thuật |
|---|---|---|---|---|
| Q-01 | Windows 10 có trong phạm vi hỗ trợ không? (Windows 10 đã hết hỗ trợ thông thường từ 14/10/2025) | PO | Ma trận test, API dùng được | Chỉ Windows 11 x64 cho MVP |
| Q-02 | Danh sách phần mềm cuối cùng: bộ gõ tiếng Việt nào, trình đọc PDF nào, Brave có hay không? | PO | Catalog, test | Team xác minh package ID/publisher trên WinGet rồi trình duyệt |
| Q-03 | Máy không có WinGet hoặc WinGet lỗi thì xử lý thế nào? | PO + TL | Độ phủ, bảo mật | MVP: báo `NOT_SUPPORTED` + hướng dẫn cập nhật App Installer; không tự tải bộ cài ngoài |
| Q-04 | Cài phần mềm cho toàn máy hay chỉ người dùng hiện tại? | PO + TL | Quyền, nâng quyền | Toàn máy khi gói hỗ trợ |
| Q-05 | Duyệt mô hình nâng quyền (ADR-002) | TL | Kiến trúc, bảo mật | Helper riêng, lệnh dạng enum, IPC giới hạn |
| Q-06 | Loại chứng chỉ ký số và ai giữ khóa? | PO + Security | SmartScreen, phát hành | Cần quyết trước M5 |
| Q-07 | Quy tắc đặt tên máy | PO | LMC đổi tên máy (dời) | — |
| Q-08 | Lưu log/báo cáo ở đâu: cạnh file EXE (USB) hay `%LOCALAPPDATA%`? Giữ bao lâu? | PO | Portable, quyền ghi, privacy | `%LOCALAPPDATA%\LamMotCham`, có nút xuất báo cáo ra thư mục người dùng chọn |
| Q-09 | Giao diện chỉ tiếng Việt hay cả tiếng Anh? | PO | UI, test | Chỉ tiếng Việt, chuỗi tách riêng để dịch sau |
| Q-10 | Cấu hình máy tham chiếu để đo hiệu năng | TL + QA | Ngưỡng ký duyệt | — |
| Q-11 | Mã lỗi WinGet nào được xem là lỗi tạm thời để retry? | TL | Retry policy | Team lập danh sách từ tài liệu WinGet, trình duyệt |
| Q-12 | Giá trị mặc định nguồn điện / tắt màn hình / ngủ (cắm sạc và dùng pin) | PO | LMC-304 | — |
| Q-13 | "Quyền riêng tư/thông báo ở mức an toàn" gồm những thiết lập cụ thể nào? | PO + Security | Dời khỏi MVP đến khi rõ | — |
| Q-14 | MVP có telemetry không? | PO | Privacy notice | Không có telemetry trong MVP |
| Q-15 | Công cụ SAST, license scan, secret scan dùng gì? | TL + DevOps | CI | Chọn trước M5 |
| Q-16 | Ứng dụng có được chấp nhận điều khoản nguồn/gói WinGet thay người dùng không? | PO (pháp lý) | LMC-202 | Hiển thị điều khoản và xin xác nhận một lần trước khi cài |

## 7. Rủi ro chính

| Rủi ro | Tác động | Giảm thiểu |
|---|---|---|
| EXE chưa ký bị SmartScreen/antivirus chặn | Không chạy được trên máy khách | Ký số trước bản nội bộ đầu tiên; theo dõi false positive |
| WPF self-contained single-file lớn và khởi động chậm | Không đạt cold start ≤2 s | Đo sớm từ M0 trên máy tham chiếu; trimming không hỗ trợ đầy đủ cho WPF nên cần thử các cấu hình nén |
| WinGet source lỗi, mạng chậm, hoặc gói đổi publisher | Cài thất bại hoặc cài nhầm | Allowlist ID + publisher, verify sau cài, retry có giới hạn |
| Nâng quyền sai cách | Lỗ hổng leo thang đặc quyền | ADR-002, review bảo mật bắt buộc |
| Báo thành công giả | Bàn giao máy lỗi | Verify bắt buộc; test cho từng task |
| Thiếu máy thật để test ma trận | Lỗi chỉ lộ khi triển khai | Chuẩn bị máy ảo + ít nhất 2 laptop thật trước M5 |

## 8. Việc đã bắt đầu trong M0

Khung code trong repository này gồm Task Engine lõi và unit test. Chưa có task nào thay đổi hệ thống thật; các task mẫu chỉ dùng trong test.
