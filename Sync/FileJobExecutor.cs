namespace OsuAssetLinker;

/// <summary>一批文件任务的执行结果。</summary>
public sealed class FileJobResult
{
    public long Linked { get; set; }
    public long Copied { get; set; }
    public long Relinked { get; set; }
    public long ContentDiffered { get; set; }
    public long Failed { get; set; }
}

/// <summary>
/// 统一执行文件任务：处理源文件缺失、目标是否已存在，逐个链接/复制并计数。
/// 铺面与皮肤共用，保证两类内容的失败统计与 relink 语义一致。
/// </summary>
public static class FileJobExecutor
{
    public static FileJobResult Execute(IEnumerable<FileJob> jobs, FileJobMode mode)
    {
        bool relink = mode == FileJobMode.Relink;
        var result = new FileJobResult();

        foreach (var job in jobs)
        {
            if (!File.Exists(job.SourcePath))
            {
                // relink 只是优化已有文件；源文件不在了就跳过。新建时缺失则记为失败。
                if (!relink)
                    result.Failed++;
                continue;
            }

            // relink 不补充缺失文件：stable 里没有该文件就保持原样。
            if (relink && !File.Exists(job.DestPath))
                continue;

            try
            {
                if (!relink)
                    Directory.CreateDirectory(Path.GetDirectoryName(job.DestPath)!);

                switch (FileSync.LinkOne(job.SourcePath, job.DestPath, relink))
                {
                    case FileSync.Outcome.Linked:
                        result.Linked++;
                        break;
                    case FileSync.Outcome.Copied:
                        result.Copied++;
                        break;
                    case FileSync.Outcome.Relinked:
                        result.Relinked++;
                        break;
                    case FileSync.Outcome.ContentDiffers:
                        result.ContentDiffered++;
                        break;
                }
            }
            catch
            {
                result.Failed++;
            }
        }

        return result;
    }

    /// <summary>
    /// 把 Realm 里的文件列表转换为任务列表。顺序与 <c>Files</c> 一致，
    /// 使 <see cref="DestPathResolver"/> 在新建和 relink 时得到相同的路径映射。
    /// </summary>
    public static List<FileJob> FromFiles(IEnumerable<RealmNamedFileUsage> files, string lazerFilesPath, string destDir)
    {
        var resolver = new DestPathResolver();
        var jobs = new List<FileJob>();

        foreach (var namedFile in files)
        {
            if (namedFile.File is null) continue;

            string sourcePath = LazerFileStore.ResolvePath(lazerFilesPath, namedFile.File.Hash);
            string destPath = resolver.Resolve(destDir, namedFile.Filename);
            jobs.Add(new FileJob(sourcePath, destPath));
        }

        return jobs;
    }
}
