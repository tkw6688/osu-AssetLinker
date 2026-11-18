using Realms;

namespace OsuAssetLinker;

/// <summary>一次同步任务需要的全部路径与开关。</summary>
public sealed class SyncContext
{
    public required Realm Realm { get; init; }

    /// <summary>osu!lazer 的 <c>files/</c> 目录（只读）。</summary>
    public required string LazerFilesPath { get; init; }

    /// <summary>osu!stable 的根目录、Songs 与 Skins 路径。</summary>
    public required StablePaths Stable { get; init; }

    /// <summary>--relink：把已有的普通文件替换为硬链接。</summary>
    public required bool Relink { get; init; }

    public string StableRoot => Stable.Root;

    /// <summary>osu!stable 的 Songs 目录；纯皮肤模式为 null。</summary>
    public string? SongsPath => Stable.SongsPath;

    public string SkinsPath => Stable.SkinsPath;
}
