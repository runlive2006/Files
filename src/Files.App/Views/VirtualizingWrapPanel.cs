using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Files.App.Views;

/// <summary>
/// 可虚拟化的网格面板：按固定单元尺寸排列条目，只实例化视口内的容器。
/// </summary>
/// <remarks>
/// WPF 内置 <see cref="WrapPanel"/> 不虚拟化，10 万项目录会一次性创建十万个容器。
/// <para>
/// 本实现遵循 <see cref="VirtualizingPanel"/> 的标准用法：
/// <list type="bullet">
/// <item><see cref="MeasureOverride"/> 只实现视口范围内的容器；</item>
/// <item>超出范围的容器回收，避免容器数随滚动增长；</item>
/// <item>通过 <see cref="IScrollInfo"/> 把滚动交给 <c>ScrollViewer</c> 统一处理。</item>
/// </list>
/// </para>
/// <para>
/// <b>重要</b>：使用本面板时，<c>ListBox/ListView</c> 的
/// <c>ScrollViewer.CanContentScroll</c> 必须为 <see langword="true"/>，
/// 这样 <c>ScrollViewer</c> 才会使用面板的 <see cref="IScrollInfo"/> 而不是按像素滚动。
/// </para>
/// </remarks>
public sealed class VirtualizingWrapPanel : VirtualizingPanel, IScrollInfo
{
    /// <summary>单元宽度。</summary>
    public static readonly DependencyProperty ItemWidthProperty = DependencyProperty.Register(
        nameof(ItemWidth),
        typeof(double),
        typeof(VirtualizingWrapPanel),
        new FrameworkPropertyMetadata(128.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>单元高度。</summary>
    public static readonly DependencyProperty ItemHeightProperty = DependencyProperty.Register(
        nameof(ItemHeight),
        typeof(double),
        typeof(VirtualizingWrapPanel),
        new FrameworkPropertyMetadata(104.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    private Size _extent = new(0, 0);
    private Size _viewport = new(0, 0);
    private Point _offset;
    private int _itemsPerRow = 1;

    /// <summary>单元宽度。</summary>
    public double ItemWidth
    {
        get => (double)GetValue(ItemWidthProperty);
        set => SetValue(ItemWidthProperty, value);
    }

    /// <summary>单元高度。</summary>
    public double ItemHeight
    {
        get => (double)GetValue(ItemHeightProperty);
        set => SetValue(ItemHeightProperty, value);
    }

    /// <summary>诊断：最近一次 Measure 的关键输入。</summary>
    public string LastMeasureDiagnostics { get; private set; } = "(未测量)";
    /// <summary>
    /// 当前实际生成的容器数量（诊断用，用于验证虚拟化是否生效）。
    /// </summary>
    public int InternalChildrenCount => InternalChildren.Count;
    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var owner = ItemsControl.GetItemsOwner(this);
        int itemCount = owner?.Items.Count ?? 0;
        IItemContainerGenerator? generatorProbe = ItemContainerGenerator;

        LastMeasureDiagnostics =
            $"available={availableSize.Width:F0}x{availableSize.Height:F0} " +
            $"owner={(owner is null ? "null" : itemCount.ToString())} " +
            $"generator={(generatorProbe is null ? "null" : "ok")} " +
            $"children={InternalChildren.Count}";

        // 视口尺寸：ScrollViewer 通常给有限值；退化情况下用单元尺寸兜底，
        // 保证 _extent/_viewport 永远是有限数（无限值会让滚动条失效）
        double viewportWidth = double.IsInfinity(availableSize.Width)
            ? Math.Max(ItemWidth, ActualWidth > 0 ? ActualWidth : ItemWidth * 4)
            : availableSize.Width;

        double viewportHeight = double.IsInfinity(availableSize.Height)
            ? Math.Max(ItemHeight, ActualHeight > 0 ? ActualHeight : ItemHeight * 4)
            : availableSize.Height;

        _itemsPerRow = Math.Max(1, (int)(viewportWidth / ItemWidth));
        int rowCount = (int)Math.Ceiling((double)itemCount / _itemsPerRow);

        UpdateScrollInfo(
            new Size(viewportWidth, rowCount * ItemHeight),
            new Size(viewportWidth, viewportHeight));

        if (itemCount == 0)
        {
            // 清空时回收所有已有容器，避免残留
            CleanupContainers(ItemContainerGenerator, firstIndex: 0, lastIndex: -1);
            return availableSize;
        }

        IItemContainerGenerator generator = ItemContainerGenerator;

        // 面板刚挂上时生成器可能尚未注入；返回有限尺寸等待下一轮布局，
        // 不可返回 Infinity —— 那会让父级永远不再重新测量
        if (generator is null)
        {
            return new Size(viewportWidth, viewportHeight);
        }

        int firstRow = Math.Max(0, (int)(_offset.Y / ItemHeight));
        int visibleRows = (int)Math.Ceiling(viewportHeight / ItemHeight) + 1;

        int firstIndex = Math.Min(itemCount - 1, firstRow * _itemsPerRow);
        int lastIndex = Math.Min(itemCount - 1, (firstRow + visibleRows) * _itemsPerRow - 1);

        RealizeContainers(generator, firstIndex, lastIndex);
        CleanupContainers(generator, firstIndex, lastIndex);

        return new Size(viewportWidth, viewportHeight);
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        double viewportWidth = double.IsInfinity(finalSize.Width) ? _viewport.Width : finalSize.Width;
        int itemsPerRow = Math.Max(1, (int)(viewportWidth / ItemWidth));
        double rowHeight = ItemHeight;
        double verticalOffset = _offset.Y;

        for (int i = 0; i < InternalChildren.Count; i++)
        {
            UIElement child = InternalChildren[i];

            // 用"当前视口第一个容器"做锚点推算：
            // 比反查 generator 位置更可靠，也避免了索引失配导致的错位
            int row = (i + _firstRealizedRowEnvelope) / itemsPerRow;
            int column = (i + _firstRealizedRowEnvelope) % itemsPerRow;

            child.Arrange(new Rect(
                column * ItemWidth,
                row * rowHeight - verticalOffset,
                ItemWidth,
                rowHeight));
        }

        return finalSize;
    }

    /// <summary>
    /// 当前视口第一个容器在整个列表中的起始序号（用于 Arrange 推算行/列）。
    /// </summary>
    /// <remarks>
    /// 只保存序号而不是每项坐标：容器按顺序连续存放，序号 + 每行数量即可算出位置，
    /// 避免为每个容器维护一份布局数据。
    /// </remarks>
    private int _firstRealizedRowEnvelope;

    private void RealizeContainers(IItemContainerGenerator generator, int firstIndex, int lastIndex)
    {
        _firstRealizedRowEnvelope = firstIndex;

        GeneratorPosition startPosition = generator.GeneratorPositionFromIndex(firstIndex);
        int childIndex = startPosition.Offset == 0 ? startPosition.Index : startPosition.Index + 1;

        using (generator.StartAt(startPosition, GeneratorDirection.Forward, true))
        {
            for (int itemIndex = firstIndex; itemIndex <= lastIndex; itemIndex++, childIndex++)
            {
                if (generator.GenerateNext(out bool newlyRealized) is not UIElement child)
                {
                    continue;
                }

                if (newlyRealized)
                {
                    if (childIndex >= InternalChildren.Count)
                    {
                        AddInternalChild(child);
                    }
                    else
                    {
                        InsertInternalChild(childIndex, child);
                    }

                    generator.PrepareItemContainer(child);
                }

                child.Measure(new Size(ItemWidth, ItemHeight));
            }
        }
    }

    private void CleanupContainers(IItemContainerGenerator? generator, int firstIndex, int lastIndex)
    {
        if (generator is null || InternalChildren.Count == 0)
        {
            return;
        }

        // 从后往前删，避免索引位移
        for (int i = InternalChildren.Count - 1; i >= 0; i--)
        {
            var position = new GeneratorPosition(i, 0);
            int itemIndex = generator.IndexFromGeneratorPosition(position);

            bool outside = itemIndex < firstIndex || itemIndex > lastIndex;

            if (!outside)
            {
                continue;
            }

            generator.Remove(position, 1);
            RemoveInternalChildRange(i, 1);
        }
    }

    private void UpdateScrollInfo(Size extent, Size viewport)
    {
        bool changed = false;

        if (extent != _extent)
        {
            _extent = extent;
            changed = true;
        }

        if (viewport != _viewport)
        {
            _viewport = viewport;
            changed = true;
        }

        // 内容缩短时把偏移夹回合法范围，否则会出现"滚到空白区"
        double maxOffset = Math.Max(0, _extent.Height - _viewport.Height);

        if (_offset.Y > maxOffset)
        {
            _offset.Y = maxOffset;
            changed = true;
        }

        if (changed)
        {
            ScrollOwner?.InvalidateScrollInfo();
        }
    }

    /// <inheritdoc />
    public bool CanVerticallyScroll { get; set; } = true;

    /// <inheritdoc />
    public bool CanHorizontallyScroll { get; set; }

    /// <inheritdoc />
    public double ExtentWidth => _extent.Width;

    /// <inheritdoc />
    public double ExtentHeight => _extent.Height;

    /// <inheritdoc />
    public double ViewportWidth => _viewport.Width;

    /// <inheritdoc />
    public double ViewportHeight => _viewport.Height;

    /// <inheritdoc />
    public double HorizontalOffset => _offset.X;

    /// <inheritdoc />
    public double VerticalOffset => _offset.Y;

    /// <inheritdoc />
    public ScrollViewer? ScrollOwner { get; set; }

    /// <inheritdoc />
    public void LineUp() => SetVerticalOffset(VerticalOffset - ItemHeight / 3);

    /// <inheritdoc />
    public void LineDown() => SetVerticalOffset(VerticalOffset + ItemHeight / 3);

    /// <inheritdoc />
    public void LineLeft() { }

    /// <inheritdoc />
    public void LineRight() { }

    /// <inheritdoc />
    public void PageUp() => SetVerticalOffset(VerticalOffset - ViewportHeight);

    /// <inheritdoc />
    public void PageDown() => SetVerticalOffset(VerticalOffset + ViewportHeight);

    /// <inheritdoc />
    public void PageLeft() { }

    /// <inheritdoc />
    public void PageRight() { }

    /// <inheritdoc />
    public void MouseWheelUp() => SetVerticalOffset(VerticalOffset - ItemHeight);

    /// <inheritdoc />
    public void MouseWheelDown() => SetVerticalOffset(VerticalOffset + ItemHeight);

    /// <inheritdoc />
    public void MouseWheelLeft() { }

    /// <inheritdoc />
    public void MouseWheelRight() { }

    /// <inheritdoc />
    public void SetHorizontalOffset(double offset) { }

    /// <inheritdoc />
    public void SetVerticalOffset(double offset)
    {
        double maxOffset = Math.Max(0, ExtentHeight - ViewportHeight);
        double clamped = Math.Clamp(offset, 0, maxOffset);

        if (Math.Abs(clamped - _offset.Y) < 0.5)
        {
            return;
        }

        _offset.Y = clamped;
        ScrollOwner?.InvalidateScrollInfo();
        InvalidateMeasure();
    }

    /// <inheritdoc />
    public Rect MakeVisible(Visual visual, Rect rectangle)
    {
        if (visual is not UIElement child)
        {
            return rectangle;
        }

        int childIndex = InternalChildren.IndexOf(child);

        if (childIndex < 0)
        {
            return rectangle;
        }

        int itemIndex = _firstRealizedRowEnvelope + childIndex;
        int row = itemIndex / _itemsPerRow;

        double top = row * ItemHeight;
        double bottom = top + ItemHeight;

        if (top < VerticalOffset)
        {
            SetVerticalOffset(top);
        }
        else if (bottom > VerticalOffset + ViewportHeight)
        {
            SetVerticalOffset(bottom - ViewportHeight);
        }

        return rectangle;
    }
}
