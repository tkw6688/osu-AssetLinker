using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Realms;

// ============================================================
//  osu! Lazer → Stable 铺面同步工具
//  用法: osu-AssetLinker [lazer目录] [Songs目录] [--relink]
//  --relink  对 stable 中已有的普通文件也替换为硬链接（释放磁盘）
// ============================================================

Console.OutputEncoding = Encoding.UTF8;
PrintBanner();

// --- --help ---
if (args.Contains("--help") || args.Contains("-h"))
{
    Console.WriteLine("用法:");
    Console.WriteLine("  osu-AssetLinker [lazer目录] [Songs目录] [选项]");
    Console.WriteLine();
    Console.WriteLine("参数:");
    Console.WriteLine("  lazer目录    osu! lazer 数据目录（含 client.realm）");
    Console.WriteLine("               留空则自动检测 %APPDATA%\\osu");
    Console.WriteLine("  Songs目录    osu!stable 的 Songs 文件夹路径");
    Console.WriteLine("               留空则自动检测（读取 stable 配置文件）");
    Console.WriteLine();
    Console.WriteLine("选项:");
    Console.WriteLine("  --relink     将 stable 中已有的普通文件替换为硬链接，释放磁盘空间");
    Console.WriteLine("               （要求两个目录在同一磁盘分区，硬链接才能生效）");
    Console.WriteLine("  --help, -h   显示此帮助信息");
    Console.WriteLine();
    Console.WriteLine("示例:");
    Console.WriteLine("  osu-AssetLinker");
    Console.WriteLine("      自动检测路径，同步 lazer 铺面到 stable（跳过已有铺面）");
    Console.WriteLine();
    Console.WriteLine("  osu-AssetLinker --relink");
    Console.WriteLine("      自动检测路径，并将 stable 中已有的普通文件替换为硬链接");
    Console.WriteLine();
    Console.WriteLine("  osu-AssetLinker \"C:\\Users\\你\\AppData\\Roaming\\osu\" \"C:\\osu!\\Songs\"");
    Console.WriteLine("      手动指定路径同步");
    Console.WriteLine();
    Console.WriteLine("  osu-AssetLinker \"D:\\osu-lazer\" \"D:\\osu-stable\\Songs\" --relink");
    Console.WriteLine("      手动指定路径并启用重链接");
    Console.WriteLine();
    Console.WriteLine("说明:");
    Console.WriteLine("  · 同一分区时使用硬链接，文件不会被复制，磁盘占用为零");
    Console.WriteLine("  · 不同分区时回退为复制，占用额外磁盘空间");
    Console.WriteLine("  · 运行前请关闭 osu! lazer（Realm 数据库需要独占访问）");
    Console.WriteLine("  · 同步完成后在 stable 中按 F5 刷新铺面列表");
    return 0;
}

// --- 0. 检测运行模式 ---
bool interactiveMode = IsInteractiveMode() && args.Length == 0;

// --- 1. 解析参数 ---
bool relink = args.Contains("--relink");
string[] pathArgs = args.Where(a => !a.StartsWith("--")).ToArray();

string? lazerDataPath;
string? stableSongsPath;

if (interactiveMode)
{
    string? detectedLazer = DetectLazerDataPath();
    string? detectedStable = DetectStableSongsPath();
    InteractivePrompt(detectedLazer, detectedStable, out lazerDataPath, out stableSongsPath, out relink);
}
else if (pathArgs.Length >= 2)
{
    lazerDataPath = pathArgs[0];
    stableSongsPath = pathArgs[1];
}
else
{
    lazerDataPath = DetectLazerDataPath();
    stableSongsPath = DetectStableSongsPath();

    if (lazerDataPath is null || stableSongsPath is null)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("无法自动检测路径，请手动指定：");
        Console.WriteLine("  用法: osu-AssetLinker <lazer目录> <stable Songs目录> [--relink]");
        Console.ResetColor();
        return 1;
    }

    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine($"[自动检测] Lazer 数据目录: {lazerDataPath}");
    Console.WriteLine($"[自动检测] Stable Songs 目录: {stableSongsPath}");
    Console.ResetColor();
}

if (relink)
{
    Console.ForegroundColor = ConsoleColor.Magenta;
    Console.WriteLine("[模式] --relink：将对已有文件也替换为硬链接以释放磁盘空间");
    Console.ResetColor();
}

Console.WriteLine();

// --- 2. 验证路径 ---
string? realmFile = FindRealmFile(lazerDataPath!);
if (realmFile == null)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"错误: 在 '{lazerDataPath}' 中找不到 client.realm 文件");
    Console.ResetColor();
    return 1;
}

string lazerFilesPath = Path.Combine(lazerDataPath!, "files");
if (!Directory.Exists(lazerFilesPath))
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"错误: Lazer files 目录不存在: {lazerFilesPath}");
    Console.ResetColor();
    return 1;
}

if (!Directory.Exists(stableSongsPath))
{
    Console.WriteLine($"创建 Songs 目录: {stableSongsPath}");
    Directory.CreateDirectory(stableSongsPath!);
}

// --- 3. 检测硬链接可用性 ---
bool hardLinksAvailable = CheckHardLinkAvailability(lazerFilesPath, stableSongsPath!);
Console.ForegroundColor = hardLinksAvailable ? ConsoleColor.Green : ConsoleColor.Yellow;
Console.WriteLine(hardLinksAvailable
    ? "✓ 硬链接可用（同一分区），文件将零额外磁盘占用"
    : "⚠ 硬链接不可用（不同分区），文件将被复制");
Console.ResetColor();

if (relink && !hardLinksAvailable)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine("错误: --relink 需要硬链接支持（两个目录必须在同一磁盘分区）");
    Console.ResetColor();
    return 1;
}

Console.WriteLine();

// --- 4. 打开 Realm 数据库（只读） ---
Console.WriteLine($"正在读取 Lazer 数据库: {realmFile}");

RealmConfiguration config;
try
{
    string tempDir = Path.Combine(Path.GetTempPath(), "lazer-sync");
    Directory.CreateDirectory(tempDir);

    config = new RealmConfiguration(realmFile)
    {
        SchemaVersion = 51,
        IsReadOnly = true,
        FallbackPipePath = tempDir,
        Schema = new Type[]
        {
            typeof(BeatmapSetInfo),
            typeof(BeatmapInfo),
            typeof(BeatmapMetadata),
            typeof(RealmNamedFileUsage),
            typeof(RealmFile),
        }
    };
}
catch (Exception ex)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"Realm 配置失败: {ex.Message}");
    Console.ResetColor();
    return 1;
}

int total = 0, synced = 0, skipped = 0, relinked = 0, failed = 0;

try
{
    using var realm = Realm.GetInstance(config);
    var beatmapSets = realm.All<BeatmapSetInfo>().Where(s => !s.DeletePending).ToList();
    total = beatmapSets.Count;
    Console.WriteLine($"共找到 {total} 个铺面集\n");

    foreach (var set in beatmapSets)
    {
        // 动态计算文件夹名最大长度，确保总路径不超 Windows MAX_PATH (260)
        int longestFileLen = set.Files
            .Where(f => f.File != null)
            .Select(f => SanitizeFilePath(f.Filename).Length)
            .DefaultIfEmpty(0)
            .Max();
        // 总路径 = stableSongsPath + '\' + folderName + '\' + fileName <= 259
        int maxFolderLen = Math.Clamp(259 - stableSongsPath!.Length - 1 - longestFileLen - 1, 20, 200);
        string folderName = BuildStableFolderName(set, maxFolderLen);
        string destDir = Path.Combine(stableSongsPath, folderName);
        int idx = synced + skipped + relinked + failed + 1;

        bool alreadyExists = CheckAlreadySynced(destDir, set);

        if (alreadyExists && !relink)
        {
            skipped++;
            continue;
        }

        if (alreadyExists && relink)
        {
            // --relink 模式：对已有目录检查哪些文件是普通文件，替换为硬链接
            Console.Write($"  [{idx}/{total}] [relink] {TruncateString(folderName, 55)}... ");
            try
            {
                int replaced = RelinkBeatmapSet(set, lazerFilesPath, destDir);
                if (replaced > 0)
                {
                    Console.ForegroundColor = ConsoleColor.Magenta;
                    Console.WriteLine($"✓ 释放 {replaced} 个文件");
                    Console.ResetColor();
                    relinked++;
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine("已是硬链接，跳过");
                    Console.ResetColor();
                    skipped++;
                }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"✗ {ex.Message}");
                Console.ResetColor();
                failed++;
            }
            continue;
        }

        // 正常同步新铺面
        Console.Write($"  [{idx}/{total}] {TruncateString(folderName, 60)}... ");
        try
        {
            Directory.CreateDirectory(destDir);
            SyncBeatmapSet(set, lazerFilesPath, destDir, hardLinksAvailable);

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("✓");
            Console.ResetColor();
            synced++;
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"✗ {ex.Message}");
            Console.ResetColor();
            try { if (Directory.Exists(destDir)) Directory.Delete(destDir, true); } catch { }
            failed++;
        }
    }
}
catch (Exception ex)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"\n读取数据库失败: {ex.Message}");
    Console.WriteLine("\n提示: 请先关闭 osu! lazer 再运行本工具");
    Console.ResetColor();
    return 1;
}

// --- 5. 输出汇总 ---
Console.WriteLine();
Console.ForegroundColor = ConsoleColor.Cyan;
Console.WriteLine("═══════════════════════════════════════");
Console.WriteLine("  同步完成！");
if (relink)
    Console.WriteLine($"  新增: {synced}  重链接: {relinked}  跳过: {skipped}  失败: {failed}");
else
    Console.WriteLine($"  新增: {synced}  跳过: {skipped}  失败: {failed}");
Console.WriteLine("═══════════════════════════════════════");
Console.ResetColor();

if (synced > 0 || relinked > 0)
{
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine("\n提示: 启动 osu!stable 后，按 F5 刷新铺面列表");
    Console.ResetColor();
}

if (interactiveMode)
{
    Console.Write("\n按任意键退出...");
    Console.ReadKey(true);
}

return failed > 0 ? 2 : 0;

// ================================================================
// 辅助方法
// ================================================================

static void PrintBanner()
{
    Console.ForegroundColor = ConsoleColor.Magenta;
    Console.WriteLine("╔══════════════════════════════════════════╗");
    Console.WriteLine("║   osu! Lazer → Stable 铺面同步工具       ║");
    Console.WriteLine("║   利用硬链接实现零磁盘占用同步           ║");
    Console.WriteLine("╚══════════════════════════════════════════╝");
    Console.ResetColor();
    Console.WriteLine();
}

static string? DetectLazerDataPath()
{
    string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    string candidate = Path.Combine(appData, "osu");
    if (File.Exists(Path.Combine(candidate, "client.realm")))
        return candidate;
    return null;
}

static string? DetectStableSongsPath()
{
    string[] candidates =
    {
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "osu!", "Songs"),
        @"C:\osu!\Songs",
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "osu!", "Songs"),
    };
    foreach (var c in candidates)
        if (Directory.Exists(c)) return c;

    string stableDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "osu!");
    if (Directory.Exists(stableDir))
    {
        string? cfg = Directory.GetFiles(stableDir, $"osu!.{Environment.UserName}.cfg").FirstOrDefault()
                   ?? Directory.GetFiles(stableDir, "osu!.*.cfg").FirstOrDefault();
        if (cfg != null)
        {
            foreach (var line in File.ReadAllLines(cfg))
            {
                if (!line.StartsWith("BeatmapDirectory", StringComparison.OrdinalIgnoreCase)) continue;
                string customPath = line.Split('=').Last().Trim();
                if (Path.IsPathRooted(customPath) && Directory.Exists(customPath)) return customPath;
                string abs = Path.Combine(stableDir, customPath);
                if (Directory.Exists(abs)) return abs;
                break;
            }
        }
        string defaultSongs = Path.Combine(stableDir, "Songs");
        if (Directory.Exists(defaultSongs)) return defaultSongs;
    }
    return null;
}

static string? FindRealmFile(string lazerPath)
{
    string direct = Path.Combine(lazerPath, "client.realm");
    if (File.Exists(direct)) return direct;
    return Directory.GetFiles(lazerPath, "client*.realm").OrderByDescending(f => f).FirstOrDefault();
}

static string BuildStableFolderName(BeatmapSetInfo set, int maxLength = 200)
{
    var meta = set.Beatmaps.FirstOrDefault()?.Metadata;
    string artist = SanitizeName(meta?.Artist ?? "Unknown Artist");
    string title = SanitizeName(meta?.Title ?? "Unknown Title");
    string onlineId = set.OnlineID > 0 ? set.OnlineID.ToString() : set.ID.ToString()[..8];
    string name = $"{onlineId} {artist} - {title}";
    if (name.Length > maxLength)
        name = name[..maxLength].TrimEnd().TrimEnd('.');
    return name;
}

static string SanitizeName(string name)
{
    char[] invalid = Path.GetInvalidFileNameChars();
    var sb = new StringBuilder();
    foreach (char c in name)
        sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
    // Windows 不允许文件名/目录名以 '.' 或 ' ' 结尾
    return sb.ToString().Trim().TrimEnd('.');
}

/// <summary>
/// 对铺面内部文件路径的每个分量分别做非法字符替换 + 结尾点号清理。
/// </summary>
static string SanitizeFilePath(string relativePath)
{
    var parts = relativePath.Split('/');
    char[] invalid = Path.GetInvalidFileNameChars();
    for (int i = 0; i < parts.Length; i++)
    {
        var sb = new StringBuilder();
        foreach (char c in parts[i])
            sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
        parts[i] = sb.ToString().Trim().TrimEnd('.');
        if (string.IsNullOrEmpty(parts[i])) parts[i] = "_";
    }
    return string.Join(Path.DirectorySeparatorChar, parts);
}

/// <summary>
/// 若完整路径超过 Windows MAX_PATH (260) 限制，截断文件名部分（保留扩展名）。
/// </summary>
static string TruncatePathForStable(string destPath, int maxTotal = 259)
{
    if (destPath.Length <= maxTotal)
        return destPath;

    string dir = Path.GetDirectoryName(destPath)!;
    string nameNoExt = Path.GetFileNameWithoutExtension(destPath);
    string ext = Path.GetExtension(destPath);
    int maxNameLen = maxTotal - dir.Length - 1 - ext.Length;

    if (maxNameLen < 5)
        throw new PathTooLongException($"路径过长，无法安全截断: {destPath}");

    string truncated = Path.Combine(dir, nameNoExt[..maxNameLen].TrimEnd().TrimEnd('.') + ext);

    // 避免截断后与已有文件冲突
    int counter = 2;
    while (File.Exists(truncated))
    {
        string suffix = $" ({counter})";
        int adjLen = maxNameLen - suffix.Length;
        if (adjLen < 1) adjLen = 1;
        truncated = Path.Combine(dir, nameNoExt[..adjLen].TrimEnd().TrimEnd('.') + suffix + ext);
        counter++;
    }

    return truncated;
}

static bool CheckAlreadySynced(string destDir, BeatmapSetInfo set)
{
    if (!Directory.Exists(destDir)) return false;
    return Directory.GetFiles(destDir, "*.osu").Length > 0;
}

/// <summary>
/// 正常同步一个铺面集（新增，使用硬链接或复制）。
/// </summary>
static void SyncBeatmapSet(BeatmapSetInfo set, string lazerFilesPath, string destDir, bool useHardLinks)
{
    foreach (var namedFile in set.Files)
    {
        if (namedFile.File == null) continue;

        string hash = namedFile.File!.Hash;
        string sourcePath = Path.Combine(lazerFilesPath, hash[..1], hash[..2], hash);

        if (!File.Exists(sourcePath))
            throw new FileNotFoundException($"Lazer 文件缺失: {sourcePath}");

        string sanitizedRelPath = SanitizeFilePath(namedFile.Filename);
        string destPath = TruncatePathForStable(Path.Combine(destDir, sanitizedRelPath));

        string? subDir = Path.GetDirectoryName(destPath);
        if (subDir != null && !Directory.Exists(subDir))
            Directory.CreateDirectory(subDir);

        if (File.Exists(destPath)) continue;

        if (useHardLinks)
        {
            if (!TryCreateHardLink(destPath, sourcePath))
                File.Copy(sourcePath, destPath);
        }
        else
        {
            File.Copy(sourcePath, destPath);
        }
    }
}

/// <summary>
/// --relink 模式：遍历铺面集中每个文件，若 stable 里是普通文件（非硬链接）
/// 则删除后用硬链接替换，返回实际替换的文件数。
/// </summary>
static int RelinkBeatmapSet(BeatmapSetInfo set, string lazerFilesPath, string destDir)
{
    int replaced = 0;

    foreach (var namedFile in set.Files)
    {
        if (namedFile.File == null) continue;

        string hash = namedFile.File!.Hash;
        string sourcePath = Path.Combine(lazerFilesPath, hash[..1], hash[..2], hash);

        if (!File.Exists(sourcePath)) continue; // lazer 文件不存在则跳过

        string sanitizedRelPath = SanitizeFilePath(namedFile.Filename);
        string destPath = TruncatePathForStable(Path.Combine(destDir, sanitizedRelPath));

        if (!File.Exists(destPath)) continue; // stable 文件不存在则跳过（不补充）

        // 检查是否已是硬链接（链接数 > 1 说明已链接，跳过）
        if (GetHardLinkCount(destPath) > 1) continue;

        // 是普通文件：删除后重建为硬链接
        File.Delete(destPath);
        if (!TryCreateHardLink(destPath, sourcePath))
        {
            // 硬链接失败（理论上不应发生，因为检测已通过），回退复制
            File.Copy(sourcePath, destPath);
        }
        else
        {
            replaced++;
        }
    }

    return replaced;
}

/// <summary>
/// 获取文件的硬链接数（Windows 专用）。
/// </summary>
static int GetHardLinkCount(string filePath)
{
    if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        return 1;

    var handle = CreateFile(filePath, 0x80000000, 3, IntPtr.Zero, 3, 0x80, IntPtr.Zero);
    if (handle == new IntPtr(-1)) return 1;

    try
    {
        if (GetFileInformationByHandle(handle, out var info))
            return (int)info.NumberOfLinks;
        return 1;
    }
    finally
    {
        CloseHandle(handle);
    }
}

static bool CheckHardLinkAvailability(string sourcePath, string destPath)
{
    if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return false;
    string testSrc = Path.Combine(sourcePath, "_hl_test_src");
    string testDst = Path.Combine(destPath, "_hl_test_dst");
    try
    {
        File.WriteAllText(testSrc, "");
        return TryCreateHardLink(testDst, testSrc);
    }
    catch { return false; }
    finally
    {
        try { File.Delete(testSrc); } catch { }
        try { File.Delete(testDst); } catch { }
    }
}

static bool TryCreateHardLink(string dest, string src)
{
    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        return CreateHardLink(dest, src, IntPtr.Zero);
    if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        return link(src, dest) == 0;
    return false;
}

static string TruncateString(string s, int maxLen)
    => s.Length <= maxLen ? s : s[..maxLen] + "…";

static bool IsInteractiveMode()
{
    if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        return false;
    uint[] list = new uint[1];
    return GetConsoleProcessList(list, 1) <= 1;
}

static void InteractivePrompt(string? detectedLazer, string? detectedStable, out string? lazerPath, out string? stablePath, out bool relinkMode)
{
    Console.WriteLine("交互模式（双击运行）\n");

    lazerPath = null;
    stablePath = null;
    relinkMode = false;

    if (detectedLazer != null)
        Console.WriteLine($"[自动检测] Lazer 数据目录: {detectedLazer}");

    do
    {
        Console.Write("请输入 Lazer 数据目录路径（留空自动检测）: ");
        string? input = Console.ReadLine()?.Trim();
        lazerPath = string.IsNullOrEmpty(input) ? detectedLazer : input;

        if (lazerPath == null)
        {
            Console.WriteLine("自动检测失败，请手动输入路径");
            continue;
        }

        if (FindRealmFile(lazerPath) == null)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"错误: 在 '{lazerPath}' 中找不到 client.realm 文件");
            Console.ResetColor();
            lazerPath = null;
        }
    } while (lazerPath == null);

    if (detectedStable != null)
        Console.WriteLine($"[自动检测] Stable Songs 目录: {detectedStable}");

    do
    {
        Console.Write("请输入 Stable Songs 目录路径（留空自动检测）: ");
        string? input = Console.ReadLine()?.Trim();
        stablePath = string.IsNullOrEmpty(input) ? detectedStable : input;

        if (stablePath == null)
        {
            Console.WriteLine("自动检测失败，请手动输入路径");
        }
    } while (stablePath == null);

    Console.Write("\n是否启用 --relink 模式？（释放磁盘空间）[y/N]: ");
    string? relinkInput = Console.ReadLine()?.Trim().ToLower();
    relinkMode = relinkInput == "y" || relinkInput == "yes";

    Console.WriteLine();
}

// ── Windows 原生 API ─────────────────────────────────────────
[DllImport("Kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
static extern bool CreateHardLink(string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);

[DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
static extern IntPtr CreateFile(string lpFileName, uint dwAccess, uint dwShare,
    IntPtr lpSA, uint dwCreation, uint dwFlags, IntPtr hTemplate);

[DllImport("kernel32.dll", SetLastError = true)]
static extern bool GetFileInformationByHandle(IntPtr handle, out ByHandleFileInformation lpInfo);

[DllImport("kernel32.dll", SetLastError = true)]
static extern bool CloseHandle(IntPtr hObject);

[DllImport("kernel32.dll")]
static extern uint GetConsoleProcessList(uint[] processList, uint processCount);

[DllImport("libc", SetLastError = true)]
static extern int link(string oldpath, string newpath);

[StructLayout(LayoutKind.Sequential)]
struct ByHandleFileInformation
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

// ── Realm 模型定义 ───────────────────────────────────────────────

[MapTo("BeatmapSet")]
public partial class BeatmapSetInfo : RealmObject
{
    [PrimaryKey] public Guid ID { get; set; } = Guid.NewGuid();
    public int OnlineID { get; set; } = -1;
    public bool DeletePending { get; set; }
    public IList<BeatmapInfo> Beatmaps { get; } = null!;
    public IList<RealmNamedFileUsage> Files { get; } = null!;
}

[MapTo("Beatmap")]
public partial class BeatmapInfo : RealmObject
{
    [PrimaryKey] public Guid ID { get; set; } = Guid.NewGuid();
    public BeatmapMetadata? Metadata { get; set; }
    public BeatmapSetInfo? BeatmapSet { get; set; }
    public string DifficultyName { get; set; } = string.Empty;
}

[MapTo("BeatmapMetadata")]
public partial class BeatmapMetadata : RealmObject
{
    public string Title { get; set; } = string.Empty;
    public string TitleUnicode { get; set; } = string.Empty;
    public string Artist { get; set; } = string.Empty;
    public string ArtistUnicode { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Tags { get; set; } = string.Empty;
    public string AudioFile { get; set; } = string.Empty;
    public string BackgroundFile { get; set; } = string.Empty;
}

[MapTo("File")]
public partial class RealmFile : RealmObject
{
    [PrimaryKey] public string Hash { get; set; } = string.Empty;
}

// EmbeddedObject：无 MapTo，Realm 直接使用类名 "RealmNamedFileUsage"
public class RealmNamedFileUsage : EmbeddedObject
{
    public string Filename { get; set; } = string.Empty;
    public RealmFile? File { get; set; }
}
