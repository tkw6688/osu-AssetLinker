namespace OsuAssetLinker;

/// <summary>stable 的铺面文件夹命名。</summary>
public static class BeatmapFolderNaming
{
    /// <summary>
    /// stable 的铺面文件夹名：<c>{在线ID} {艺术家} - {标题}</c>，无在线 ID 时用本地 ID 前 8 位。
    /// </summary>
    public static string Build(BeatmapSetInfo set, int maxLength = 200)
    {
        var meta = set.Beatmaps.FirstOrDefault()?.Metadata;
        string artist = PathSanitizer.SanitizeName(meta?.Artist ?? "Unknown Artist");
        string title = PathSanitizer.SanitizeName(meta?.Title ?? "Unknown Title");
        string onlineId = set.OnlineID > 0 ? set.OnlineID.ToString() : set.ID.ToString()[..8];
        string name = $"{onlineId} {artist} - {title}";
        if (name.Length > maxLength)
            name = name[..maxLength].TrimEnd().TrimEnd('.');
        return name;
    }
}
