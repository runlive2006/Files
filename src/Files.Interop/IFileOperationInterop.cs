using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace Files.Interop;

/// <summary>
/// <c>IFileOperation</c> 的 COM 接口声明。
/// </summary>
/// <remarks>
/// <b>为什么换掉 <c>SHFileOperation</c></b>（PRD §6.4）：
/// <list type="bullet">
/// <item><b>支持长路径</b>：<c>SHFileOperation</c> 明确不支持 <c>\\?\</c> 前缀，
/// 超过 MAX_PATH 的路径直接失败；<c>IFileOperation</c> 走 IShellItem，可以处理；</item>
/// <item><b>可自定义进度与冲突</b>：通过 <see cref="IFileOperationProgressSink"/> 拿到
/// 每个文件的事件，可以聚合成我们自己的对话框；</item>
/// <item><b>可取消</b>：<c>IFileOperation::Cancel</c> 能在长操作中及时中断；</item>
/// <item><b>异步</b>：<c>StartOperation</c> 立即返回，配合 <c>PerformOperations</c> 同步等待。</item>
/// </list>
/// <para>
/// 本文件只声明用到的成员；vtable 顺序必须与 <c>shobjidl.h</c> 完全一致，
/// 否则调用会跳到错误的函数 —— 这是手写 COM 最容易出错的地方。
/// </para>
/// </remarks>
[ComImport]
[Guid("947aab5f-0a5c-4c13-b4d6-4bf7836fc9f8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IFileOperation
{
    // 顺序必须严格按 shobjidl.h，不可调整

    [PreserveSig]
    int Advise(IFileOperationProgressSink pfops, out uint pdwCookie);

    [PreserveSig]
    int Unadvise(uint dwCookie);

    [PreserveSig]
    int SetOperationFlags(uint dwOperationFlags);

    [PreserveSig]
    int SetProgressMessage([MarshalAs(UnmanagedType.LPWStr)] string pszMessage);

    [PreserveSig]
    int SetProgressDialog(IntPtr popd);

    [PreserveSig]
    int SetProperties(IntPtr pproparray);

    [PreserveSig]
    int SetOwnerWindow(IntPtr hwndOwner);

    [PreserveSig]
    int ApplyPropertiesToItem(IShellItem psiItem);

    [PreserveSig]
    int ApplyPropertiesToItems(IntPtr punkItems);

    [PreserveSig]
    int RenameItem(
        IShellItem psiItem,
        [MarshalAs(UnmanagedType.LPWStr)] string pszNewName,
        IFileOperationProgressSink? pfopsItem);

    [PreserveSig]
    int RenameItems(IntPtr pUnkItems, [MarshalAs(UnmanagedType.LPWStr)] string pszNewName);

    [PreserveSig]
    int MoveItem(
        IShellItem psiItem,
        IShellItem psiDestinationFolder,
        [MarshalAs(UnmanagedType.LPWStr)] string? pszNewName,
        IFileOperationProgressSink? pfopsItem);

    [PreserveSig]
    int MoveItems(IntPtr punkItems, IShellItem psiDestinationFolder);

    [PreserveSig]
    int CopyItem(
        IShellItem psiItem,
        IShellItem psiDestinationFolder,
        [MarshalAs(UnmanagedType.LPWStr)] string? pszCopyName,
        IFileOperationProgressSink? pfopsItem);

    [PreserveSig]
    int CopyItems(IntPtr punkItems, IShellItem psiDestinationFolder);

    [PreserveSig]
    int DeleteItem(IShellItem psiItem, IFileOperationProgressSink? pfopsItem);

    [PreserveSig]
    int DeleteItems(IntPtr punkItems);

    [PreserveSig]
    int NewItem(
        IShellItem psiDestinationFolder,
        uint dwFileAttributes,
        [MarshalAs(UnmanagedType.LPWStr)] string pszName,
        [MarshalAs(UnmanagedType.LPWStr)] string? pszTemplateName,
        IFileOperationProgressSink? pfopsItem);

    [PreserveSig]
    int PerformOperations();

    [PreserveSig]
    int GetAnyOperationsAborted([MarshalAs(UnmanagedType.Bool)] out bool pfAnyOperationsAborted);
}

/// <summary>
/// <c>IShellItem</c> 的最小声明。
/// </summary>
[ComImport]
[Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellItem
{
    [PreserveSig]
    int BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);

    [PreserveSig]
    int GetParent(out IShellItem ppsi);

    [PreserveSig]
    int GetDisplayName(uint sigdnName, [MarshalAs(UnmanagedType.LPWStr)] out string ppszName);

    [PreserveSig]
    int GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);

    [PreserveSig]
    int Compare(IShellItem psi, uint hint, out int piOrder);
}

/// <summary>
/// <c>IFileOperationProgressSink</c> 的最小声明。
/// </summary>
/// <remarks>
/// 只用于接收"操作是否被用户取消"等少量事件；完整的自定义进度对话框见后续排期。
/// 继承 <c>IUnknown</c> 的所有方法由 CLR 自动处理，这里按 vtable 顺序声明。
/// </remarks>
[ComImport]
[Guid("04b0f1a7-9490-44bc-96e1-4296a31252e2")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IFileOperationProgressSink
{
    [PreserveSig]
    int StartOperations();

    [PreserveSig]
    int FinishOperations(int hrResult);

    [PreserveSig]
    int PreRenameItem(uint dwFlags, IShellItem psiItem, [MarshalAs(UnmanagedType.LPWStr)] string? pszNewName);

    [PreserveSig]
    int PostRenameItem(uint dwFlags, IShellItem psiItem, [MarshalAs(UnmanagedType.LPWStr)] string pszNewName, int hrRename, IShellItem? psiNewlyCreated);

    [PreserveSig]
    int PreMoveItem(uint dwFlags, IShellItem psiItem, IShellItem psiDestinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string? pszNewName);

    [PreserveSig]
    int PostMoveItem(uint dwFlags, IShellItem psiItem, IShellItem psiDestinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string? pszNewName, int hrMove, IShellItem? psiNewlyCreated);

    [PreserveSig]
    int PreCopyItem(uint dwFlags, IShellItem psiItem, IShellItem psiDestinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string? pszNewName);

    [PreserveSig]
    int PostCopyItem(uint dwFlags, IShellItem psiItem, IShellItem psiDestinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string? pszNewName, int hrCopy, IShellItem? psiNewlyCreated);

    [PreserveSig]
    int PreDeleteItem(uint dwFlags, IShellItem psiItem);

    [PreserveSig]
    int PostDeleteItem(uint dwFlags, IShellItem psiItem, int hrDelete, IShellItem? psiNewlyCreated);

    [PreserveSig]
    int PreNewItem(uint dwFlags, IShellItem psiDestinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string? pszNewName);

    [PreserveSig]
    int PostNewItem(uint dwFlags, IShellItem psiDestinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string? pszNewName, [MarshalAs(UnmanagedType.LPWStr)] string? pszTemplateName, uint dwFileAttributes, int hrNew, IShellItem? psiNewItem);

    [PreserveSig]
    int UpdateProgress(uint iWorkTotal, uint iWorkSoFar);

    [PreserveSig]
    int ResetTimer();

    [PreserveSig]
    int PauseTimer();

    [PreserveSig]
    int ResumeTimer();
}
