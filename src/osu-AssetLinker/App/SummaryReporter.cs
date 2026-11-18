namespace OsuAssetLinker;

/// <summary>同步结束后的汇总输出。</summary>
public static class SummaryReporter
{
    public static void Print(List<SyncStats> stats)
    {
        Ui.Separator();
        Ui.Info("  同步完成！");

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
            if (linked > 0) Console.WriteLine($"  新建硬链接: {linked} 个文件（零额外磁盘占用）");
            if (relinked > 0) Console.WriteLine($"  替换为硬链接: {relinked} 个文件");
            if (copied > 0) Ui.Warn($"  复制文件: {copied} 个（硬链接不可用，占用额外磁盘）");
            if (contentDiffer > 0) Ui.Warn($"  内容不同未覆盖: {contentDiffer} 个文件（stable 里的版本可能被改过，保持原样）");
            if (filesFailed > 0) Ui.Error($"  失败文件: {filesFailed} 个");
        }

        Ui.Separator();
    }
}
