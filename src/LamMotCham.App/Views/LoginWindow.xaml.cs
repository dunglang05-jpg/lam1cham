using System.Windows;
using LamMotCham.Presentation.ViewModels;

namespace LamMotCham.App.Views;

/// <summary>
/// Cửa sổ đăng nhập (mô-đun "account"). Mật khẩu chuyển thẳng từ PasswordBox sang ViewModel, không qua binding hay log;
/// ô mật khẩu được xóa khi ViewModel yêu cầu và khi đóng cửa sổ.
/// </summary>
public partial class LoginWindow : Window
{
    private LoginViewModel? _vm;

    public LoginWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, e) => Attach(e.NewValue as LoginViewModel);
        Loaded += (_, _) => UsernameBox.Focus();
        Closed += (_, _) =>
        {
            Attach(null);
            PasswordBox.Clear();
            PasswordConfirmBox.Clear();
        };
    }

    private void Attach(LoginViewModel? vm)
    {
        if (_vm is not null)
        {
            _vm.PasswordsCleared -= OnPasswordsCleared;
        }

        _vm = vm;
        if (_vm is not null)
        {
            _vm.PasswordsCleared += OnPasswordsCleared;
        }
    }

    private void OnPasswordsCleared(object? sender, EventArgs e)
    {
        PasswordBox.Clear();
        PasswordConfirmBox.Clear();
    }

    private void OnPasswordChanged(object sender, RoutedEventArgs e) => _vm?.SetPassword(PasswordBox.Password);

    private void OnPasswordConfirmChanged(object sender, RoutedEventArgs e) => _vm?.SetPasswordConfirm(PasswordConfirmBox.Password);
}
