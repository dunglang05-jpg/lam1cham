using System.Windows;
using System.Windows.Controls;
using LamMotCham.Presentation.ViewModels;

namespace LamMotCham.App.Views.Dialogs;

/// <summary>Hộp sửa tài khoản. Mật khẩu chuyển thẳng từ PasswordBox sang ViewModel, không qua binding hay log.</summary>
public partial class UserEditDialog : UserControl
{
    public UserEditDialog()
    {
        InitializeComponent();
        Unloaded += (_, _) =>
        {
            Password.Clear();
            PasswordConfirm.Clear();
        };
    }

    private void OnPasswordChanged(object sender, RoutedEventArgs e) =>
        (DataContext as UserEditDialogViewModel)?.SetPassword(Password.Password);

    private void OnPasswordConfirmChanged(object sender, RoutedEventArgs e) =>
        (DataContext as UserEditDialogViewModel)?.SetPasswordConfirm(PasswordConfirm.Password);
}
