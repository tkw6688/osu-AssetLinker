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

/// <summary>单个同步器的统计结果。</summary>
public sealed class SyncStats
{
    public string Label { get; init; } = string.Empty;

    /// <summary>条目总数（铺面集 / 皮肤 / 成绩）。</summary>
    public int Total { get; set; }

    public int Added { get; set; }
    public int Skipped { get; set; }
    public int Relinked { get; set; }
    public int Failed { get; set; }

    /// <summary>本次新建的硬链接数。</summary>
    public long FilesLinked { get; set; }

    /// <summary>本次复制的文件数（硬链接不可用时）。</summary>
    public long FilesCopied { get; set; }

    /// <summary>--relink 模式下被替换为硬链接的文件数。</summary>
    public long FilesRelinked { get; set; }

    /// <summary>同步失败的文件数（条目本身可能仍然成功）。</summary>
    public long FilesFailed { get; set; }

    /// <summary>
    /// --relink 时因两边内容不同而被跳过的文件数。
    /// stable 里那份可能是用户改过的，覆盖会丢数据，所以只报告不替换。
    /// </summary>
    public long FilesContentDiffer { get; set; }

    public bool HasFailures => Failed > 0;

    public string Summary()
    {
        var parts = new List<string> { $"新增 {Added}", $"跳过 {Skipped}" };
        if (Relinked > 0) parts.Add($"重链接 {Relinked}");
        parts.Add($"失败 {Failed}");
        return string.Join("  ", parts);
    }
}
