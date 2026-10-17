# ADR-001 — Công nghệ nền cho ứng dụng desktop

- Trạng thái: **Đề xuất — chờ Tech Lead duyệt**
- Ngày: 18/10/2026
- Liên quan: tài liệu 02 (Kiến trúc), 04 (Hiệu năng), 06 (CI/CD)

## Bối cảnh

Ứng dụng phải là một file EXE portable trên Windows x64, không yêu cầu người dùng cài runtime riêng, giao diện không bị treo khi cài phần mềm, và phần logic điều phối phải kiểm thử được tự động trong CI.

## Quyết định đề xuất

1. **Ngôn ngữ và runtime:** C# trên .NET 10 (LTS). Tại thời điểm lập ADR, Microsoft công bố .NET 10 được hỗ trợ đến 14/11/2028 và bản vá mới nhất là 10.0.12 (08/09/2026). Phiên bản SDK được ghim trong `global.json`; luôn dùng bản vá mới nhất khi phát hành.
2. **UI:** WPF + MVVM. Ở M0 dùng lớp `ObservableObject`/`RelayCommand` tự viết tối thiểu để tránh thêm phụ thuộc; có thể chuyển sang CommunityToolkit.Mvvm (MIT) bằng một ADR riêng nếu cần.
3. **Đóng gói:** `win-x64`, self-contained, single-file, `IncludeNativeLibrariesForSelfExtract=true`. Không dùng trimming cho WPF cho đến khi được đo và kiểm thử.
4. **Tách lớp:**
   - `LamMotCham.Core` (`net10.0`): hợp đồng task, orchestrator, resource governor, redaction, mô hình báo cáo. Không phụ thuộc Windows để chạy unit test trên mọi agent.
   - `LamMotCham.Windows` (`net10.0-windows`): System Probe, WinGet adapter, task Windows.
   - `LamMotCham.App` (`net10.0-windows`, WPF): chỉ UI và ViewModel.
5. **Kiểm thử:** xUnit. CI chạy trên `windows-latest`.
6. **Chất lượng mã:** Nullable bật, cảnh báo là lỗi, .NET analyzers mức `Recommended`, `.editorconfig` và `dotnet format --verify-no-changes` trong CI.

## Hệ quả

- Ưu: một ngôn ngữ cho toàn bộ desktop; Core test được không cần Windows; không cần runtime trên máy khách.
- Nhược: file EXE self-contained dung lượng lớn (thường vài chục đến hơn 100 MB với WPF); cold start của single-file phải được đo sớm (rủi ro với mục tiêu ≤2 s).
- Mọi phụ thuộc NuGet mới phải ghi license và đưa vào THIRD-PARTY-NOTICES.

## Phương án đã cân nhắc

- WinUI 3: hiện đại hơn nhưng đóng gói portable unpackaged phức tạp hơn và cần Windows App SDK runtime; không chọn cho MVP.
- WinForms: đơn giản nhưng khó đạt giao diện theo định hướng thiết kế; không chọn.
