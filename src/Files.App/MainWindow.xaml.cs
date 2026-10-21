using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows;
using Files.Core.Models;
using Files.Core.Services;

namespace Files.App;

/// <summary>
/// 文件管理器主窗口：展示当前目录内容，支持进入子目录与返回上级。
/// </summary>
public partial class MainWindow : Window
{
    private readonly IDirectoryLister _lister = new FileSystemDirectoryLister();
    private readonly ObservableCollection<EntryRow> _rows = [];
    private string _currentPath = string.Empty;

    public MainWindow()
    {
        InitializeComponent();
        EntriesList.ItemsSource = _rows;
        Navigate(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
    }

    private void OnGoUpClick(object sender, RoutedEventArgs e)
    {
        var parent = Directory.GetParent(_currentPath);
        if (parent is not null)
        {
            Navigate(parent.FullName);
        }
    }

    private void OnRefreshClick(object sender, RoutedEventArgs e) => Navigate(_currentPath);

    private void OnEntryDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (EntriesList.SelectedItem is not EntryRow { IsDirectory: true } row)
        {
            return;
        }

        Navigate(row.FullPath);
    }

    private void Navigate(string path)
    {
        try
        {
            IReadOnlyList<FileSystemEntry> entries = _lister.List(path);

            _currentPath = path;
            _rows.Clear();
            foreach (var entry in entries)
            {
                _rows.Add(EntryRow.From(entry));
            }

            CurrentPathText.Text = path;
            StatusText.Text = $"{entries.Count} 个项目";
        }
        catch (Exception ex) when (ex is DirectoryNotFoundException or UnauthorizedAccessException or IOException)
        {
            StatusText.Text = $"无法打开：{ex.Message}";
        }
    }

    /// <summary>供列表绑定使用的视图行。</summary>
    private sealed record EntryRow(
        string Name,
        string FullPath,
        bool IsDirectory,
        string Kind,
        string SizeText,
        string LastModifiedText)
    {
        public static EntryRow From(FileSystemEntry entry) => new(
            entry.Name,
            entry.FullPath,
            entry.IsDirectory,
            entry.IsDirectory ? "文件夹" : (string.IsNullOrEmpty(entry.Extension) ? "文件" : entry.Extension),
            entry.IsDirectory ? string.Empty : FormatSize(entry.SizeBytes),
            entry.LastModified.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture));

        private static string FormatSize(long bytes) => bytes switch
        {
            < 1024 => $"{bytes} B",
            < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
            < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
            _ => $"{bytes / (1024.0 * 1024 * 1024):F2} GB",
        };
    }
}
