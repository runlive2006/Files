using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Files.App.Views;

/// <summary>
/// 可虚拟化的网格面板：按固定单元尺寸把条目排成行，只实例化视口内的容器。
/// </summary>
/// <remarks>
/// WPF 内置的 <see cref="WrapPanel"/> 不支持虚拟化 —— 10 万项目录会一次性创建
/// 十万个容器，直接卡死。缩略图视图必须用可虚拟化的网格，因此这里自己实现。
/// <para>
/// 采用"按行布局 + 项容器回收"的策略：所有单元等宽等高，行列位置可以直接算出来，
/// 不需要测量全部子项。
/// </para>
/// </remarks>
public sealed class VirtualizingWrapPanel : VirtualizingPanel, IScrollInfo
{
    /// <summary>单元宽度。</summary>
    public static readonly DependencyProperty ItemWidthProperty = DependencyProperty.Register(
        nameof(ItemWidth),
        typeof(double),
        typeof(VirtualizingWrapPanel),
        new FrameworkPropertyMetadata(120.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>单元高度。</summary>
    public static readonly DependencyProperty ItemHeightProperty = DependencyProperty.Register(
        nameof(ItemHeight),
        typeof(double),
        typeof(VirtualizingWrapPanel),
        new FrameworkPropertyMetadata(120.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

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

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var itemsOwner = ItemsControl.GetItemsOwner(this);
        int itemCount = itemsOwner?.Items.Count ?? 0;

        double availableWidth = double.IsInfinity(availableSize.Width)
            ? Math.Max(ItemWidth, ActualWidth)
            : availableSize.Width;

        double availableHeight = double.IsInfinity(availableSize.Height)
            ? Math.Max(ItemHeight, ActualHeight)
            : availableSize.Height;

        _itemsPerRow = Math.Max(1, (int)(availableWidth / ItemWidth));
        int rowCount = (int)Math.Ceiling((double)itemCount / _itemsPerRow);

        UpdateScrollInfo(new Size(availableWidth, rowCount * ItemHeight), new Size(availableWidth, availableHeight));

        if (itemCount == 0)
        {
            return availableSize;
        }

        int firstRow = Math.Max(0, (int)(_offset.Y / ItemHeight));
        int visibleRows = (int)Math.Ceiling(availableHeight / ItemHeight) + 1;

        int firstIndex = Math.Min(itemCount - 1, firstRow * _itemsPerRow);
        int lastIndex = Math.Min(itemCount - 1, (firstRow + visibleRows) * _itemsPerRow - 1);

        RealizeRange(firstIndex, lastIndex);
        VirtualizeOutsideRange(firstIndex, lastIndex);

        return availableSize;
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        var itemsOwner = ItemsControl.GetItemsOwner(this);
        int itemCount = itemsOwner?.Items.Count ?? 0;

        if (itemCount == 0)
        {
            return finalSize;
        }

        double availableWidth = double.IsInfinity(finalSize.Width) ? ActualWidth : finalSize.Width;
        int itemsPerRow = Math.Max(1, (int)(availableWidth / ItemWidth));

        IItemContainerGenerator generator = ItemContainerGenerator;

        for (int i = 0; i < InternalChildren.Count; i++)
        {
            var child = InternalChildren[i];
            int itemIndex = generator.IndexFromGeneratorPosition(new GeneratorPosition(i, 0));
            if (itemIndex < 0)
            {
                continue;
            }

            int row = itemIndex / itemsPerRow;
            int column = itemIndex % itemsPerRow;

            child.Arrange(new Rect(
                column * ItemWidth,
                row * ItemHeight,
                ItemWidth,
                ItemHeight));
        }

        return finalSize;
    }

    private void RealizeRange(int firstIndex, int lastIndex)
    {
        IItemContainerGenerator generator = ItemContainerGenerator;

        GeneratorPosition startPosition = generator.GeneratorPositionFromIndex(firstIndex);
        int childIndex = startPosition.Offset == 0 ? startPosition.Index : startPosition.Index + 1;

        using (generator.StartAt(startPosition, GeneratorDirection.Forward, true))
        {
            for (int itemIndex = firstIndex; itemIndex <= lastIndex; itemIndex++, childIndex++)
            {
                var child = (UIElement)generator.GenerateNext(out bool newlyRealized);

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

    private void VirtualizeOutsideRange(int firstIndex, int lastIndex)
    {
        for (int i = InternalChildren.Count - 1; i >= 0; i--)
        {
            var position = new GeneratorPosition(i, 0);
            int itemIndex = ItemContainerGenerator.IndexFromGeneratorPosition(position);

            if (itemIndex < firstIndex || itemIndex > lastIndex)
            {
                ItemContainerGenerator.Remove(position, 1);
                RemoveInternalChildRange(i, 1);
            }
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

        if (changed)
        {
            ScrollOwner?.InvalidateScrollInfo();
        }
    }

    // ---------- IScrollInfo ----------

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
        var child = visual as UIElement;
        if (child is null)
        {
            return rectangle;
        }

        int childIndex = InternalChildren.IndexOf(child);
        if (childIndex < 0)
        {
            return rectangle;
        }

        int itemIndex = ItemContainerGenerator.IndexFromGeneratorPosition(new GeneratorPosition(childIndex, 0));
        if (itemIndex < 0)
        {
            return rectangle;
        }

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
