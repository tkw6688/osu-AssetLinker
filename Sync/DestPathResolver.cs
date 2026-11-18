namespace OsuAssetLinker;

/// <summary>
/// 为一个目标文件夹分配文件名：路径过长时截断，并避让本批次中已占用的名字。
///
/// 只依赖本批次已分配的名字、不探测磁盘，因此「新建」与「relink」按相同顺序
/// 处理同一组文件时会得到完全一致的映射。
/// </summary>
public sealed class DestPathResolver
{
    private readonly HashSet<string> claimed = new(StringComparer.OrdinalIgnoreCase);

    public string Resolve(string destDir, string relativeFilename)
    {
        string path = PathHelper.TruncateForStable(
            Path.Combine(destDir, PathHelper.SanitizeFilePath(relativeFilename)),
            PathHelper.MaxPath,
            claimed);

        claimed.Add(path);
        return path;
    }
}
