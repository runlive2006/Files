using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Files.App.ViewModels;
using Files.Core.Services;
using Files.Core.Services.Settings;

namespace Files.App.Views;

/// <summary>
/// 设置窗口。
/// </summary>
/// <remarks>
/// 界面完全由 <see cref="SettingCatalog"/> 的元数据驱动，
/// 新增设置项只需在目录里加一条，无需改动 XAML。
/// <para>
/// 交互规则（设计文档 SETTINGS §4.2）：
/// 视图类设置改动后<b>立即作用于所有窗口</b>，因此「取消」只回滚那些尚未即时生效的项，
/// 并明确提示这一点，避免用户误以为全部撤销了。
/// </para>
/// </remarks>
public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _viewModel;
    private readonly SettingsService _service;

    /// <summary>打开设置窗口。</summary>
    public SettingsWindow(SettingsService service)
    {
        ArgumentNullException.ThrowIfNull(service);

        InitializeComponent();

        _service = service;
        _viewModel = new SettingsViewModel(service);
        _viewModel.SettingChanged += OnSettingChanged;

        DataContext = _viewModel;
    }

    /// <summary>某项设置已变更（供主窗口即时应用）。</summary>
    public event EventHandler<string>? SettingChanged;

    private void OnSettingChanged(object? sender, string key)
    {
        SettingChanged?.Invoke(this, key);
    }

    private void OnConfirmClick(object sender, RoutedEventArgs e)
    {
        try
        {
            _viewModel.Save();
            DialogResult = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(
                this,
                $"无法保存设置：{ex.Message}",
                "设置",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;

    private void OnResetGroupClick(object sender, RoutedEventArgs e)
    {
        string group = _viewModel.SelectedGroup;

        if (MessageBox.Show(
                this,
                $"确定要把「{group}」分组的设置恢复为默认值吗？",
                "恢复默认",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        _viewModel.ResetCurrentGroup();
    }

    private void OnOpenConfigDirClick(object sender, RoutedEventArgs e)
    {
        string? directory = Path.GetDirectoryName(_service.FilePath);

        if (string.IsNullOrEmpty(directory))
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(directory);
            Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(
                this,
                $"无法打开目录：{ex.Message}",
                "设置",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }
}
