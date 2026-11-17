using System.Runtime.InteropServices;
using System.Text;
using OsuAssetLinker;
using Realms;

// ============================================================
//  osu! Lazer → Stable 数据同步工具
//  用法: osu-AssetLinker [--lazer-dir <目录>] [--stable-dir <目录>] [功能开关]
// ============================================================

Console.OutputEncoding = Encoding.UTF8;
Ui.Banner();

// --- --help ---
if (args.Contains("--help") || args.Contains("-h"))
{
    PrintHelp();
    return 0;
}

bool interactiveMode = IsInteractiveMode() && args.Length == 0;

try
{
    string lazerDataPath;
    StablePaths stablePaths;
    bool relink;
    List<Feature> features;

    if (interactiveMode)
    {
        var result = InteractivePrompt();
        if (result == null)
        {
            Console.WriteLine();
            Ui.Warn("已取消。");
            return 0;
        }

        lazerDataPath = result.LazerPath;
        stablePaths = result.Stable;
        relink = result.Relink;
        features = result.Features;
    }
    else
    {
        var parsed = ParseArgs(args);
        if (parsed.Error != null)
        {
            Ui.Error($"错误: {parsed.Error}");
            return 1;
        }

        var options = parsed.Options!;
        features = options.Features;
        relink = options.Relink;

        if (options.LazerDir != null)
        {
            lazerDataPath = ResolveLazerDataPath(options.LazerDir);
        }
        else
        {
            string? detected = DetectLazerDataPath();
            if (detected == null)
            {
                Ui.Error("错误: 无法自动检测 osu!lazer 数据目录。");
                Ui.Dim("  请用 --lazer-dir \"...\" 指定。");
                return 1;
            }

            lazerDataPath = detected;
            Ui.Info($"[自动检测] Lazer 数据目录: {lazerDataPath}");
        }

        if (!Directory.Exists(lazerDataPath))
        {
            Ui.Error($"错误: lazer 数据目录不存在: {lazerDataPath}");
            return 1;
        }

        string stableRoot;
        if (options.StableDir != null)
        {
            var info = StableData.NormalizeRoot(options.StableDir);
            if (info.Note != null)
                Ui.Warn($"⚠ {info.Note}");
            stableRoot = info.Root;
        }
        else
        {
            var roots = StableData.FindRoots();
            if (roots.Count == 0)
            {
                Ui.Error("错误: 无法自动检测 osu!stable 根目录。");
                Ui.Dim("  已尝试: " + string.Join("、", StableData.CandidateRoots()));
                Ui.Dim("  请用 --stable-dir \"C:\\osu!\" 指定。");
                return 1;
            }

            stableRoot = roots[0];
            Ui.Info($"[自动检测] Stable 根目录: {stableRoot}");
            if (roots.Count > 1)
                Ui.Dim("  检测到多个 stable 目录，当前使用第一个；可用 --stable-dir 指定其他位置。");
        }

        if (!Directory.Exists(stableRoot))
        {
            Ui.Error($"错误: stable 根目录不存在: {stableRoot}");
            return 1;
        }

        if (!StableData.LooksLikeStableRoot(stableRoot))
            Ui.Warn($"⚠ '{stableRoot}' 中没有发现 osu!stable 的典型文件/目录，仍将尝试继续。");

        var built = BuildStablePathsForCli(stableRoot, features);
        if (built == null)
            return 1;

        stablePaths = built;
    }

    if (relink && !interactiveMode)
        Ui.Accent("[模式] --relink：将对已有文件也替换为硬链接以释放磁盘空间");

    Console.WriteLine();

    // --- 2. 验证 lazer 路径 ---
    string? realmFile = LazerData.FindRealmFile(lazerDataPath);
    if (realmFile == null)
    {
        Ui.Error($"错误: 在 '{lazerDataPath}' 中找不到 client.realm 文件");
        if (LazerData.DescribeMissingRealm(lazerDataPath) is { } hint)
            Ui.Dim(hint);
        return 1;
    }

    string lazerFilesPath = Path.Combine(lazerDataPath, "files");
    if (!Directory.Exists(lazerFilesPath))
    {
        Ui.Error($"错误: Lazer files 目录不存在: {lazerFilesPath}");
        return 1;
    }

    // --- 3. 检测硬链接可用性（按实际目标分别判断）---
    var targets = new List<(string Label, string Path)>();
    if (features.Contains(Feature.Beatmaps) && stablePaths.SongsPath != null)
        targets.Add(("铺面", stablePaths.SongsPath));
    if (features.Contains(Feature.Skins))
        targets.Add(("皮肤", stablePaths.SkinsPath));

    var differentVolumes = new List<(string Label, string Path)>();
    bool allKnownSameVolume = targets.Count > 0;

    foreach (var target in targets)
    {
        bool? sameVolume = HardLink.SameVolume(
            HardLink.NearestExistingDirectory(lazerFilesPath),
            HardLink.NearestExistingDirectory(target.Path));

        if (sameVolume is false)
            differentVolumes.Add(target);
        else if (sameVolume is null)
            allKnownSameVolume = false;
    }

    foreach (var target in differentVolumes)
        Ui.Warn($"⚠ {target.Label}目标与 lazer 不在同一分区，将复制文件: {target.Path}");

    if (differentVolumes.Count > 0 && relink)
    {
        if (interactiveMode && Ask("是否关闭 --relink 并继续？", true))
        {
            relink = false;
            Ui.Warn("已关闭 --relink，将改用复制。");
        }
        else
        {
            Ui.Error("错误: --relink 需要硬链接支持，以下目标与 lazer 不在同一分区:");
            foreach (var target in differentVolumes)
                Ui.Error($"  · {target.Label}: {target.Path}");
            Ui.Dim("  请移除 --relink，或将 stable 目录移到与 lazer 相同的分区。");
            return 1;
        }
    }

    if (targets.Count > 0 && differentVolumes.Count == 0)
    {
        if (allKnownSameVolume)
            Ui.Ok("✓ 硬链接可用（同一分区），文件将零额外磁盘占用");
        else
            Ui.Dim("无法判断卷信息，将按文件逐个回退（硬链接不可用时复制）");
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
                Stable = stablePaths,
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

    return results.Any(r => r.HasFailures) ? 2 : 0;
}
finally
{
    // 双击运行时（包括拖拽参数导致解析失败的场景）暂停，让用户看到报错
    if (IsInteractiveMode())
        PauseBeforeExit();
}

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
    Console.WriteLine("  osu-AssetLinker [--lazer-dir <目录>] [--stable-dir <目录>] [功能开关] [其他选项]");
    Console.WriteLine();
    Console.WriteLine("路径选项（省略时自动检测）:");
    Console.WriteLine("  --lazer-dir <目录>   osu!lazer 数据目录（含 client.realm）");
    Console.WriteLine("                       会自动跟随 storage.ini 里的自定义数据目录");
    Console.WriteLine("  --stable-dir <目录>  osu!stable 根目录（含 osu!.exe / Songs / Skins / osu!*.cfg）");
    Console.WriteLine("                       也接受 Songs 或 Skins 目录，会自动改用其父目录");
    Console.WriteLine();
    Console.WriteLine("功能开关（可组合，都不写时等同 --beats）:");
    Console.WriteLine("  --beats        同步铺面到 stable 的 Songs 目录");
    Console.WriteLine("  --skins        同步皮肤到 <stable-dir>\\Skins");
    Console.WriteLine("  --all          同步以上全部");
    Console.WriteLine();
    Console.WriteLine("其他选项:");
    Console.WriteLine("  --relink       将 stable 中已有的普通文件替换为硬链接，释放磁盘空间");
    Console.WriteLine("                 （要求目标目录与 lazer 在同一磁盘分区）");
    Console.WriteLine("  --help, -h     显示此帮助信息");
    Console.WriteLine();
    Console.WriteLine("示例:");
    Console.WriteLine("  osu-AssetLinker");
    Console.WriteLine("      自动检测路径，只同步铺面");
    Console.WriteLine();
    Console.WriteLine("  osu-AssetLinker --all --relink");
    Console.WriteLine("      同步全部内容，并把已有的普通文件替换为硬链接");
    Console.WriteLine();
    Console.WriteLine("  osu-AssetLinker --lazer-dir \"C:\\Users\\你\\AppData\\Roaming\\osu\" --stable-dir \"C:\\osu!\" --skins");
    Console.WriteLine("      手动指定路径，只同步皮肤");
    Console.WriteLine();
    Console.WriteLine("说明:");
    Console.WriteLine("  · 铺面目标优先使用 stable 配置里的 BeatmapDirectory；没有配置时使用 <stable-dir>\\Songs");
    Console.WriteLine("  · 同一分区时使用硬链接，文件不会被复制，磁盘占用为零");
    Console.WriteLine("  · 不同分区时回退为复制，占用额外磁盘空间");
    Console.WriteLine("  · 运行前请关闭 osu! lazer（Realm 数据库需要独占访问）");
    Console.WriteLine("  · 同步完成后在 stable 中按 F5 刷新铺面列表");
}

// ================================================================
//  参数解析
// ================================================================

static CliParseResult ParseArgs(string[] args)
{
    string? lazerDir = null;
    string? stableDir = null;
    bool relink = false;
    var features = new HashSet<Feature>();

    for (int i = 0; i < args.Length; i++)
    {
        string arg = args[i];
        string? inlineValue = null;

        int equals = arg.IndexOf('=');
        if (equals > 0 && arg.StartsWith("--", StringComparison.Ordinal))
        {
            inlineValue = arg[(equals + 1)..];
            arg = arg[..equals];
        }

        switch (arg)
        {
            case "--lazer-dir":
            case "--stable-dir":
            {
                string? value = inlineValue;
                if (string.IsNullOrEmpty(value))
                {
                    if (i + 1 >= args.Length || args[i + 1].StartsWith('-'))
                        return new(null, $"参数 {arg} 缺少目录值。");
                    value = args[++i];
                }

                if (string.IsNullOrWhiteSpace(value))
                    return new(null, $"参数 {arg} 缺少目录值。");

                if (arg == "--lazer-dir")
                {
                    if (lazerDir != null)
                        return new(null, "参数 --lazer-dir 重复指定。");
                    lazerDir = value;
                }
                else
                {
                    if (stableDir != null)
                        return new(null, "参数 --stable-dir 重复指定。");
                    stableDir = value;
                }

                break;
            }

            case "--relink":
                relink = true;
                break;

            case "--beats":
                features.Add(Feature.Beatmaps);
                break;

            case "--skins":
                features.Add(Feature.Skins);
                break;

            case "--all":
                features.Add(Feature.Beatmaps);
                features.Add(Feature.Skins);
                break;

            default:
                if (arg.StartsWith('-'))
                    return new(null, $"未知参数: {arg}\n提示: 运行 --help 查看用法。");

                return new(null,
                    $"不支持位置参数: {arg}\n" +
                    "提示: 旧写法 [lazer目录] [Songs目录] 已改为命名参数，例如:\n" +
                    "  osu-AssetLinker --lazer-dir \"C:\\Users\\你\\AppData\\Roaming\\osu\" --stable-dir \"C:\\osu!\"");
        }
    }

    var ordered = new List<Feature>();
    if (features.Contains(Feature.Beatmaps)) ordered.Add(Feature.Beatmaps);
    if (features.Contains(Feature.Skins)) ordered.Add(Feature.Skins);
    if (ordered.Count == 0) ordered.Add(Feature.Beatmaps);

    return new(new CliOptions(lazerDir, stableDir, relink, ordered), null);
}

// ================================================================
//  路径检测与解析
// ================================================================

/// <summary>跟随 storage.ini 里的自定义数据目录；发生切换时打印提示。</summary>
static string ResolveLazerDataPath(string candidate)
{
    string resolved = LazerData.ResolveDataDirectory(candidate, out bool redirected);
    if (redirected)
        Ui.Info($"[数据目录] 按 storage.ini 使用自定义数据目录: {resolved}");
    return resolved;
}

static string? DetectLazerDataPath()
{
    string candidate = LazerData.DefaultDirectory;
    if (!Directory.Exists(candidate))
        return null;

    string resolved = ResolveLazerDataPath(candidate);

    // 即使 client.realm 不在，只要目录里有 lazer 的痕迹也返回，
    // 让后续检查给出具体原因，而不是笼统的「自动检测失败」。
    if (LazerData.FindRealmFile(resolved) != null || LazerData.LooksLikeLazerDataDirectory(resolved))
        return resolved;

    return null;
}

/// <summary>CLI 模式：只解析实际选中的目标；铺面必须有 Songs。</summary>
static StablePaths? BuildStablePathsForCli(string stableRoot, List<Feature> features)
{
    string? songsPath = null;

    if (features.Contains(Feature.Beatmaps))
    {
        var songs = StableData.ResolveSongs(stableRoot);
        if (!songs.Exists)
        {
            Ui.Error("错误: 无法定位 stable 的 Songs 目录。");
            PrintSongsDiagnostics(songs, stableRoot);
            Ui.Dim("  请先运行一次 stable 生成配置，或手动创建对应的 Songs 目录。");
            return null;
        }

        if (songs.Warning != null)
            Ui.Warn($"⚠ {songs.Warning}");

        string source = songs.Source == SongsSource.Config && songs.ConfigFile != null
            ? $"（来自 {Path.GetFileName(songs.ConfigFile)}）"
            : "（stable 根目录下的默认位置）";
        Ui.Info($"[铺面] 目标: {songs.Path} {source}");
        songsPath = songs.Path;
    }

    var paths = new StablePaths(stableRoot, songsPath);
    if (features.Contains(Feature.Skins))
        Ui.Info($"[皮肤] 目标: {paths.SkinsPath}");

    return paths;
}

/// <summary>交互模式：Songs 找不到时允许重输根目录 / 手输路径 / 创建目录。</summary>
static StablePaths? ResolveStablePathsInteractive(string initialRoot, List<Feature> features)
{
    string root = initialRoot;
    string? songsPath = null;

    if (!features.Contains(Feature.Beatmaps))
        return new StablePaths(root, null);

    while (songsPath == null)
    {
        var songs = StableData.ResolveSongs(root);

        if (songs.Exists)
        {
            if (songs.Warning != null)
                Ui.Warn($"⚠ {songs.Warning}");

            string source = songs.Source == SongsSource.Config && songs.ConfigFile != null
                ? $"（来自 {Path.GetFileName(songs.ConfigFile)}）"
                : "（stable 根目录下的默认位置）";
            Ui.Info($"  铺面目标: {songs.Path} {source}");
            songsPath = songs.Path;
            break;
        }

        Ui.Error("  无法定位 stable 的 Songs 目录。");
        PrintSongsDiagnostics(songs, root);
        Console.WriteLine();
        Console.WriteLine("请选择:");
        Console.WriteLine("  [1] 重新输入 stable 根目录");
        Console.WriteLine("  [2] 手动输入 Songs 目录");
        Console.WriteLine($"  [3] 创建目录: {songs.Path}");
        Console.Write("选择 [1/2/3]（默认 3）: ");

        string? choice = Console.ReadLine();
        if (choice == null)
            return null;

        switch (choice.Trim())
        {
            case "1":
            {
                string? newRoot = PromptStableRoot(null);
                if (newRoot == null)
                    return null;
                root = newRoot;
                break;
            }

            case "2":
            {
                Console.Write($"请输入 Songs 目录路径（留空使用 {songs.Path}）: ");
                string? entered = Console.ReadLine();
                if (entered == null)
                    return null;

                string path = string.IsNullOrWhiteSpace(entered) ? songs.Path : entered.Trim().Trim('"');
                try
                {
                    path = Path.GetFullPath(path);
                }
                catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException or UnauthorizedAccessException)
                {
                    Ui.Error($"  路径无效: {ex.Message}");
                    break;
                }

                if (!Directory.Exists(path))
                {
                    if (!Ask($"  目录不存在，创建 {path} 吗？", true))
                        break;
                    try
                    {
                        Directory.CreateDirectory(path);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        Ui.Error($"  创建失败: {ex.Message}");
                        break;
                    }
                }

                songsPath = path;
                break;
            }

            case "":
            case "3":
            {
                try
                {
                    Directory.CreateDirectory(songs.Path);
                    Ui.Ok($"  已创建: {songs.Path}");
                    songsPath = songs.Path;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Ui.Error($"  创建失败: {ex.Message}");
                }

                break;
            }

            default:
                Ui.Warn("  请输入 1、2 或 3。");
                break;
        }
    }

    return new StablePaths(root, songsPath);
}

static void PrintSongsDiagnostics(SongsResolution songs, string stableRoot)
{
    string defaultSongs = Path.Combine(stableRoot, StableData.SongsFolderName);
    Ui.Dim($"  已尝试默认位置: {defaultSongs}（{(Directory.Exists(defaultSongs) ? "存在" : "不存在")}）");

    if (songs.ConfigFile != null)
    {
        Ui.Dim($"  配置文件: {songs.ConfigFile}");
        Ui.Dim(songs.ConfiguredValue is null
            ? "            没有 BeatmapDirectory"
            : $"            BeatmapDirectory = {songs.ConfiguredValue}");

        if (songs.Source == SongsSource.Config)
            Ui.Dim($"  解析结果: {songs.Path}（{(songs.Exists ? "存在" : "不存在")}）");
    }
    else
    {
        Ui.Dim("  配置文件: 未找到 osu!*.cfg（stable 可能尚未运行过）");
    }

    if (songs.Error != null)
        Ui.Dim($"  错误: {songs.Error}");
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

/// <summary>交互式提问；返回 null 表示取消。</summary>
static InteractiveResult? InteractivePrompt()
{
    Console.WriteLine("交互模式（双击运行）\n");

    // --- Lazer 数据目录 ---
    string? detectedLazer = DetectLazerDataPath();
    if (detectedLazer != null)
        Console.WriteLine($"[自动检测] Lazer 数据目录: {detectedLazer}");

    string? lazerPath = null;
    while (lazerPath == null)
    {
        Console.Write("请输入 Lazer 数据目录路径（留空使用检测到的）: ");
        string? input = Console.ReadLine();
        if (input == null)
            return null;

        string? candidate = string.IsNullOrWhiteSpace(input) ? detectedLazer : input.Trim().Trim('"');
        if (candidate == null)
        {
            Console.WriteLine("自动检测失败，请手动输入路径");
            continue;
        }

        string resolved = ResolveLazerDataPath(candidate);

        if (LazerData.FindRealmFile(resolved) == null)
        {
            Ui.Error($"错误: 在 '{resolved}' 中找不到 client.realm 文件");
            if (LazerData.DescribeMissingRealm(resolved) is { } hint)
                Ui.Dim(hint);
            continue;
        }

        lazerPath = resolved;
    }

    // --- Stable 根目录 ---
    var roots = StableData.FindRoots();
    if (roots.Count > 1)
        Ui.Dim($"  检测到多个 stable 目录，当前使用 {roots[0]}；可手动输入其他位置。");

    string? stableRoot = PromptStableRoot(roots.Count > 0 ? roots[0] : null);
    if (stableRoot == null)
        return null;

    // --- 要同步的内容（多选）---
    Console.WriteLine("\n要同步哪些内容？（可多选）");
    var features = new List<Feature>();
    if (Ask("  同步铺面 → <stable>\\Songs", true)) features.Add(Feature.Beatmaps);
    if (Ask("  同步皮肤 → <stable>\\Skins", false)) features.Add(Feature.Skins);

    if (features.Count == 0)
    {
        Ui.Warn("  没有选择任何内容，默认只同步铺面。");
        features.Add(Feature.Beatmaps);
    }

    // --- Songs（仅铺面时）---
    StablePaths? stable = ResolveStablePathsInteractive(stableRoot, features);
    if (stable == null)
        return null;

    // --- relink ---
    Console.WriteLine();
    Ui.Dim("  --relink 会把 stable 里已有的【普通文件】替换为硬链接，释放重复占用的磁盘空间。");
    Ui.Dim("  替换前会逐字节比对（文件长度 + SHA-256），内容不同的文件不会被覆盖。");
    bool relink = Ask("是否启用 --relink？", false);

    // --- 执行前确认 ---
    Console.WriteLine();
    Ui.Separator();
    Ui.Info("  请确认以下操作：");
    Console.WriteLine($"    Lazer 数据目录 : {lazerPath}   （只读）");
    Console.WriteLine($"    Stable 根目录  : {stable.Root}");
    if (features.Contains(Feature.Beatmaps) && stable.SongsPath != null)
        Console.WriteLine($"    铺面目标       : {stable.SongsPath}");
    if (features.Contains(Feature.Skins))
        Console.WriteLine($"    皮肤目标       : {stable.SkinsPath}");
    Console.WriteLine($"    relink 模式    : {(relink ? "开（普通文件会替换为硬链接）" : "关")}");
    Ui.Separator();

    if (!Ask("\n确认开始？", true))
        return null;

    Console.WriteLine();
    return new InteractiveResult(lazerPath, stable, features, relink);
}

/// <summary>询问 stable 根目录；返回 null 表示取消。</summary>
static string? PromptStableRoot(string? detected)
{
    string? detectedValue = detected;

    if (detectedValue != null)
        Console.WriteLine($"[自动检测] Stable 根目录: {detectedValue}");

    while (true)
    {
        Console.Write("请输入 osu!stable 根目录（含 osu!.exe / Songs / Skins；留空使用检测到的）: ");
        string? input = Console.ReadLine();
        if (input == null)
            return null;

        string? candidate = string.IsNullOrWhiteSpace(input) ? detectedValue : input.Trim();
        if (candidate == null)
        {
            Console.WriteLine("自动检测失败，请手动输入路径");
            continue;
        }

        var info = StableData.NormalizeRoot(candidate);
        if (info.Note != null)
            Ui.Warn($"  {info.Note}");

        if (!Directory.Exists(info.Root))
        {
            Ui.Error($"错误: 目录不存在: {info.Root}");
            continue;
        }

        if (!StableData.LooksLikeStableRoot(info.Root))
        {
            Ui.Warn("  ⚠ 未检测到 osu!stable 的典型文件/目录（osu!.exe、Songs、Skins、osu!*.cfg）。");
            if (!Ask($"  仍要使用 {info.Root} 吗？", false))
                continue;
        }
        else
        {
            Ui.Ok($"  ✓ 检测到 stable: {string.Join(", ", StableData.DescribeMarkers(info.Root))}");
        }

        return info.Root;
    }
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
//  类型定义
// ================================================================

enum Feature
{
    Beatmaps,
    Skins,
}

sealed record CliOptions(string? LazerDir, string? StableDir, bool Relink, List<Feature> Features);

sealed record CliParseResult(CliOptions? Options, string? Error);

sealed record InteractiveResult(string LazerPath, StablePaths Stable, List<Feature> Features, bool Relink);
