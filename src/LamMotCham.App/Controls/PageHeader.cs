using System.Windows;
using System.Windows.Controls;

namespace LamMotCham.App.Controls;

/// <summary>
/// Tiêu đề trang theo thiết kế v3: ô icon, tên trang, nhãn mô-đun (tên + phiên bản), mô tả; nội dung (nút) ở bên phải.
/// Giao diện nằm trong Themes/Theme.xaml.
/// </summary>
public sealed class PageHeader : ContentControl
{
    public static readonly DependencyProperty IconKeyProperty = DependencyProperty.Register(
        nameof(IconKey), typeof(string), typeof(PageHeader), new PropertyMetadata("home"));

    public static readonly DependencyProperty HeadingProperty = DependencyProperty.Register(
        nameof(Heading), typeof(string), typeof(PageHeader), new PropertyMetadata(""));

    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(
        nameof(Description), typeof(string), typeof(PageHeader), new PropertyMetadata(""));

    public static readonly DependencyProperty ModuleLabelProperty = DependencyProperty.Register(
        nameof(ModuleLabel), typeof(string), typeof(PageHeader), new PropertyMetadata(""));

    public string IconKey
    {
        get => (string)GetValue(IconKeyProperty);
        set => SetValue(IconKeyProperty, value);
    }

    public string Heading
    {
        get => (string)GetValue(HeadingProperty);
        set => SetValue(HeadingProperty, value);
    }

    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public string ModuleLabel
    {
        get => (string)GetValue(ModuleLabelProperty);
        set => SetValue(ModuleLabelProperty, value);
    }
}
