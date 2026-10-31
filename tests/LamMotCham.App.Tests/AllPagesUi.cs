using System.Text;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
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

            foreach (var page in Enum.GetValues<Page>())
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

            stage("kiểm tra lỗi binding");
            Assert.True(errors.Errors.Count == 0, "Lỗi binding trên giao diện:\n" + string.Join("\n", errors.Errors.Distinct().Take(20)));
        }
        finally
        {
            PresentationTraceSources.DataBindingSource.Listeners.Remove(errors);
        }
    }

    private static void Layout(FrameworkElement view)
    {
        view.Measure(new Size(1280, 900));
        view.Arrange(new Rect(0, 0, 1280, 900));
        view.UpdateLayout();
    }
}
