using Files.Core.Models;

namespace Files.Core.Services;

/// <summary>
/// 根据布局模式构造布局树，并实现 PRD §2.1 的四栏生成规则。
/// </summary>
public static class LayoutFactory
{
    /// <summary>
    /// 按目标布局，从当前布局树构造新的布局树。
    /// 新栏继承来源栏活动标签的路径，但各自独立（新建 <see cref="TabState"/> 实例）。
    /// </summary>
    /// <param name="current">当前布局树；为 <c>null</c> 时按单栏起步。</param>
    /// <param name="target">目标布局。</param>
    public static LayoutNode Create(LayoutNode? current, PaneLayout target)
    {
        var panes = current is null ? [] : CollectPanes(current).ToList();

        // 保证至少有一个来源栏，用于继承路径
        if (panes.Count == 0)
        {
            panes.Add(CreateDefaultPane());
        }

        var source = panes[0].ActiveTab;

        return target switch
        {
            PaneLayout.Single => CreatePaneNode(source),
            PaneLayout.HorizontalSplit => Split(
                SplitDirection.Horizontal,
                CreatePaneNode(source),
                CreatePaneNode(source)),
            PaneLayout.VerticalSplit => Split(
                SplitDirection.Vertical,
                CreatePaneNode(source),
                CreatePaneNode(source)),
            // Quad：先左右分裂，再各自上下分裂，形成 2×2
            PaneLayout.Quad => Split(
                SplitDirection.Horizontal,
                Split(SplitDirection.Vertical, CreatePaneNode(source), CreatePaneNode(source)),
                Split(SplitDirection.Vertical, CreatePaneNode(source), CreatePaneNode(source))),
            _ => throw new ArgumentOutOfRangeException(nameof(target), target, "未知的布局模式。"),
        };
    }

    /// <summary>收集布局树中的全部栏，按从左到右、从上到下的顺序。</summary>
    public static IReadOnlyList<PaneState> CollectPanes(LayoutNode root)
    {
        var result = new List<PaneState>();
        Walk(root, result);
        return result;
    }

    /// <summary>统计布局树中的栏数量。</summary>
    public static int CountPanes(LayoutNode root) => CollectPanes(root).Count;

    private static void Walk(LayoutNode node, List<PaneState> sink)
    {
        switch (node)
        {
            case PaneNode pane:
                sink.Add(pane.Pane);
                break;
            case SplitNode split:
                Walk(split.First, sink);
                Walk(split.Second, sink);
                break;
            default:
                throw new NotSupportedException($"未知的布局节点类型：{node.GetType().Name}");
        }
    }

    private static PaneNode CreatePaneNode(TabState template) => new()
    {
        Pane = new PaneState
        {
            Tabs = [CloneTab(template)],
            ActiveTabIndex = 0,
        },
    };

    private static PaneState CreateDefaultPane()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return new PaneState
        {
            Tabs = [new TabState { Path = string.IsNullOrEmpty(home) ? "C:\\" : home }],
            ActiveTabIndex = 0,
        };
    }

    private static TabState CloneTab(TabState source) => new()
    {
        Path = source.Path,
        ViewMode = source.ViewMode,
        SortColumn = source.SortColumn,
        SortDescending = source.SortDescending,
        ScrollOffset = 0,
    };

    private static SplitNode Split(SplitDirection direction, LayoutNode first, LayoutNode second) => new()
    {
        Direction = direction,
        First = first,
        Second = second,
        FirstRatio = 0.5,
    };
}
