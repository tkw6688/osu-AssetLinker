namespace OsuAssetLinker;

/// <summary>一次文件同步任务：从 lazer 文件仓库链接到 stable 的目标路径。</summary>
public readonly record struct FileJob(string SourcePath, string DestPath);

/// <summary>文件任务的执行方式。</summary>
public enum FileJobMode
{
    /// <summary>新建：目标缺失时建立链接，目标已存在则跳过。</summary>
    Sync,

    /// <summary>--relink：只把已存在的普通文件替换为硬链接，不补充缺失文件。</summary>
    Relink,
}
