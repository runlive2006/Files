using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Files.Core.Models;

namespace Files.App.ViewModels;

/// <summary>
/// 一条搜索命中。
/// </summary>
/// <remarks>
/// 与 <see cref="FileSystemEntry"/> 的区别：搜索结果的条目分散在不同目录，
/// 必须携带<b>自己的完整路径</b>，无法像目录列举那样由"父目录 + 名称"推导。
/// </remarks>
/// <param name="Name">显示名称。</param>
/// <param name="FullPath">完整路径。</param>
/// <param name="IsDirectory">是否为文件夹。</param>
/// <param name="SizeBytes">大小（字节）。</param>
/// <param name="LastModifiedUtc">修改时间。</param>
public readonly record struct SearchHitRow(
    string Name,
    string FullPath,
    bool IsDirectory,
    long SizeBytes,
    DateTimeOffset LastModifiedUtc);
