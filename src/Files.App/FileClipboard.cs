using System.Collections.Specialized;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;

namespace Files.App;

/// <summary>
/// 文件剪贴板封装：与系统剪贴板互通（可直接粘贴到资源管理器，反之亦然）。
/// </summary>
/// <remarks>
/// 用 <c>FileDrop</c> 格式传递路径，用 <c>Preferred DropEffect</c> 传递"复制/剪切"意图。
/// 后者是 Windows 的约定：资源管理器据此决定粘贴后是否删除源文件。
/// 只用一个内部字段记录剪切状态的话，跨进程粘贴就会退化成复制。
/// </remarks>
internal static class FileClipboard
{
    private const string DropEffectFormat = "Preferred DropEffect";
    private const int DropEffectCopy = 1;
    private const int DropEffectMove = 2;

    /// <summary>把路径写入剪贴板。</summary>
    /// <param name="paths">文件/目录完整路径。</param>
    /// <param name="isCut">是否为剪切。</param>
    /// <returns>是否成功（剪贴板可能被其他程序占用）。</returns>
    public static bool SetFiles(IReadOnlyList<string> paths, bool isCut)
    {
        if (paths is null || paths.Count == 0)
        {
            return false;
        }

        try
        {
            var collection = new StringCollection();
            foreach (string path in paths)
            {
                collection.Add(path);
            }

            var data = new DataObject();
            data.SetFileDropList(collection);

            using var effect = new MemoryStream(sizeof(int));
            using (var writer = new BinaryWriter(effect, System.Text.Encoding.UTF8, leaveOpen: true))
            {
                writer.Write(isCut ? DropEffectMove : DropEffectCopy);
            }

            effect.Position = 0;
            data.SetData(DropEffectFormat, effect);

            Clipboard.SetDataObject(data, copy: true);
            return true;
        }
        catch (Exception ex) when (ex is COMException or ExternalException or IOException)
        {
            return false;
        }
    }

    /// <summary>尝试从剪贴板读取文件列表。</summary>
    /// <param name="paths">读到的路径。</param>
    /// <param name="isCut">是否为剪切操作。</param>
    public static bool TryGetFiles(out IReadOnlyList<string> paths, out bool isCut)
    {
        paths = [];
        isCut = false;

        try
        {
            if (!Clipboard.ContainsFileDropList())
            {
                return false;
            }

            StringCollection? collection = Clipboard.GetFileDropList();
            if (collection is null || collection.Count == 0)
            {
                return false;
            }

            var list = new List<string>(collection.Count);
            foreach (string? item in collection)
            {
                if (!string.IsNullOrWhiteSpace(item))
                {
                    list.Add(item);
                }
            }

            if (list.Count == 0)
            {
                return false;
            }

            paths = list;

            if (Clipboard.GetData(DropEffectFormat) is MemoryStream stream)
            {
                using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
                if (stream.Length >= sizeof(int))
                {
                    isCut = reader.ReadInt32() == DropEffectMove;
                }
            }

            return true;
        }
        catch (Exception ex) when (ex is COMException or ExternalException or IOException)
        {
            return false;
        }
    }

    /// <summary>清空剪贴板（剪切粘贴成功后调用，避免重复移动）。</summary>
    public static void Clear()
    {
        try
        {
            Clipboard.Clear();
        }
        catch (Exception ex) when (ex is COMException or ExternalException)
        {
            // 剪贴板被占用时忽略
        }
    }
}
