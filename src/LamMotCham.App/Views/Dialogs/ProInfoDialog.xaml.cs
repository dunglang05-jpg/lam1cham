using System.Windows;

namespace LamMotCham.App.Views.Dialogs;

/// <summary>
/// Informational Pro preview only. Billing, entitlement validation, and paid feature gates
/// are not connected in this application build.
/// </summary>
public partial class ProInfoDialog : Window
{
    public ProInfoDialog()
    {
        InitializeComponent();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
