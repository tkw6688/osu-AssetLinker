namespace OsuAssetLinker;

/// <summary>同步结束后的汇总输出。</summary>
public static class SummaryReporter
{
    public static void Print(List<SyncStats> stats, bool dryRun)
    {
        Ui.Separator();
        Ui.Info(dryRun ? "  预览完成（未写入任何文件）" : "  同步完成！");

        foreach (var s in stats)
            Console.WriteLine($"  {s.Label}    {s.Summary()}");

        long linked = stats.Sum(f => f.FilesLinked);
        long copied = stats.Sum(f => f.FilesCopied);
        long relinked = stats.Sum(f => f.FilesRelinked);
        long filesFailed = stats.Sum(f => f.FilesFailed);
        long contentDiffer = stats.Sum(f => f.FilesContentDiffer);

        if (linked + copied + relinked + contentDiffer + filesFailed > 0)
        {
            Console.WriteLine();
            if (linked > 0) Console.WriteLine(dryRun
                ? $"  将新建硬链接: {linked} 个文件（零额外磁盘占用）"
                : $"  新建硬链接: {linked} 个文件（零额外磁盘占用）");
            if (relinked > 0) Console.WriteLine(dryRun
                ? $"  将替换为硬链接: {relinked} 个文件"
                : $"  替换为硬链接: {relinked} 个文件");
            if (copied > 0) Ui.Warn(dryRun
                ? $"  将复制文件: {copied} 个（硬链接不可用，会占用额外磁盘）"
                : $"  复制文件: {copied} 个（硬链接不可用，占用额外磁盘）");
            if (contentDiffer > 0) Ui.Warn(dryRun
                ? $"  内容不同，将不覆盖: {contentDiffer} 个文件（stable 里的版本可能被改过，保持原样）"
                : $"  内容不同未覆盖: {contentDiffer} 个文件（stable 里的版本可能被改过，保持原样）");
            if (filesFailed > 0) Ui.Error(dryRun
                ? $"  将失败的文件: {filesFailed} 个"
                : $"  失败文件: {filesFailed} 个");
        }

        Ui.Separator();
    }
}
