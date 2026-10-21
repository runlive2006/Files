namespace Files.Core.Services;

/// <summary>
/// 单栏的后退/前进历史。
/// </summary>
/// <remarks>
/// 语义与资源管理器一致：导航到新位置时，把当前位置压入后退栈并清空前进栈；
/// 后退时把当前位置压入前进栈。深度上限由 <see cref="DefaultDepth"/> 控制，
/// 超出后丢弃最旧的一项（PRD §2.3 规定 50）。
/// </remarks>
public sealed class NavigationHistory
{
    /// <summary>默认历史深度。</summary>
    public const int DefaultDepth = 50;

    private readonly List<string> _back = [];
    private readonly List<string> _forward = [];
    private readonly int _depth;

    /// <summary>创建历史记录。</summary>
    /// <param name="depth">深度上限，必须为正。</param>
    public NavigationHistory(int depth = DefaultDepth)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(depth);
        _depth = depth;
    }

    /// <summary>当前位置；尚未导航时为 <see langword="null"/>。</summary>
    public string? Current { get; private set; }

    /// <summary>是否可以后退。</summary>
    public bool CanGoBack => _back.Count > 0;

    /// <summary>是否可以前进。</summary>
    public bool CanGoForward => _forward.Count > 0;

    /// <summary>后退栈（最旧在前）。</summary>
    public IReadOnlyList<string> BackStack => _back;

    /// <summary>前进栈（最近一次可前进的在前）。</summary>
    public IReadOnlyList<string> ForwardStack => _forward;

    /// <summary>
    /// 导航到新位置。若与当前位置相同则不产生历史记录。
    /// </summary>
    /// <param name="path">目标路径。</param>
    public void Navigate(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (Current is not null && string.Equals(Current, path, StringComparison.Ordinal))
        {
            return;
        }

        if (Current is not null)
        {
            _back.Add(Current);
            Trim(_back);
        }

        _forward.Clear();
        Current = path;
    }

    /// <summary>后退一步，返回新的当前位置；无法后退时返回 <see langword="null"/>。</summary>
    public string? Back()
    {
        if (_back.Count == 0)
        {
            return null;
        }

        string target = _back[^1];
        _back.RemoveAt(_back.Count - 1);

        if (Current is not null)
        {
            _forward.Add(Current);
            Trim(_forward);
        }

        Current = target;
        return target;
    }

    /// <summary>前进一步，返回新的当前位置；无法前进时返回 <see langword="null"/>。</summary>
    public string? Forward()
    {
        if (_forward.Count == 0)
        {
            return null;
        }

        string target = _forward[^1];
        _forward.RemoveAt(_forward.Count - 1);

        if (Current is not null)
        {
            _back.Add(Current);
            Trim(_back);
        }

        Current = target;
        return target;
    }

    /// <summary>清空历史（切换到新的根位置时使用）。</summary>
    public void Clear()
    {
        _back.Clear();
        _forward.Clear();
        Current = null;
    }

    private void Trim(List<string> stack)
    {
        if (stack.Count > _depth)
        {
            stack.RemoveRange(0, stack.Count - _depth);
        }
    }
}
