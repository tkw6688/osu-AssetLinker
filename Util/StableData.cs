namespace OsuAssetLinker;

/// <summary>一次同步用到的 stable 路径；纯皮肤模式下 <see cref="SongsPath"/> 为 null。</summary>
public sealed record StablePaths(string Root, string? SongsPath)
{
    public string SkinsPath => Path.Combine(Root, "Skins");
}

/// <summary>Songs 路径的来源。</summary>
public enum SongsSource
{
    /// <summary>没有可用的 cfg 配置，使用 <c>&lt;root&gt;\Songs</c>。</summary>
    Default,

    /// <summary>来自 stable 用户配置里的 <c>BeatmapDirectory</c>。</summary>
    Config,
}

/// <summary>Songs 目录的解析结果。</summary>
public sealed record SongsResolution(
    string Path,
    SongsSource Source,
    bool Exists,
    string? ConfigFile,
    string? ConfiguredValue,
    string? Warning,
    string? Error);

/// <summary>stable 根目录输入的规范化结果。</summary>
public sealed record StableRootInfo(string Root, bool RedirectedFromLeaf, string? Note);

/// <summary>cfg 中 BeatmapDirectory 的解析结果。</summary>
public sealed record BeatmapDirectoryResult(string? RawValue, string? AbsolutePath, string? Error);

/// <summary>
/// 定位 osu!stable 的根目录、用户配置与 Songs 目录。
///
/// stable 的用户配置是根目录下的 <c>osu!.{用户名}.cfg</c>（UTF-8/CRLF，
/// <c>Key = Value</c>），其中的 <c>BeatmapDirectory</c> 才是 stable 实际使用的铺面目录；
/// <c>osu!.cfg</c> 是更新清单，不含该键，不能当作用户配置。
/// </summary>
public static class StableData
{
    public const string SongsFolderName = "Songs";
    public const string SkinsFolderName = "Skins";

    /// <summary>自动检测时依次尝试的 stable 根目录。</summary>
    public static string[] CandidateRoots() =>
    [
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "osu!"),
        @"C:\osu!",
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "osu!"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".osu"),
    ];

    /// <summary>返回所有看起来像 stable 根目录的候选（按优先级排序）。</summary>
    public static IReadOnlyList<string> FindRoots()
    {
        var roots = new List<string>();

        foreach (string candidate in CandidateRoots().Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (Directory.Exists(candidate) && LooksLikeStableRoot(candidate))
                    roots.Add(Path.GetFullPath(candidate));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                // 候选目录不可访问时跳过
            }
        }

        return roots;
    }

    /// <summary>自动检测 stable 根目录；找不到返回 null。</summary>
    public static string? DetectRoot()
    {
        var roots = FindRoots();
        return roots.Count > 0 ? roots[0] : null;
    }

    /// <summary>目录是否具备 stable 的典型特征（osu!.exe、Songs、Skins 或 osu!*.cfg）。</summary>
    public static bool LooksLikeStableRoot(string directory)
    {
        try
        {
            if (!Directory.Exists(directory))
                return false;

            return File.Exists(Path.Combine(directory, "osu!.exe"))
                   || Directory.Exists(Path.Combine(directory, SongsFolderName))
                   || Directory.Exists(Path.Combine(directory, SkinsFolderName))
                   || Directory.GetFiles(directory, "osu!*.cfg").Length > 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>返回命中的 stable 特征，用于交互提示。</summary>
    public static IReadOnlyList<string> DescribeMarkers(string directory)
    {
        var markers = new List<string>();

        try
        {
            if (!Directory.Exists(directory))
                return markers;

            if (File.Exists(Path.Combine(directory, "osu!.exe"))) markers.Add("osu!.exe");
            if (Directory.Exists(Path.Combine(directory, SongsFolderName))) markers.Add("Songs/");
            if (Directory.Exists(Path.Combine(directory, SkinsFolderName))) markers.Add("Skins/");
            if (Directory.GetFiles(directory, "osu!*.cfg").Length > 0) markers.Add("osu!*.cfg");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 忽略探测失败
        }

        return markers;
    }

    /// <summary>
    /// 规范化用户输入：去引号/首尾空白、转绝对路径；如果叶子名是 <c>Songs</c>/<c>Skins</c>
    /// 且父目录像 stable 根，则改用父目录。
    /// </summary>
    public static StableRootInfo NormalizeRoot(string input)
    {
        string trimmed = input.Trim().Trim('"').Trim();
        if (trimmed.Length == 0)
            return new(string.Empty, false, "路径为空。");

        string path;
        try
        {
            path = Path.GetFullPath(trimmed);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException or UnauthorizedAccessException)
        {
            return new(trimmed, false, $"路径无效: {ex.Message}");
        }

        string leaf = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
        if (!leaf.Equals(SongsFolderName, StringComparison.OrdinalIgnoreCase) &&
            !leaf.Equals(SkinsFolderName, StringComparison.OrdinalIgnoreCase))
        {
            return new(path, false, null);
        }

        string? parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(path));
        if (!string.IsNullOrEmpty(parent) && LooksLikeStableRoot(parent))
            return new(parent, true, $"输入的是 {leaf} 目录，已改用其父目录作为 stable 根目录: {parent}");

        return new(path, false, $"输入的路径看起来是 {leaf} 目录而不是 stable 根目录，请确认。");
    }

    /// <summary>
    /// 查找用户配置：<c>osu!.{用户名}.cfg</c> → 其他 <c>osu!.*.cfg</c>（最近修改优先）
    /// → 最后才 <c>osu!.cfg</c>（更新清单，通常没有 BeatmapDirectory）。
    /// </summary>
    public static string? FindConfigFile(string stableRoot)
    {
        try
        {
            string userConfig = Path.Combine(stableRoot, $"osu!.{Environment.UserName}.cfg");
            if (File.Exists(userConfig))
                return userConfig;

            var candidates = Directory.GetFiles(stableRoot, "osu!.*.cfg")
                .Where(path => !Path.GetFileName(path).Equals("osu!.cfg", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .ToList();

            if (candidates.Count > 0)
                return candidates[0];

            string plainConfig = Path.Combine(stableRoot, "osu!.cfg");
            return File.Exists(plainConfig) ? plainConfig : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>读取并解析 cfg 中的 <c>BeatmapDirectory</c>（绝对/相对 root）。</summary>
    public static BeatmapDirectoryResult ReadBeatmapDirectory(string stableRoot, string configFile)
    {
        string? raw = null;

        try
        {
            foreach (string line in File.ReadLines(configFile))
            {
                string trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed[0] is '#' or ';' or '/')
                    continue;

                int separator = trimmed.IndexOf('=');
                if (separator < 0)
                    separator = trimmed.IndexOf(':');
                if (separator <= 0)
                    continue;

                string key = trimmed[..separator].Trim();
                if (!key.Equals("BeatmapDirectory", StringComparison.OrdinalIgnoreCase))
                    continue;

                raw = trimmed[(separator + 1)..].Trim();
                break;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new(null, null, $"无法读取配置文件 {configFile}: {ex.Message}");
        }

        if (raw == null)
            return new(null, null, null);

        string value = raw;
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
            value = value[1..^1];

        value = Environment.ExpandEnvironmentVariables(value.Trim());

        if (string.IsNullOrWhiteSpace(value))
            return new(raw, null, "配置中的 BeatmapDirectory 为空。");

        try
        {
            string absolute = Path.IsPathRooted(value)
                ? Path.GetFullPath(value)
                : Path.GetFullPath(Path.Combine(stableRoot, value));
            return new(raw, absolute, null);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException or UnauthorizedAccessException)
        {
            return new(raw, null, $"配置中的 BeatmapDirectory 路径无效: {raw}");
        }
    }

    /// <summary>
    /// 定位 Songs：cfg 里的 <c>BeatmapDirectory</c> 优先（stable 实际生效的设置），
    /// 没有 cfg/没有该键时才回退 <c>&lt;root&gt;\Songs</c>。
    /// </summary>
    public static SongsResolution ResolveSongs(string stableRoot)
    {
        string defaultSongs = Path.Combine(stableRoot, SongsFolderName);
        string? configFile = FindConfigFile(stableRoot);
        BeatmapDirectoryResult? config = null;

        if (configFile != null)
        {
            config = ReadBeatmapDirectory(stableRoot, configFile);

            if (config.AbsolutePath != null)
            {
                bool exists = Directory.Exists(config.AbsolutePath);
                string? warning = null;

                if (exists && Directory.Exists(defaultSongs) &&
                    !string.Equals(
                        Path.TrimEndingDirectorySeparator(config.AbsolutePath),
                        Path.TrimEndingDirectorySeparator(defaultSongs),
                        StringComparison.OrdinalIgnoreCase))
                {
                    warning = $"配置文件里的 BeatmapDirectory 与默认 Songs 目录不同，实际使用: {config.AbsolutePath}";
                }

                return new(config.AbsolutePath, SongsSource.Config, exists, configFile, config.RawValue, warning, config.Error);
            }
        }

        return new(defaultSongs, SongsSource.Default, Directory.Exists(defaultSongs), configFile, config?.RawValue, null, config?.Error);
    }
}
