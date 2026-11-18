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
