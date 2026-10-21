using System.Windows;

namespace Files.App;

/// <summary>
/// 为 <see cref="System.Windows.Controls.GridViewColumn"/> 附加"排序列名"。
/// </summary>
/// <remarks>
/// <c>GridViewColumn</c> 派生自 <see cref="DependencyObject"/> 而不是 <c>FrameworkElement</c>，
/// 因此没有 <c>Tag</c> 属性，必须用附加属性承载这类元数据。
/// </remarks>
public static class ColumnSort
{
    /// <summary>排序列名的附加属性。</summary>
    public static readonly DependencyProperty KeyProperty = DependencyProperty.RegisterAttached(
        "Key",
        typeof(string),
        typeof(ColumnSort),
        new PropertyMetadata(null));

    /// <summary>设置排序列名。</summary>
    public static void SetKey(DependencyObject element, string? value) =>
        element.SetValue(KeyProperty, value);

    /// <summary>读取排序列名。</summary>
    public static string? GetKey(DependencyObject element) =>
        (string?)element.GetValue(KeyProperty);
}
