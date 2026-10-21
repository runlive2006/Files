using System.Windows;

namespace Files.App;

/// <summary>
/// 通用的单行文本输入对话框，用于新建与重命名。
/// </summary>
/// <remarks>
/// 为什么 M1 不用内联重命名：内联编辑需要行视图模型实现
/// <see cref="System.ComponentModel.INotifyPropertyChanged"/>，在 10 万项目录里
/// 会为每行增加可观的内存与事件开销（参见 PERF-BASELINE 的内存预算）。
/// 待 M2 引入"仅活动行可变"的机制后再改为内联。
/// </remarks>
public partial class TextInputDialog : Window
{
    private readonly Func<string, string?>? _validate;

    /// <summary>创建对话框。</summary>
    /// <param name="title">窗口标题。</param>
    /// <param name="prompt">输入框上方的提示文字。</param>
    /// <param name="initialValue">初始值。</param>
    /// <param name="validate">校验回调；返回错误信息表示不通过，返回 <see langword="null"/> 表示通过。</param>
    public TextInputDialog(string title, string prompt, string initialValue, Func<string, string?>? validate = null)
    {
        InitializeComponent();

        Title = title;
        PromptText.Text = prompt;
        ValueBox.Text = initialValue;
        _validate = validate;

        // 选中主文件名（不含扩展名），与资源管理器的 F2 行为一致
        Loaded += (_, _) =>
        {
            ValueBox.Focus();
            int dot = initialValue.LastIndexOf('.');
            if (dot > 0)
            {
                ValueBox.Select(0, dot);
            }
            else
            {
                ValueBox.SelectAll();
            }
        };
    }

    /// <summary>用户确认的文本。</summary>
    public string Value => ValueBox.Text.Trim();

    private void OnConfirmClick(object sender, RoutedEventArgs e)
    {
        string value = Value;

        if (string.IsNullOrWhiteSpace(value))
        {
            ShowError("名称不能为空。");
            return;
        }

        string? error = _validate?.Invoke(value);
        if (error is not null)
        {
            ShowError(error);
            return;
        }

        DialogResult = true;
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
        ValueBox.Focus();
        ValueBox.SelectAll();
    }
}
