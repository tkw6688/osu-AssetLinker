using System.Runtime.InteropServices;

namespace OsuAssetLinker;

/// <summary>硬链接相关操作（Windows 原生 API，其他平台走 libc）。</summary>
public static class HardLink
{
    private static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    /// <summary>创建硬链接，失败返回 false（调用方应回退为复制）。</summary>
    public static bool TryCreate(string dest, string src)
    {
        try
        {
            if (IsWindows)
                return CreateHardLink(dest, src, IntPtr.Zero);

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                return link(src, dest) == 0;
        }
        catch
        {
            // 落到返回 false，由调用方回退为复制
        }

        return false;
    }

    /// <summary>把源文件链接到目标路径；硬链接不可用时回退为复制。</summary>
    /// <returns>true 表示建立了硬链接，false 表示执行了复制。</returns>
    public static bool LinkOrCopy(string dest, string src)
    {
        if (TryCreate(dest, src))
            return true;

        File.Copy(src, dest);
        return false;
    }

    /// <summary>获取文件的硬链接数（Windows 专用，其他平台返回 1）。</summary>
    public static int GetLinkCount(string filePath)
    {
        if (!IsWindows)
            return 1;

        var handle = CreateFile(filePath, 0x80000000, 3, IntPtr.Zero, 3, 0x80, IntPtr.Zero);
        if (handle == new IntPtr(-1))
            return 1;

        try
        {
            return GetFileInformationByHandle(handle, out var info) ? (int)info.NumberOfLinks : 1;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    /// <summary>
    /// 判断两个目录是否位于同一卷。用卷序列号比较，全程只读，不会往 lazer 目录里写测试文件。
    /// 返回 null 表示无法判断（调用方应乐观处理，逐个文件的回退逻辑会兜底）。
    /// </summary>
    public static bool? SameVolume(string pathA, string pathB)
    {
        if (!IsWindows)
            return null;

        uint? a = GetVolumeSerialNumber(pathA);
        uint? b = GetVolumeSerialNumber(pathB);

        if (a is null || b is null)
            return null;

        return a == b;
    }

    /// <summary>
    /// 乐观判断两个路径能否建立硬链接：只有明确不在同一卷时才返回 false，
    /// 无法判断时按可链接处理（实际执行时会按文件回退为复制）。
    /// </summary>
    public static bool LikelySameVolume(string pathA, string pathB)
        => SameVolume(NearestExistingDirectory(pathA), NearestExistingDirectory(pathB)) != false;

    /// <summary>沿路径向上找到第一个真实存在的目录（用来比较卷序列号）。</summary>
    public static string NearestExistingDirectory(string path)
    {
        var dir = new DirectoryInfo(path);
        while (dir is { Exists: false })
        {
            if (dir.Parent is null)
                return path;
            dir = dir.Parent;
        }
        return dir.FullName;
    }

    private static uint? GetVolumeSerialNumber(string path)
    {
        // 打开目录句柄需要 FILE_FLAG_BACKUP_SEMANTICS (0x02000000)
        var handle = CreateFile(path, 0, 7 /*share r/w/delete*/, IntPtr.Zero, 3 /*OPEN_EXISTING*/,
            0x02000000, IntPtr.Zero);

        if (handle == new IntPtr(-1))
            return null;

        try
        {
            return GetFileInformationByHandle(handle, out var info) ? info.VolumeSerialNumber : null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    // ── Windows 原生 API ─────────────────────────────────────────
    [DllImport("Kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateHardLink(string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateFile(string lpFileName, uint dwAccess, uint dwShare,
        IntPtr lpSA, uint dwCreation, uint dwFlags, IntPtr hTemplate);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetFileInformationByHandle(IntPtr handle, out ByHandleFileInformation lpInfo);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("libc", SetLastError = true)]
    private static extern int link(string oldpath, string newpath);

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }
}
