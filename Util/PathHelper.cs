using System.Text;

namespace OsuAssetLinker;

/// <summary>文件名/路径清洗与 Windows MAX_PATH 处理。</summary>
public static class PathHelper
{
    /// <summary>Windows 路径长度上限 260，留出结尾 \0 的位置。</summary>
    public const int MaxPath = 259;

    public static string SanitizeName(string name)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder();
        foreach (char c in name)
            sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
        // Windows 不允许文件名/目录名以 '.' 或 ' ' 结尾
        return sb.ToString().Trim().TrimEnd('.');
    }

    /// <summary>
    /// 对铺面内部文件路径的每个分量分别做非法字符替换 + 结尾点号清理。
    /// </summary>
    public static string SanitizeFilePath(string relativePath)
    {
        var parts = relativePath.Split('/');
        char[] invalid = Path.GetInvalidFileNameChars();
        for (int i = 0; i < parts.Length; i++)
        {
            var sb = new StringBuilder();
            foreach (char c in parts[i])
                sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
            parts[i] = sb.ToString().Trim().TrimEnd('.');
            if (string.IsNullOrEmpty(parts[i])) parts[i] = "_";
        }
        return string.Join(Path.DirectorySeparatorChar, parts);
    }

    /// <summary>
    /// 若完整路径超过 Windows MAX_PATH (260) 限制，截断文件名部分（保留扩展名）。
    /// <paramref name="claimed"/> 是本批次已经分配过的路径，用于避让同名冲突。
    /// 冲突判断只依赖已分配集合、不探测磁盘，保证新建与 relink 按相同顺序处理时映射一致。
    /// </summary>
    public static string TruncateForStable(string destPath, int maxTotal = MaxPath, IReadOnlySet<string>? claimed = null)
    {
        if (destPath.Length <= maxTotal && claimed?.Contains(destPath) != true)
            return destPath;

        string dir = Path.GetDirectoryName(destPath) ?? string.Empty;
        string nameNoExt = Path.GetFileNameWithoutExtension(destPath);
        string ext = Path.GetExtension(destPath);
        int maxNameLen = maxTotal - dir.Length - 1 - ext.Length;

        if (maxNameLen < 5)
            throw new PathTooLongException($"路径过长，无法安全截断: {destPath}");

        string baseName = nameNoExt.Length == 0
            ? "_"
            : nameNoExt.Length <= maxNameLen
                ? nameNoExt
                : nameNoExt[..maxNameLen].TrimEnd().TrimEnd('.');

        if (baseName.Length == 0)
            baseName = "_";

        string candidate = Path.Combine(dir, baseName + ext);

        // 截断后可能与本批次中已分配的名字冲突（例如前缀相同的长文件名）
        int counter = 2;
        while (candidate.Length > maxTotal || claimed?.Contains(candidate) == true)
        {
            string suffix = $" ({counter})";
            int adjLen = maxNameLen - suffix.Length;
            if (adjLen < 1) adjLen = 1;

            string stem = baseName.Length <= adjLen ? baseName : baseName[..adjLen];
            stem = stem.TrimEnd().TrimEnd('.');
            if (stem.Length == 0)
                stem = "_";

            candidate = Path.Combine(dir, stem + suffix + ext);
            counter++;
        }

        return candidate;
    }

    /// <summary>控制台输出用：超长字符串截断并加省略号。</summary>
    public static string Ellipsis(string s, int maxLen)
        => s.Length <= maxLen ? s : s[..maxLen] + "…";

    /// <summary>
    /// stable 的铺面文件夹名：<c>{在线ID} {艺术家} - {标题}</c>，无在线 ID 时用本地 ID 前 8 位。
    /// </summary>
    public static string BuildBeatmapFolderName(BeatmapSetInfo set, int maxLength = 200)
    {
        var meta = set.Beatmaps.FirstOrDefault()?.Metadata;
        string artist = SanitizeName(meta?.Artist ?? "Unknown Artist");
        string title = SanitizeName(meta?.Title ?? "Unknown Title");
        string onlineId = set.OnlineID > 0 ? set.OnlineID.ToString() : set.ID.ToString()[..8];
        string name = $"{onlineId} {artist} - {title}";
        if (name.Length > maxLength)
            name = name[..maxLength].TrimEnd().TrimEnd('.');
        return name;
    }
}
