namespace Files.Core.Services;

/// <summary>
/// 一个云盘位置。
/// </summary>
/// <param name="DisplayName">显示名称，如「WPS云盘」。</param>
/// <param name="Path">可用的本地路径。</param>
/// <param name="Provider">提供商标识，如 <c>wps</c>。</param>
public readonly record struct CloudLocation(string DisplayName, string Path, string Provider);

/// <summary>
/// 探测系统中的云盘位置。
/// </summary>
/// <remarks>
/// 抽象放在 Core，具体实现放在 <c>Files.Interop</c>（需要 COM/Shell）。
/// 这样 Core 保持平台无关，也便于在测试中替换为假的探测结果。
/// </remarks>
public interface ICloudLocationProvider
{
    /// <summary>
    /// 探测所有可用的云盘位置。失败时返回空列表，不抛异常。
    /// </summary>
    IReadOnlyList<CloudLocation> Detect();
}
