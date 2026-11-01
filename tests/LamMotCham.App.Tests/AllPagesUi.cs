using System.Text;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LamMotCham.App.Views;
using LamMotCham.App.Views.Pages;
using LamMotCham.Core.Orchestration;
using LamMotCham.Core.Tasks;
using LamMotCham.Presentation.Services;
using LamMotCham.Presentation.ViewModels;
using Xunit;

namespace LamMotCham.App.Tests;

/// <summary>
/// Dựng thật từng trang của ứng dụng (Tổng quan, Phần mềm, Cài đặt &amp; Tối ưu, Ổ đĩa &amp; Tài khoản, Dọn dẹp, Bàn giao, Mô-đun)
/// với dữ liệu thật của máy, và hộp tiến độ lúc đang chạy; bắt mọi lỗi binding (chữ không hiện, nút không chạy…).
/// </summary>
internal static class AllPagesUi
{
    private sealed class NullUi : IUiPlatform
    {
        public void Shutdown()
        {
        }

        public void SetClipboard(string text)
        {
        }

        public void RestartForSignIn()
        {
        }
    }

    /// <summary>Gom lỗi binding WPF (mức Error) để báo trong kiểm thử.</summary>
    private sealed class BindingErrors : TraceListener
    {
        private readonly StringBuilder _line = new();

        public List<string> Errors { get; } = [];

        public override void Write(string? message) => _line.Append(message);

        public override void WriteLine(string? message)
        {
            _line.Append(message);
            var text = _line.ToString();
            _line.Clear();

            // Trang dựng riêng (không nằm trong MainWindow) nên binding tìm cửa sổ cha là bình thường.
            if (!text.Contains("AncestorType='System.Windows.Window'", StringComparison.Ordinal))
            {
                Errors.Add(text);
            }
        }
    }

    public static void Check(Action<string> stage)
    {
        var errors = new BindingErrors();
        PresentationTraceSources.Refresh();
        PresentationTraceSources.DataBindingSource.Listeners.Add(errors);
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
        try
        {
            stage("tạo dịch vụ ứng dụng");

            // Dùng danh sách ứng dụng giống Admin (có Office, Kaspersky, ESET nhiều phiên bản) để dựng trang Phần mềm như thật.
            if (FindUp("tools/LamMotCham.AppCheck/apps-admin.json") is { } adminList)
            {
                LamMotCham.Windows.Software.SoftwareCatalog.Use(LamMotCham.Windows.Software.RemoteCatalog.Parse(File.ReadAllText(adminList)).Packages);
            }

            using var services = AppServices.Create();
            using var vm = new MainViewModel(services, new NullUi());
            var views = new Dictionary<Type, Func<FrameworkElement>>
            {
                [typeof(OverviewViewModel)] = () => new OverviewPage(),
                [typeof(SoftwareViewModel)] = () => new SoftwarePage(),
                [typeof(WindowsViewModel)] = () => new WindowsPage(),
                [typeof(DiskViewModel)] = () => new DiskPage(),
                [typeof(CleanupViewModel)] = () => new CleanupPage(),
                [typeof(HandoverViewModel)] = () => new HandoverPage(),
                [typeof(ModulesViewModel)] = () => new ModulesPage(),
            };

            foreach (var page in Enum.GetValues<LamMotCham.Presentation.ViewModels.Page>())
            {
                stage("trang " + page);
                vm.CurrentPage = page;
                var pageVm = vm.CurrentPageViewModel;
                Assert.True(views.TryGetValue(pageVm.GetType(), out var make), $"Trang {page} chưa có giao diện.");
                var view = make!();
                view.DataContext = pageVm;
                Layout(view);
                Assert.True(ConfirmDialogUiTests.Descendants<TextBlock>(view).Any(t => !string.IsNullOrWhiteSpace(t.Text)), $"Trang {page} không hiện chữ nào.");
            }

            // Hộp tiến độ khi đang chạy: một app đang tải, một app chờ, một tác vụ tối ưu đang thực hiện.
            stage("hộp tiến độ");
            var app = vm.Rows.Values.First(r => r.Entry.Category == TaskCategory.Software);
            var waiting = vm.Rows.Values.Where(r => r.Entry.Category == TaskCategory.Software).Skip(1).First();
            var setting = vm.Rows.Values.First(r => r.Entry.Category == TaskCategory.WindowsSettings);
            foreach (var r in new[] { app, waiting, setting })
            {
                r.ResetForRun();
                vm.RunRows.Add(r);
            }

            app.Apply(new TaskStatusUpdate(app.Id, TaskState.Running, "Đang tải 45%", 1, 0.4));
            waiting.Apply(new TaskStatusUpdate(waiting.Id, TaskState.Pending, null, 0));
            setting.Apply(new TaskStatusUpdate(setting.Id, TaskState.Running, "Đang thực hiện", 1, 0.3));
            var overlay = new RunOverlay { DataContext = vm };
            Layout(overlay);
            var texts = ConfirmDialogUiTests.Descendants<TextBlock>(overlay).Select(t => t.Text).ToList();
            Assert.Contains(texts, t => t.StartsWith("Đang tải 45% · ", StringComparison.Ordinal));
            Assert.Contains(texts, t => t == "Chờ đến lượt");
            Assert.Contains(texts, t => t.StartsWith("Đang thực hiện · ", StringComparison.Ordinal));
            Assert.DoesNotContain(texts, t => t.Contains(" · ", StringComparison.Ordinal) && t.Split(" · ").Length > 2);
            if (Environment.GetEnvironmentVariable("L1C_ANH_CHUP") is { Length: > 0 } dir)
            {
                Save(overlay, 1366, 768, Path.Combine(dir, "1366x768-9-HopTienDo.png"));
            }

            stage("kiểm tra lỗi binding");
            Assert.True(errors.Errors.Count == 0, "Lỗi binding trên giao diện:\n" + string.Join("\n", errors.Errors.Distinct().Take(20)));

            // Chụp sau khi đã kiểm tra lỗi binding: khung chính dựng ngoài cửa sổ nên các binding tìm cửa sổ cha báo lỗi giả.
            PresentationTraceSources.DataBindingSource.Listeners.Remove(errors);
            // Ảnh chụp toàn bộ khung ứng dụng (thanh bên + trang) ở 2 cỡ màn hình laptop phổ biến, để soát giao diện bằng mắt.
            if (Environment.GetEnvironmentVariable("L1C_ANH_CHUP") is { Length: > 0 } shots)
            {
                Directory.CreateDirectory(shots);
                // Trang dựng riêng (MainWindow cần icon trong gói EXE, trình chạy test không có): bề rộng = màn hình trừ thanh bên.
                foreach (var (w, h) in new[] { (1366, 768), (1920, 1080) })
                {
                    foreach (var page in Enum.GetValues<LamMotCham.Presentation.ViewModels.Page>())
                    {
                        stage($"chụp {page} {w}x{h}");
                        vm.CurrentPage = page;
                        var pageVm = vm.CurrentPageViewModel;
                        var view = views[pageVm.GetType()]();
                        view.DataContext = pageVm;
                        Save(view, w - 260, h - 70, Path.Combine(shots, $"{w}x{h}-{(int)page}-{page}.png"));
                    }
                }

                // Trang Phần mềm khi mở ô chọn phiên bản của Kaspersky (mặc định chỉ hiện "Phiên bản ▾").
                if (vm.Rows.Values.FirstOrDefault(r => r.HasVariants) is { } multi)
                {
                    multi.VariantsOpen = true;
                    vm.CurrentPage = LamMotCham.Presentation.ViewModels.Page.Software;
                    var view = views[vm.CurrentPageViewModel.GetType()]();
                    view.DataContext = vm.CurrentPageViewModel;
                    Save(view, 1366 - 260, 1400, Path.Combine(shots, "1366x768-1b-Software-mo-phien-ban.png"));
                    multi.VariantsOpen = false;
                }

                vm.CurrentPage = LamMotCham.Presentation.ViewModels.Page.Overview;
            }

        }
        finally
        {
            PresentationTraceSources.DataBindingSource.Listeners.Remove(errors);
        }
    }

    /// <summary>Bố cục rồi vẽ ra PNG (nền trắng như cửa sổ).</summary>
    private static void Save(FrameworkElement view, int width, int height, string path)
    {
        view.Measure(new Size(width, height));
        view.Arrange(new Rect(0, 0, width, height));
        view.UpdateLayout();
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
            // Không co giãn: ảnh đúng kích thước thật của giao diện.
            dc.DrawRectangle(new VisualBrush(view) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top }, null, new Rect(0, 0, width, height));
        }

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var file = File.Create(path);
        encoder.Save(file);
    }

    private static string? FindUp(string relative)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static void Layout(FrameworkElement view)
    {
        view.Measure(new Size(1280, 900));
        view.Arrange(new Rect(0, 0, 1280, 900));
        view.UpdateLayout();
    }
}
