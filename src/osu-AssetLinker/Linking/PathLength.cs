namespace OsuAssetLinker;

/// <summary>Windows MAX_PATH 限制与超长路径截断。</summary>
public static class PathLength
{
    /// <summary>Windows 路径长度上限 260，留出结尾 \0 的位置。</summary>
    public const int MaxPath = 259;

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
}
