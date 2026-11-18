namespace OsuAssetLinker;

/// <summary>命令行与交互向导共用的 lazer/stable 路径检测与解析。</summary>
public static class PathResolution
{
    /// <summary>跟随 storage.ini 里的自定义数据目录；发生切换时打印提示。</summary>
    public static string ResolveLazerDataPath(string candidate)
    {
        string resolved = LazerData.ResolveDataDirectory(candidate, out bool redirected);
        if (redirected)
            Ui.Info($"[数据目录] 按 storage.ini 使用自定义数据目录: {resolved}");
        return resolved;
    }

    public static string? DetectLazerDataPath()
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
    public static StablePaths? BuildStablePathsForCli(string stableRoot, List<SyncFeature> features)
    {
        string? songsPath = null;

        if (features.Contains(SyncFeature.Beatmaps))
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
        if (features.Contains(SyncFeature.Skins))
        {
            Ui.Info($"[皮肤] 目标: {paths.SkinsPath}");
            if (!Directory.Exists(paths.SkinsPath))
                Ui.Dim("  目录不存在，正式同步时会自动创建（--dry-run 不会）。");
        }

        return paths;
    }

    public static void PrintSongsDiagnostics(SongsResolution songs, string stableRoot)
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
}
