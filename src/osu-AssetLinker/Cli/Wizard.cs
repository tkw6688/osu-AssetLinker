namespace OsuAssetLinker;

/// <summary>双击运行时的交互式向导。</summary>
public static class Wizard
{
    /// <summary>依次询问 lazer/stable 路径、同步内容与 relink；返回 null 表示取消。</summary>
    public static InteractiveResult? Run()
    {
        Console.WriteLine("交互模式（双击运行）\n");

        // --- Lazer 数据目录 ---
        string? detectedLazer = PathResolution.DetectLazerDataPath();
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

            string resolved = PathResolution.ResolveLazerDataPath(candidate);

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
        var features = new List<SyncFeature>();
        if (ConsoleSession.Ask("  同步铺面 → <stable>\\Songs", true)) features.Add(SyncFeature.Beatmaps);
        if (ConsoleSession.Ask("  同步皮肤 → <stable>\\Skins", false)) features.Add(SyncFeature.Skins);

        if (features.Count == 0)
        {
            Ui.Warn("  没有选择任何内容，默认只同步铺面。");
            features.Add(SyncFeature.Beatmaps);
        }

        // --- Songs（仅铺面时）---
        StablePaths? stable = ResolveStablePathsInteractive(stableRoot, features);
        if (stable == null)
            return null;

        // --- relink ---
        Console.WriteLine();
        Ui.Dim("  --relink 会把 stable 里已有的【普通文件】替换为硬链接，释放重复占用的磁盘空间。");
        Ui.Dim("  替换前会逐字节比对（文件长度 + SHA-256），内容不同的文件不会被覆盖。");
        bool relink = ConsoleSession.Ask("是否启用 --relink？", false);

        // --- 执行前确认 ---
        Console.WriteLine();
        Ui.Separator();
        Ui.Info("  请确认以下操作：");
        Console.WriteLine($"    Lazer 数据目录 : {lazerPath}   （只读）");
        Console.WriteLine($"    Stable 根目录  : {stable.Root}");
        if (features.Contains(SyncFeature.Beatmaps) && stable.SongsPath != null)
            Console.WriteLine($"    铺面目标       : {stable.SongsPath}");
        if (features.Contains(SyncFeature.Skins))
            Console.WriteLine($"    皮肤目标       : {stable.SkinsPath}");
        Console.WriteLine($"    relink 模式    : {(relink ? "开（普通文件会替换为硬链接）" : "关")}");
        Ui.Separator();

        if (!ConsoleSession.Ask("\n确认开始？", true))
            return null;

        Console.WriteLine();
        return new InteractiveResult(lazerPath, stable, features, relink);
    }

    /// <summary>交互模式：Songs 找不到时允许重输根目录 / 手输路径 / 创建目录。</summary>
    private static StablePaths? ResolveStablePathsInteractive(string initialRoot, List<SyncFeature> features)
    {
        string root = initialRoot;
        string? songsPath = null;

        if (!features.Contains(SyncFeature.Beatmaps))
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
            PathResolution.PrintSongsDiagnostics(songs, root);
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
                        if (!ConsoleSession.Ask($"  目录不存在，创建 {path} 吗？", true))
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

    /// <summary>询问 stable 根目录；返回 null 表示取消。</summary>
    private static string? PromptStableRoot(string? detected)
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
                if (!ConsoleSession.Ask($"  仍要使用 {info.Root} 吗？", false))
                    continue;
            }
            else
            {
                Ui.Ok($"  ✓ 检测到 stable: {string.Join(", ", StableData.DescribeMarkers(info.Root))}");
            }

            return info.Root;
        }
    }
}

/// <summary>交互向导收集到的全部选项。</summary>
public sealed record InteractiveResult(string LazerPath, StablePaths Stable, List<SyncFeature> Features, bool Relink);
