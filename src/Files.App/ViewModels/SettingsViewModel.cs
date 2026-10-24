using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Runtime.CompilerServices;
using Files.Core.Services;
using Files.Core.Services.Settings;

namespace Files.App.ViewModels;

/// <summary>
/// 单个设置项的视图模型。由 <see cref="SettingDescriptor"/> 驱动，界面自动生成。
/// </summary>
public sealed class SettingItemViewModel : INotifyPropertyChanged
{
    private readonly SettingsService _service;
    private string _value = string.Empty;

    /// <summary>创建设置项。</summary>
    public SettingItemViewModel(SettingDescriptor descriptor, SettingsService service)
    {
        Descriptor = descriptor;
        _service = service;
        _value = service.ReadValue(descriptor);
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>元数据。</summary>
    public SettingDescriptor Descriptor { get; }

    /// <summary>当前值（字符串形式，供控件双向绑定）。</summary>
    public string Value
    {
        get => _value;
        set
        {
            if (_value == value)
            {
                return;
            }

            _value = value;
            _service.WriteValue(Descriptor, value);
            OnPropertyChanged();

            // 派生属性同步刷新
            OnPropertyChanged(nameof(BoolValue));
            OnPropertyChanged(nameof(IntValue));
            OnPropertyChanged(nameof(SelectedOption));
        }
    }

    /// <summary>布尔值（供 CheckBox 绑定）。</summary>
    public bool BoolValue
    {
        get => bool.TryParse(_value, out bool result) && result;
        set => Value = value ? "true" : "false";
    }

    /// <summary>整数值（供 Slider / NumericUpDown 绑定）。</summary>
    public int IntValue
    {
        get => int.TryParse(_value, out int result) ? result : Descriptor.Min;
        set => Value = Math.Clamp(value, Descriptor.Min, Descriptor.Max).ToString();
    }

    /// <summary>枚举当前选中的选项；无匹配时返回 <see langword="null"/>。</summary>
    public KeyValuePair<string, string>? SelectedOption
    {
        get
        {
            foreach (var option in Descriptor.Options)
            {
                if (string.Equals(option.Key, _value, StringComparison.OrdinalIgnoreCase))
                {
                    return option;
                }
            }

            return null;
        }
        set
        {
            if (value is not null)
            {
                Value = value.Value.Key;
            }
        }
    }

    /// <summary>是否可编辑。</summary>
    public bool IsEditable => !Descriptor.IsReadOnly;

    /// <summary>只读说明。</summary>
    public string ReadOnlyReason => Descriptor.ReadOnlyReason ?? string.Empty;

    /// <summary>是否显示只读提示。</summary>
    public bool ShowReadOnlyHint => Descriptor.IsReadOnly && Descriptor.ReadOnlyReason is not null;

    /// <summary>是否为布尔项。</summary>
    public bool IsBoolean => Descriptor.Kind == SettingKind.Boolean;

    /// <summary>是否为整数项。</summary>
    public bool IsInteger => Descriptor.Kind == SettingKind.Integer;

    /// <summary>是否为枚举项。</summary>
    public bool IsEnum => Descriptor.Kind == SettingKind.Enum;

    /// <summary>是否为文本项。</summary>
    public bool IsText => Descriptor.Kind == SettingKind.Text;

    /// <summary>是否为目录项。</summary>
    public bool IsDirectory => Descriptor.Kind == SettingKind.DirectoryPath;

    // ---------- 供 XAML 直接绑定的可见性（避免在 XAML 里写转换器） ----------

    /// <summary>说明文字的可见性。</summary>
    public Visibility ShowDescriptionVisibility =>
        string.IsNullOrEmpty(Descriptor.Description) ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>只读提示的可见性。</summary>
    public Visibility ShowReadOnlyHintVisibility =>
        ShowReadOnlyHint ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>布尔控件的可见性。</summary>
    public Visibility BooleanVisibility =>
        IsBoolean ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>整数控件的可见性。</summary>
    public Visibility IntegerVisibility =>
        IsInteger ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>枚举控件的可见性。</summary>
    public Visibility EnumVisibility =>
        IsEnum ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>文本控件的可见性。</summary>
    public Visibility TextVisibility =>
        IsText || IsDirectory ? Visibility.Visible : Visibility.Collapsed;
    /// <summary>从服务重新读取值（导入或恢复默认后调用）。</summary>
    public void Refresh() => Value = _service.ReadValue(Descriptor);

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>设置窗口的视图模型。</summary>
public sealed class SettingsViewModel : INotifyPropertyChanged
{
    private readonly SettingsService _service;
    private string _searchKeyword = string.Empty;
    private string _selectedGroup = "常规";

    /// <summary>创建视图模型。</summary>
    public SettingsViewModel(SettingsService service)
    {
        ArgumentNullException.ThrowIfNull(service);
        _service = service;

        Groups = [.. SettingCatalog.GroupOrder];
        Items = [];
        ReloadItems();
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>设置变更时触发（供主窗口即时应用）。</summary>
    public event EventHandler<string>? SettingChanged;

    /// <summary>分组列表。</summary>
    public ObservableCollection<string> Groups { get; }

    /// <summary>当前分组下的设置项。</summary>
    public ObservableCollection<SettingItemViewModel> Items { get; }

    /// <summary>当前选中的分组。</summary>
    public string SelectedGroup
    {
        get => _selectedGroup;
        set
        {
            if (_selectedGroup == value)
            {
                return;
            }

            _selectedGroup = value;
            OnPropertyChanged();
            ReloadItems();
        }
    }

    /// <summary>搜索关键字。</summary>
    public string SearchKeyword
    {
        get => _searchKeyword;
        set
        {
            if (_searchKeyword == value)
            {
                return;
            }

            _searchKeyword = value;
            OnPropertyChanged();
            ReloadItems();
        }
    }

    /// <summary>当前是否处于搜索状态。</summary>
    public bool IsSearching => !string.IsNullOrWhiteSpace(_searchKeyword);

    /// <summary>没有匹配项时显示提示。</summary>
    public bool IsEmpty => Items.Count == 0;

    /// <summary>空状态提示的可见性。</summary>
    public Visibility EmptyVisibility => IsEmpty ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>保存设置。</summary>
    public void Save() => _service.Save();

    /// <summary>恢复当前分组的默认值。</summary>
    public void ResetCurrentGroup()
    {
        var defaults = new AppSettings();

        foreach (SettingItemViewModel item in Items.Where(i => i.IsEditable))
        {
            string defaultValue = ReadDefault(defaults, item.Descriptor);
            _service.WriteValue(item.Descriptor, defaultValue);
            item.Refresh();
        }

        OnPropertyChanged(nameof(Items));
    }

    private static string ReadDefault(AppSettings defaults, SettingDescriptor descriptor)
    {
        return descriptor.Key switch
        {
            "General.StartupMode" => defaults.General.StartupMode.ToString(),
            "General.FixedPath" => defaults.General.FixedPath,
            "General.RestoreTabs" => defaults.General.RestoreTabs ? "true" : "false",
            "General.SingleInstance" => defaults.General.SingleInstance ? "true" : "false",
            "View.DefaultMode" => defaults.View.DefaultMode.ToString(),
            "View.IconSize" => defaults.View.IconSize.ToString(),
            "View.ThumbnailSize" => defaults.View.ThumbnailSize.ToString(),
            "View.ShowHidden" => defaults.View.ShowHidden ? "true" : "false",
            "View.ShowExtensions" => defaults.View.ShowExtensions ? "true" : "false",
            "View.ShowStatusBar" => defaults.View.ShowStatusBar ? "true" : "false",
            "View.RowHeight" => defaults.View.RowHeight.ToString(),
            "View.AlternateRowColor" => defaults.View.AlternateRowColor ? "true" : "false",
            "View.FolderFirst" => defaults.View.FolderFirst ? "true" : "false",
            "View.ShowGridLines" => defaults.View.ShowGridLines ? "true" : "false",
            "View.InheritFolderView" => defaults.View.InheritFolderView ? "true" : "false",
            "FileOps.ConfirmDelete" => defaults.FileOperations.ConfirmDelete ? "true" : "false",
            "FileOps.DeleteToRecycleBin" => defaults.FileOperations.DeleteToRecycleBin ? "true" : "false",
            "FileOps.ConfirmDragMove" => defaults.FileOperations.ConfirmDragMove ? "true" : "false",
            "FileOps.OpenWithSingleClick" => defaults.FileOperations.OpenWithSingleClick ? "true" : "false",
            "FileOps.DoubleClickOnFolder" => defaults.FileOperations.DoubleClickOnFolder.ToString(),
            "Panes.ShowTabStripAlways" => defaults.PaneBehavior.ShowTabStripAlways ? "true" : "false",
            "Panes.NewTabInheritsPath" => defaults.PaneBehavior.NewTabInheritsPath ? "true" : "false",
            "Panes.CloseWindowWithLastTab" => defaults.PaneBehavior.CloseWindowWithLastTab ? "true" : "false",
            "Panes.MaxClosedTabHistory" => defaults.PaneBehavior.MaxClosedTabHistory.ToString(),
            "Perf.IconCacheSize" => defaults.Performance.IconCacheSize.ToString(),
            "Perf.ShowPerfHud" => defaults.Performance.ShowPerfHud ? "true" : "false",
            "Advanced.LogLevel" => defaults.Advanced.LogLevel.ToString(),
            "Advanced.KeepCrashLogs" => defaults.Advanced.KeepCrashLogs.ToString(),
            "Advanced.EnableShellExtensions" => defaults.Advanced.EnableShellExtensions ? "true" : "false",
            "Advanced.ConfigDirectory" => defaults.Advanced.ConfigDirectory,
            _ => string.Empty,
        };
    }

    private void ReloadItems()
    {
        Items.Clear();

        IReadOnlyList<SettingDescriptor> descriptors = IsSearching
            ? SettingCatalog.Search(_searchKeyword)
            : SettingCatalog.ForGroup(_selectedGroup);

        foreach (SettingDescriptor descriptor in descriptors)
        {
            var item = new SettingItemViewModel(descriptor, _service);
            item.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(SettingItemViewModel.Value))
                {
                    SettingChanged?.Invoke(this, descriptor.Key);
                }
            };

            Items.Add(item);
        }

        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(EmptyVisibility));
        OnPropertyChanged(nameof(IsSearching));
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
