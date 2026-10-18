using System.Text;
using LamMotCham.Presentation.Modules;
using LamMotCham.Core.Logging;
using LamMotCham.Presentation.Mvvm;

namespace LamMotCham.Presentation.ViewModels;

/// <summary>Màn hình Mô-đun &amp; Cập nhật (mô-đun "modules", QĐ-002).</summary>
public sealed class ModulesViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private string _copiedText = "";

    public ModulesViewModel(MainViewModel main)
    {
        _main = main;
        CopyInfoCommand = new RelayCommand(CopyInfo);
    }

    public AppModule Module { get; } = ModuleCatalog.Modules;

    public IReadOnlyList<AppModule> Items { get; } = ModuleCatalog.All;

    public string AppVersion => "v" + _main.S.Version;

    public string Headline => $"Ứng dụng {AppVersion} · {ModuleCatalog.CodeModuleCount} mô-đun · danh mục {ModuleCatalog.Catalog.Version}";

    public RelayCommand CopyInfoCommand { get; }

    /// <summary>Phản hồi ngắn sau khi sao chép.</summary>
    public string CopiedText
    {
        get => _copiedText;
        private set => Set(ref _copiedText, value);
    }

    /// <summary>Văn bản phiên bản để dán vào báo lỗi.</summary>
    public string BuildInfoText()
    {
        var sb = new StringBuilder();
        sb.Append("Làm 1 Chạm ").AppendLine(AppVersion);
        sb.Append("Windows: ").AppendLine(_main.Machine.WindowsShort);
        foreach (var m in ModuleCatalog.All)
        {
            sb.Append("- ").Append(m.Id).Append(' ').AppendLine(m.Version);
        }

        return sb.ToString();
    }

    private void CopyInfo()
    {
        _main.Ui.SetClipboard(BuildInfoText());
        CopiedText = "Đã sao chép thông tin phiên bản.";
        _main.S.Log.Info("Đã sao chép thông tin phiên bản mô-đun.");
    }
}
