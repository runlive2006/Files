using System.Runtime.InteropServices;
using Files.Core.Services;

namespace Files.Interop;

/// <summary>
/// 用 Shell 命名空间（CLSID）探测云盘的真实本地路径。
/// </summary>
/// <remarks>
/// <b>为什么用 Shell 探测、却用普通文件 API 访问</b>：
/// WPS 云盘等产品注册为 Shell 命名空间扩展（CLSID），但其内容同时存在于一个
/// 真实的本地目录中。通过 <c>Shell.Application</c> 拿到该路径后，枚举、文件操作、
/// 搜索全部可以复用现有实现，<b>不需要 IShellFolder/PIDL interop</b>。
/// <para>
/// 只有纯虚拟文件夹（网络位置、控制面板等）才必须走完整 Shell 命名空间；
/// 它们没有可用的本地路径，本实现会主动跳过。
/// </para>
/// <para>
/// 使用 <c>dynamic</c> 调用 COM：Shell.Application 是 late-bound 的 IDispatch 对象，
/// 手写接口声明收益不大且易错。所有调用都在 try/catch 内，失败即返回空。
/// </para>
/// </remarks>
public sealed class ShellCloudLocationProvider : ICloudLocationProvider
{
    /// <summary>已知云盘的 Shell 命名空间标识。</summary>
    private static readonly (string Provider, string Clsid, string FallbackName)[] KnownClouds =
    [
        // WPS 云盘（金山办公）
        ("wps", "{EEEEFCF7-867B-4FA2-9ABD-884CF531B612}", "WPS云盘"),
    ];

    /// <inheritdoc />
    public IReadOnlyList<CloudLocation> Detect()
    {
        var found = new List<CloudLocation>();

        foreach ((string provider, string clsid, string fallbackName) in KnownClouds)
        {
            CloudLocation? location = TryResolve(provider, clsid, fallbackName);

            if (location is not null && !found.Any(f => string.Equals(f.Path, location.Value.Path, StringComparison.OrdinalIgnoreCase)))
            {
                found.Add(location.Value);
            }
        }

        return found;
    }

    private static CloudLocation? TryResolve(string provider, string clsid, string fallbackName)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        object? shellObject = null;

        try
        {
            Type? shellType = Type.GetTypeFromProgID("Shell.Application");

            if (shellType is null)
            {
                return null;
            }

            shellObject = Activator.CreateInstance(shellType);

            if (shellObject is null)
            {
                return null;
            }

            dynamic shell = shellObject;
            dynamic? folder = shell.Namespace($"::{clsid}");

            if (folder is null)
            {
                return null;
            }

            string title = (folder.Title as string) ?? fallbackName;
            string path = (folder.Self.Path as string) ?? string.Empty;

            // 关键校验：必须背后有真实可用的文件系统目录。
            // 纯虚拟文件夹（网络位置、控制面板）Path 为空或形如 ::{CLSID}，此处会被拒绝。
            if (string.IsNullOrWhiteSpace(path)
                || path.StartsWith("::", StringComparison.Ordinal)
                || !Directory.Exists(path))
            {
                return null;
            }

            return new CloudLocation(title, path, provider);
        }
        catch (Exception ex) when (ex is COMException
                                       or InvalidOperationException
                                       or IOException
                                       or UnauthorizedAccessException
                                       or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
        {
            // 云盘未安装/未登录/组件异常：视为不可用
            return null;
        }
        finally
        {
            if (shellObject is not null && Marshal.IsComObject(shellObject))
            {
                try
                {
                    Marshal.ReleaseComObject(shellObject);
                }
                catch (ArgumentException)
                {
                    // 已释放，忽略
                }
            }
        }
    }
}
