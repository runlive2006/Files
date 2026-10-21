namespace Files.Core.Models;

/// <summary>
/// 标签标题的生成规则。
/// </summary>
/// <remarks>
/// 直接显示目录名会在多个同名目录时产生歧义（<c>项目\src</c> 与 <c>备份\src</c>
/// 都显示 <c>src</c>，用户无法区分该点哪个）。因此仅当存在同名标签时，
/// 才补上父目录名 —— 常规情况下保持标题简短。
/// </remarks>
public static class TabTitle
{
    /// <summary>计算某个标签的显示标题。</summary>
    /// <param name="tab">目标标签。</param>
    /// <param name="siblings">同一栏内的全部标签（用于判断是否需要消歧）。</param>
    public static string Resolve(TabState tab, IEnumerable<TabState> siblings)
    {
        ArgumentNullException.ThrowIfNull(tab);
        ArgumentNullException.ThrowIfNull(siblings);

        string name = GetShortName(tab.Path);

        bool duplicated = siblings
            .Where(other => !ReferenceEquals(other, tab))
            .Any(other => string.Equals(GetShortName(other.Path), name, StringComparison.OrdinalIgnoreCase));

        if (!duplicated)
        {
            return name;
        }

        string parent = GetParentName(tab.Path);
        return string.IsNullOrEmpty(parent) ? name : $"{parent}\\{name}";
    }

    /// <summary>
    /// 取路径的末段作为短名；盘符根（如 <c>C:\</c>）返回完整路径。
    /// </summary>
    public static string GetShortName(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "此电脑";
        }

        string trimmed = path.TrimEnd('\\', '/');

        // "C:" 这种盘符根去掉分隔符后没有末段，直接用完整路径显示
        string name = Path.GetFileName(trimmed);
        if (!string.IsNullOrEmpty(name))
        {
            return name;
        }

        return string.IsNullOrEmpty(trimmed) ? path : trimmed;
    }

    /// <summary>取父目录名；无父目录时返回空字符串。</summary>
    public static string GetParentName(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        string trimmed = path.TrimEnd('\\', '/');
        string? parent = Path.GetDirectoryName(trimmed);

        return string.IsNullOrEmpty(parent) ? string.Empty : GetShortName(parent);
    }
}
