using System.Diagnostics;

namespace Files.Core.Diagnostics;

/// <summary>
/// 工作集采样。用于验证 PRD §5.1 的内存预算（单栏 ≤90MB、四栏 ≤150MB）。
/// </summary>
/// <remarks>
/// 采样值包含运行时自身的基线开销，因此比较时看的是**增量**而不是绝对值：
/// 进入大目录前后的差值才是这一栏的真实成本。
/// </remarks>
public sealed class WorkingSetSampler
{
    private long _peakBytes;

    /// <summary>当前工作集（字节）。</summary>
    public long CurrentBytes
    {
        get
        {
            using var process = Process.GetCurrentProcess();
            process.Refresh();
            return process.WorkingSet64;
        }
    }

    /// <summary>当前工作集（MB，保留 1 位小数）。</summary>
    public double CurrentMb => CurrentBytes / (1024.0 * 1024.0);

    /// <summary>自上次重置以来的峰值工作集（MB）。</summary>
    public double PeakMb => _peakBytes / (1024.0 * 1024.0);

    /// <summary>采样一次并更新峰值。</summary>
    public double Sample()
    {
        long current = CurrentBytes;
        if (current > _peakBytes)
        {
            _peakBytes = current;
        }

        return current / (1024.0 * 1024.0);
    }

    /// <summary>重置峰值。</summary>
    public void ResetPeak() => _peakBytes = 0;

    /// <summary>对一段操作前后采样，返回增量（MB）。</summary>
    /// <param name="action">被测操作。</param>
    public double MeasureDeltaMb(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        // 先触发一次 GC，让"操作前"的基线稳定，避免把上一次的垃圾算进本次增量
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        double before = CurrentMb;
        action();
        double after = CurrentMb;

        return after - before;
    }
}
