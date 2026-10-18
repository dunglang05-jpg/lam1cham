using System.Windows;
using System.Windows.Controls;

namespace LamMotCham.App.Controls;

/// <summary>
/// Tiêu đề trang theo thiết kế canvas: nhãn nhỏ (eyebrow), tên trang, nhãn mô-đun (tên + phiên bản), mô tả; nội dung (nút) ở bên phải.
/// IconKey giữ lại để tương thích, không còn hiển thị.
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

    public static readonly DependencyProperty EyebrowProperty = DependencyProperty.Register(
        nameof(Eyebrow), typeof(string), typeof(PageHeader), new PropertyMetadata(""));

    /// <summary>Nhãn nhỏ viết hoa phía trên tiêu đề (thiết kế canvas: .eyebrow).</summary>
    public string Eyebrow
    {
        get => (string)GetValue(EyebrowProperty);
        set => SetValue(EyebrowProperty, value);
    }

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
