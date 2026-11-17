namespace OsuAssetLinker;

/// <summary>把 lazer 的铺面集同步到 stable 的 Songs 目录。</summary>
public static class BeatmapSyncer
{
    public static SyncStats Run(SyncContext ctx)
    {
        string songsPath = ctx.SongsPath
            ?? throw new InvalidOperationException("铺面同步需要 Songs 目录，但当前同步上下文未配置。");

        var stats = new SyncStats { Label = "铺面" };
        var sets = ctx.Realm.All<BeatmapSetInfo>().Where(s => !s.DeletePending).ToList();
        stats.Total = sets.Count;

        Ui.Info($"共找到 {stats.Total} 个铺面集\n");

        foreach (var set in sets)
        {
            int index = stats.Added + stats.Skipped + stats.Relinked + stats.Failed + 1;

            // 动态计算文件夹名最大长度，确保总路径不超 Windows MAX_PATH (260)
            int longestFileLen = set.Files
                .Where(f => f.File != null)
                .Select(f => PathHelper.SanitizeFilePath(f.Filename).Length)
                .DefaultIfEmpty(0)
                .Max();
            // 总路径 = Songs + '\' + folderName + '\' + fileName <= 259
            int maxFolderLen = Math.Clamp(PathHelper.MaxPath - songsPath.Length - 1 - longestFileLen - 1, 20, 200);

            string folderName = PathHelper.BuildBeatmapFolderName(set, maxFolderLen);
            string destDir = Path.Combine(songsPath, folderName);
            bool alreadyExists = IsAlreadySynced(destDir);

            if (alreadyExists && !ctx.Relink)
            {
                stats.Skipped++;
                continue;
            }

            if (alreadyExists)
            {
                Ui.Inline($"  [{index}/{stats.Total}] [relink] {PathHelper.Ellipsis(folderName, 55)}... ");
                RunGuarded(stats, () =>
                {
                    long differedBefore = stats.FilesContentDiffer;
                    long replaced = Relink(ctx, set, destDir, stats);
                    long differed = stats.FilesContentDiffer - differedBefore;

                    if (replaced > 0)
                    {
                        Ui.InlineAccent($"✓ 释放 {replaced} 个文件");
                        stats.Relinked++;
                    }
                    else
                    {
                        Ui.InlineDim("已是硬链接，跳过");
                        stats.Skipped++;
                    }

                    if (differed > 0)
                        Ui.InlineError($"  ⚠ {differed} 个文件内容不同，未覆盖");

                    Console.WriteLine();
                });

                continue;
            }

            Ui.Inline($"  [{index}/{stats.Total}] {PathHelper.Ellipsis(folderName, 60)}... ");
            try
            {
                Directory.CreateDirectory(destDir);
                Sync(ctx, set, destDir, stats);
                Ui.InlineOk("✓");
                Console.WriteLine();
                stats.Added++;
            }
            catch (Exception ex)
            {
                Ui.InlineError($"✗ {ex.Message}");
                Console.WriteLine();
                try
                {
                    if (Directory.Exists(destDir)) Directory.Delete(destDir, true);
                }
                catch
                {
                    // 清理失败不影响统计
                }
                stats.Failed++;
            }
        }

        return stats;
    }

    private static void RunGuarded(SyncStats stats, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Ui.InlineError($"✗ {ex.Message}");
            Console.WriteLine();
            stats.Failed++;
        }
    }

    /// <summary>目录里已经有 .osu 文件即视为同步过。</summary>
    private static bool IsAlreadySynced(string destDir)
        => Directory.Exists(destDir) && Directory.GetFiles(destDir, "*.osu").Length > 0;

    /// <summary>正常同步一个铺面集（新增，使用硬链接或复制）。</summary>
    private static void Sync(SyncContext ctx, BeatmapSetInfo set, string destDir, SyncStats stats)
    {
        foreach (var namedFile in set.Files)
        {
            if (namedFile.File is null) continue;

            string sourcePath = LazerFileStore.ResolvePath(ctx.LazerFilesPath, namedFile.File.Hash);
            if (!File.Exists(sourcePath))
                throw new FileNotFoundException($"Lazer 文件缺失: {sourcePath}");

            string destPath = ResolveDestPath(destDir, namedFile.Filename);
            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);

            FileSync.Record(FileSync.LinkOne(sourcePath, destPath, relink: false), stats);
        }
    }

    /// <summary>
    /// --relink：遍历铺面集里的每个文件，把 stable 中已有的普通文件替换为硬链接。
    /// </summary>
    private static long Relink(SyncContext ctx, BeatmapSetInfo set, string destDir, SyncStats stats)
    {
        long replaced = 0;

        foreach (var namedFile in set.Files)
        {
            if (namedFile.File is null) continue;

            string sourcePath = LazerFileStore.ResolvePath(ctx.LazerFilesPath, namedFile.File.Hash);
            if (!File.Exists(sourcePath)) continue; // lazer 源文件不存在则跳过

            string destPath = ResolveDestPath(destDir, namedFile.Filename);
            if (!File.Exists(destPath)) continue; // stable 里没有该文件则不补

            var outcome = FileSync.LinkOne(sourcePath, destPath, relink: true);
            FileSync.Record(outcome, stats);

            if (outcome == FileSync.Outcome.Relinked)
                replaced++;
        }

        return replaced;
    }

    private static string ResolveDestPath(string destDir, string relativeFilename)
        => PathHelper.TruncateForStable(
            Path.Combine(destDir, PathHelper.SanitizeFilePath(relativeFilename)));
}
