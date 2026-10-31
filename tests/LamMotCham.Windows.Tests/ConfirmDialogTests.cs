using LamMotCham.Presentation.ViewModels;
using Xunit;

namespace LamMotCham.Windows.Tests;

/// <summary>Lỗi 1.1.1: tick "Đồng ý…" trong hộp Xác nhận cài đặt nhưng nút xác nhận vẫn mờ.</summary>
public class ConfirmDialogTests
{
    private static ConfirmDialogViewModel Create(bool requireAgreement) =>
        new(null!, "Xác nhận cài đặt", "download", "Xác nhận cài đặt", () => Task.CompletedTask)
        {
            RequireAgreement = requireAgreement,
            AgreementText = "Đồng ý",
        };

    [Fact]
    public void Tick_dong_y_bat_nut_xac_nhan_va_bao_giao_dien()
    {
        var vm = Create(requireAgreement: true);
        var raised = 0;
        vm.ConfirmCommand.CanExecuteChanged += (_, _) => raised++;

        Assert.False(vm.ConfirmCommand.CanExecute(null));

        vm.Agreed = true;
        Assert.True(raised > 0);
        Assert.True(vm.ConfirmCommand.CanExecute(null));

        var before = raised;
        vm.Agreed = false;
        Assert.True(raised > before);
        Assert.False(vm.ConfirmCommand.CanExecute(null));
    }

    [Fact]
    public void Khong_can_dong_y_thi_nut_bat_san()
    {
        var vm = Create(requireAgreement: false);
        Assert.True(vm.ConfirmCommand.CanExecute(null));
    }

    [Fact]
    public void Gan_lai_cung_gia_tri_khong_bao_thua()
    {
        var vm = Create(requireAgreement: true);
        var raised = 0;
        vm.ConfirmCommand.CanExecuteChanged += (_, _) => raised++;
        vm.Agreed = false;
        Assert.Equal(0, raised);
    }
}
