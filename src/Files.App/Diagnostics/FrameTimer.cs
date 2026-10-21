using System.Windows.Media;

namespace Files.App.Diagnostics;

/// <summary>
/// 基于 <see cref="CompositionTarget.Rendering"/> 的帧计时器，用于验证 PRD §5.1 的滚动帧率红线。
/// </summary>
/// <remarks>
/// <see cref="CompositionTarget.Rendering"/> 每个合成帧触发一次，因此相邻两次回调的间隔
/// 就是实际呈现间隔。它反映的是"WPF 真的画出了一帧"，比 UI 线程的忙闲更接近用户感知。
/// </remarks>
public sealed class FrameTimer
{
    private static readonly TimeSpan Window = TimeSpan.FromMilliseconds(500);

    private readonly object _gate = new();
    private long _lastTimestamp;
    private long _windowStart;
    private int _framesInWindow;
    private double _currentFps;
    private double _worstFrameMs;
    private bool _running;

    /// <summary>当前帧率（每 500ms 更新一次）。</summary>
    public double CurrentFps
    {
        get
        {
            lock (_gate)
            {
                return _currentFps;
            }
        }
    }

    /// <summary>自启动以来最差单帧耗时（毫秒）。</summary>
    public double WorstFrameMs
    {
        get
        {
            lock (_gate)
            {
                return _worstFrameMs;
            }
        }
    }

    /// <summary>开始计时。</summary>
    public void Start()
    {
        if (_running)
        {
            return;
        }

        _running = true;
        _lastTimestamp = 0;
        _windowStart = 0;
        _framesInWindow = 0;
        CompositionTarget.Rendering += OnRendering;
    }

    /// <summary>停止计时并解除事件订阅（必须调用，否则窗口关闭后仍会被合成器回调）。</summary>
    public void Stop()
    {
        if (!_running)
        {
            return;
        }

        _running = false;
        CompositionTarget.Rendering -= OnRendering;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        long now = Environment.TickCount64;

        lock (_gate)
        {
            if (_lastTimestamp != 0)
            {
                long delta = now - _lastTimestamp;
                if (delta > _worstFrameMs)
                {
                    _worstFrameMs = delta;
                }
            }

            _lastTimestamp = now;

            if (_windowStart == 0)
            {
                _windowStart = now;
                return;
            }

            _framesInWindow++;

            long elapsed = now - _windowStart;
            if (elapsed >= (long)Window.TotalMilliseconds)
            {
                _currentFps = _framesInWindow * 1000.0 / elapsed;
                _framesInWindow = 0;
                _windowStart = now;
            }
        }
    }
}
