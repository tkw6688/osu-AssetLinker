namespace OsuAssetLinker;

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

    /// <summary>累加一批文件任务的执行结果。</summary>
    public void Add(FileJobResult result)
    {
        FilesLinked += result.Linked;
        FilesCopied += result.Copied;
        FilesRelinked += result.Relinked;
        FilesContentDiffer += result.ContentDiffered;
        FilesFailed += result.Failed;
    }

    public string Summary()
    {
        var parts = new List<string> { $"新增 {Added}", $"跳过 {Skipped}" };
        if (Relinked > 0) parts.Add($"重链接 {Relinked}");
        parts.Add($"失败 {Failed}");
        return string.Join("  ", parts);
    }
}
