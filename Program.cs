using System.Runtime.InteropServices;
using System.Text;
using OsuAssetLinker;
using Realms;

// ============================================================
//  osu! Lazer → Stable 数据同步工具
//  用法: osu-AssetLinker [lazer目录] [Songs目录] [功能开关]
// ============================================================

Console.OutputEncoding = Encoding.UTF8;
Ui.Banner();

// --- --help ---
if (args.Contains("--help") || args.Contains("-h"))
{
    PrintHelp();
    return 0;
}

// --- 1. 解析参数 ---
bool relink = args.Contains("--relink");
string[] pathArgs = args.Where(a => !a.StartsWith('-')).ToArray();

var features = new List<Feature>();
if (args.Contains("--all")) features.AddRange(AllFeatures());
if (args.Contains("--beats")) features.Add(Feature.Beatmaps);
if (args.Contains("--skins")) features.Add(Feature.Skins);
// 没有任何功能开关时保持旧行为：只同步铺面
if (features.Count == 0) features.Add(Feature.Beatmaps);
features = features.Distinct().ToList();

bool interactiveMode = IsInteractiveMode() && args.Length == 0;

string? lazerDataPath;
string? stableSongsPath;

if (interactiveMode)
{
    string? detectedLazer = DetectLazerDataPath();
    string? detectedStable = DetectStableSongsPath();

    if (!InteractivePrompt(detectedLazer, detectedStable, out lazerDataPath, out stableSongsPath, out relink, features))
    {
        Console.WriteLine();
        Ui.Warn("已取消。");
        PauseBeforeExit();
        return 0;
    }
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
        Ui.Error("无法自动检测路径，请手动指定：");
        Console.WriteLine("  用法: osu-AssetLinker <lazer目录> <stable Songs目录> [功能开关]");
        return 1;
    }

    Ui.Info($"[自动检测] Lazer 数据目录: {lazerDataPath}");
    Ui.Info($"[自动检测] Stable Songs 目录: {stableSongsPath}");
}

stableSongsPath = ResolveSongsPath(stableSongsPath!);
string stableRoot = Path.GetDirectoryName(stableSongsPath) ?? stableSongsPath;

if (relink)
    Ui.Accent("[模式] --relink：将对已有文件也替换为硬链接以释放磁盘空间");

Console.WriteLine();

// --- 2. 验证路径 ---
string? realmFile = FindRealmFile(lazerDataPath!);
if (realmFile == null)
{
    Ui.Error($"错误: 在 '{lazerDataPath}' 中找不到 client.realm 文件");
    return 1;
}

string lazerFilesPath = Path.Combine(lazerDataPath!, "files");
if (!Directory.Exists(lazerFilesPath))
{
    Ui.Error($"错误: Lazer files 目录不存在: {lazerFilesPath}");
    return 1;
}

if (features.Contains(Feature.Beatmaps) && !Directory.Exists(stableSongsPath))
{
    Console.WriteLine($"创建 Songs 目录: {stableSongsPath}");
    Directory.CreateDirectory(stableSongsPath);
}

// --- 3. 检测硬链接可用性 ---
if (features.Contains(Feature.Beatmaps) || features.Contains(Feature.Skins))
{
    bool? sameVolume = HardLink.SameVolume(
        HardLink.NearestExistingDirectory(lazerFilesPath),
        HardLink.NearestExistingDirectory(stableRoot));

    if (sameVolume == false)
    {
        Ui.Warn("⚠ 硬链接不可用（lazer 与 stable 在不同分区），文件将被复制");
        if (relink)
        {
            Ui.Error("错误: --relink 需要硬链接支持（两个目录必须在同一磁盘分区）");
            return 1;
        }
    }
    else
    {
        Ui.Ok("✓ 硬链接可用（同一分区），文件将零额外磁盘占用");
    }
}

Console.WriteLine();

// --- 4. 打开 Realm 数据库（只读）---
Console.WriteLine($"正在读取 Lazer 数据库: {realmFile}");

Realm realm;
try
{
    realm = LazerRealm.Open(realmFile);
}
catch (Exception ex)
{
    Ui.Error($"\n{ex.Message}");
    if (ex is not InvalidOperationException)
        Ui.Error("\n提示: 请先关闭 osu! lazer 再运行本工具");
    return 1;
}

// --- 5. 执行同步 ---
var results = new List<SyncStats>();

try
{
    using (realm)
    {
        var ctx = new SyncContext
        {
            Realm = realm,
            LazerFilesPath = lazerFilesPath,
            SongsPath = stableSongsPath,
            StableRoot = stableRoot,
            Relink = relink,
        };

        foreach (var feature in features)
        {
            var stats = RunFeature(feature, ctx);
            results.Add(stats);
            Console.WriteLine();
        }
    }
}
catch (Exception ex)
{
    Ui.Error($"\n读取数据库失败: {ex.Message}");
    Ui.Error("\n提示: 请先关闭 osu! lazer 再运行本工具");
    return 1;
}

// --- 6. 输出汇总 ---
PrintSummary(results);

if (interactiveMode)
    PauseBeforeExit();

return results.Any(r => r.HasFailures) ? 2 : 0;

// ================================================================
//  调度
// ================================================================

SyncStats RunFeature(Feature feature, SyncContext ctx)
{
    Ui.Accent($"【{FeatureName(feature)}】");
    return feature switch
    {
        Feature.Skins => SkinSyncer.Run(ctx),
        _ => BeatmapSyncer.Run(ctx),
    };
}

static List<Feature> AllFeatures() => [Feature.Beatmaps, Feature.Skins];

static string FeatureName(Feature feature) => feature switch
{
    Feature.Beatmaps => "铺面",
    Feature.Skins => "皮肤",
    _ => feature.ToString(),
};

void PrintSummary(List<SyncStats> stats)
{
    Ui.Separator();
    Ui.Info("  同步完成！");

    foreach (var s in stats)
        Console.WriteLine($"  {s.Label}    {s.Summary()}");

    long linked = stats.Sum(f => f.FilesLinked);
    long copied = stats.Sum(f => f.FilesCopied);
    long relinked = stats.Sum(f => f.FilesRelinked);
    long filesFailed = stats.Sum(f => f.FilesFailed);
    long contentDiffer = stats.Sum(f => f.FilesContentDiffer);

    if (linked + copied + relinked + contentDiffer + filesFailed > 0)
    {
        Console.WriteLine();
        if (linked > 0) Console.WriteLine($"  新建硬链接: {linked} 个文件（零额外磁盘占用）");
        if (relinked > 0) Console.WriteLine($"  替换为硬链接: {relinked} 个文件");
        if (copied > 0) Ui.Warn($"  复制文件: {copied} 个（硬链接不可用，占用额外磁盘）");
        if (contentDiffer > 0) Ui.Warn($"  内容不同未覆盖: {contentDiffer} 个文件（stable 里的版本可能被改过，保持原样）");
        if (filesFailed > 0) Ui.Error($"  失败文件: {filesFailed} 个");
    }

    Ui.Separator();
}

static void PrintHelp()
{
    Console.WriteLine("用法:");
    Console.WriteLine("  osu-AssetLinker [lazer目录] [Songs目录] [功能开关]");
    Console.WriteLine();
    Console.WriteLine("参数:");
    Console.WriteLine("  lazer目录    osu! lazer 数据目录（含 client.realm）");
    Console.WriteLine("               留空则自动检测 %APPDATA%\\osu");
    Console.WriteLine("  Songs目录    osu!stable 的 Songs 文件夹路径（也可以直接给 stable 根目录）");
    Console.WriteLine("               留空则自动检测（读取 stable 配置文件）");
    Console.WriteLine();
    Console.WriteLine("功能开关（可组合，都不写时等同 --beats）:");
    Console.WriteLine("  --beats        同步铺面到 Songs/");
    Console.WriteLine("  --skins        同步皮肤到 Skins/");
    Console.WriteLine("  --all          同步以上全部");
    Console.WriteLine();
    Console.WriteLine("其他选项:");
    Console.WriteLine("  --relink       将 stable 中已有的普通文件替换为硬链接，释放磁盘空间");
    Console.WriteLine("                 （要求两个目录在同一磁盘分区，硬链接才能生效）");
    Console.WriteLine("  --help, -h     显示此帮助信息");
    Console.WriteLine();
    Console.WriteLine("示例:");
    Console.WriteLine("  osu-AssetLinker");
    Console.WriteLine("      自动检测路径，只同步铺面");
    Console.WriteLine();
    Console.WriteLine("  osu-AssetLinker --all --relink");
    Console.WriteLine("      同步全部内容，并把已有的普通文件替换为硬链接");
    Console.WriteLine();
    Console.WriteLine("  osu-AssetLinker \"C:\\Users\\你\\AppData\\Roaming\\osu\" \"C:\\osu!\\Songs\" --skins");
    Console.WriteLine("      手动指定路径，只同步皮肤");
    Console.WriteLine();
    Console.WriteLine("说明:");
    Console.WriteLine("  · 同一分区时使用硬链接，文件不会被复制，磁盘占用为零");
    Console.WriteLine("  · 不同分区时回退为复制，占用额外磁盘空间");
    Console.WriteLine("  · 运行前请关闭 osu! lazer（Realm 数据库需要独占访问）");
    Console.WriteLine("  · 同步完成后在 stable 中按 F5 刷新铺面列表");
}

// ================================================================
//  路径检测
// ================================================================

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
    [
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "osu!", "Songs"),
        @"C:\osu!\Songs",
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "osu!", "Songs"),
    ];
    foreach (var c in candidates)
        if (Directory.Exists(c)) return c;

    string stableDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "osu!");
    if (!Directory.Exists(stableDir))
        return null;

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
    return Directory.Exists(defaultSongs) ? defaultSongs : null;
}

/// <summary>允许直接传 stable 根目录（会自动补上 Songs）。</summary>
static string ResolveSongsPath(string input)
{
    string path = input.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    if (path.Length == 0) path = input;

    if (Path.GetFileName(path).Equals("Songs", StringComparison.OrdinalIgnoreCase))
        return path;

    string sub = Path.Combine(path, "Songs");
    return Directory.Exists(sub) ? sub : path;
}

static string? FindRealmFile(string lazerPath)
{
    string direct = Path.Combine(lazerPath, "client.realm");
    if (File.Exists(direct)) return direct;
    return Directory.GetFiles(lazerPath, "client*.realm").OrderByDescending(f => f).FirstOrDefault();
}

// ================================================================
//  交互模式
// ================================================================

static bool IsInteractiveMode()
{
    if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        return false;
    uint[] list = new uint[1];
    return GetConsoleProcessList(list, 1) <= 1;
}

/// <summary>交互式提问，返回用户是否确认执行。</summary>
static bool InteractivePrompt(string? detectedLazer, string? detectedStable,
    out string? lazerPath, out string? stablePath, out bool relinkMode, List<Feature> features)
{
    Console.WriteLine("交互模式（双击运行）\n");

    // --- Lazer 数据目录 ---
    if (detectedLazer != null)
        Console.WriteLine($"[自动检测] Lazer 数据目录: {detectedLazer}");

    lazerPath = null;
    while (lazerPath == null)
    {
        Console.Write("请输入 Lazer 数据目录路径（留空使用检测到的）: ");
        string? input = Console.ReadLine()?.Trim();
        lazerPath = string.IsNullOrEmpty(input) ? detectedLazer : input;

        if (lazerPath == null)
        {
            Console.WriteLine("自动检测失败，请手动输入路径");
            continue;
        }

        if (FindRealmFile(lazerPath) == null)
        {
            Ui.Error($"错误: 在 '{lazerPath}' 中找不到 client.realm 文件");
            lazerPath = null;
        }
    }

    // --- Stable Songs 目录 ---
    if (detectedStable != null)
        Console.WriteLine($"[自动检测] Stable Songs 目录: {detectedStable}");

    stablePath = null;
    while (stablePath == null)
    {
        Console.Write("请输入 Stable Songs 目录路径（留空使用检测到的）: ");
        string? input = Console.ReadLine()?.Trim();
        stablePath = string.IsNullOrEmpty(input) ? detectedStable : input;

        if (stablePath == null)
            Console.WriteLine("自动检测失败，请手动输入路径");
    }

    // --- 要同步的内容（多选）---
    Console.WriteLine("\n要同步哪些内容？（可多选）");
    features.Clear();
    if (Ask("  同步铺面 → Songs/", true)) features.Add(Feature.Beatmaps);
    if (Ask("  同步皮肤 → Skins/", false)) features.Add(Feature.Skins);

    if (features.Count == 0)
    {
        Ui.Warn("  没有选择任何内容，默认只同步铺面。");
        features.Add(Feature.Beatmaps);
    }

    // --- relink ---
    Console.WriteLine();
    Ui.Dim("  --relink 会把 stable 里已有的【普通文件】替换为硬链接，释放重复占用的磁盘空间。");
    Ui.Dim("  替换前会逐字节比对（文件长度 + SHA-256），内容不同的文件不会被覆盖。");
    relinkMode = Ask("是否启用 --relink？", false);

    // --- 执行前确认 ---
    string songsPath = ResolveSongsPath(stablePath);
    string stableRoot = Path.GetDirectoryName(songsPath) ?? songsPath;

    Console.WriteLine();
    Ui.Separator();
    Ui.Info("  请确认以下操作：");
    Console.WriteLine($"    Lazer 数据目录 : {lazerPath}   （只读）");
    Console.WriteLine($"    Stable 数据目录: {stableRoot}");
    if (features.Contains(Feature.Beatmaps))
        Console.WriteLine($"    铺面           : {songsPath}");
    if (features.Contains(Feature.Skins))
        Console.WriteLine($"    皮肤           : {Path.Combine(stableRoot, "Skins")}");
    Console.WriteLine($"    relink 模式    : {(relinkMode ? "开（普通文件会替换为硬链接）" : "关")}");
    Ui.Separator();

    if (!Ask("\n确认开始？", true))
        return false;

    Console.WriteLine();
    return true;
}

/// <summary>双击运行时暂停，让用户能看到结果。输入被重定向时 ReadKey 不可用，直接跳过。</summary>
static void PauseBeforeExit()
{
    if (Console.IsInputRedirected)
        return;

    Console.Write("\n按任意键退出...");

    try
    {
        Console.ReadKey(true);
    }
    catch (InvalidOperationException)
    {
        // 没有控制台（例如被计划任务拉起），忽略即可
    }
}

/// <summary>y/N 提问，输入为空时返回 <paramref name="defaultValue"/>。</summary>
static bool Ask(string prompt, bool defaultValue)
{
    Console.Write($"{prompt} [{(defaultValue ? "Y/n" : "y/N")}]: ");
    string input = Console.ReadLine()?.Trim().ToLowerInvariant() ?? string.Empty;

    if (string.IsNullOrEmpty(input))
        return defaultValue;

    return input is "y" or "yes";
}

// ── Windows 原生 API ─────────────────────────────────────────
[DllImport("kernel32.dll")]
static extern uint GetConsoleProcessList(uint[] processList, uint processCount);

// ================================================================
//  功能开关
// ================================================================

enum Feature
{
    Beatmaps,
    Skins,
}
