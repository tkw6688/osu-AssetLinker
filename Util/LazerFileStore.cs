namespace OsuAssetLinker;

/// <summary>
/// osu!lazer 的文件仓库（<c>&lt;lazer&gt;/files/</c>）。
/// 文件以内容哈希命名，路径为 <c>files/&lt;第1位&gt;/&lt;前2位&gt;/&lt;完整哈希&gt;</c>。
/// </summary>
public static class LazerFileStore
{
    public static string ResolvePath(string filesRoot, string hash)
        => Path.Combine(filesRoot, hash[..1], hash[..2], hash);
}
