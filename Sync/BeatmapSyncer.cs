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
                Relink(ctx, set, destDir, stats);
            }
            else
            {
                Ui.Inline($"  [{index}/{stats.Total}] {PathHelper.Ellipsis(folderName, 60)}... ");
                Sync(ctx, set, destDir, stats);
            }

            Console.WriteLine();
        }

        return stats;
    }

    /// <summary>正常同步一个铺面集（新增，使用硬链接或复制）。</summary>
    private static void Sync(SyncContext ctx, BeatmapSetInfo set, string destDir, SyncStats stats)
    {
        // 只有本次新建的目录才能在失败时整体清理；
        // 已存在的目录里可能有用户自己的文件，绝不能因为一次失败就删掉。
        bool createdDir = !Directory.Exists(destDir);

        try
        {
            Directory.CreateDirectory(destDir);

            var result = FileJobExecutor.Execute(
                FileJobExecutor.FromFiles(set.Files, ctx.LazerFilesPath, destDir), FileJobMode.Sync);
            stats.Add(result);

            if (result.Failed > 0)
            {
                Ui.InlineError($"✗ {result.Failed} 个文件失败");
                if (createdDir)
                    TryDeleteDirectory(destDir);
                stats.Failed++;
                return;
            }

            Ui.InlineOk("✓");
            stats.Added++;
        }
        catch (Exception ex)
        {
            Ui.InlineError($"✗ {ex.Message}");
            if (createdDir)
                TryDeleteDirectory(destDir);
            stats.Failed++;
        }
    }

    /// <summary>
    /// --relink：把 stable 中已有的普通文件替换为硬链接。
    /// 路径映射与 <see cref="Sync"/> 完全一致，长文件名不会因为目标已存在而被改名跳过。
    /// </summary>
    private static void Relink(SyncContext ctx, BeatmapSetInfo set, string destDir, SyncStats stats)
    {
        try
        {
            var result = FileJobExecutor.Execute(
                FileJobExecutor.FromFiles(set.Files, ctx.LazerFilesPath, destDir), FileJobMode.Relink);
            stats.Add(result);

            if (result.Relinked > 0)
            {
                Ui.InlineAccent($"✓ 释放 {result.Relinked} 个文件");
                stats.Relinked++;
            }
            else
            {
                Ui.InlineDim("已是硬链接，跳过");
                stats.Skipped++;
            }

            if (result.ContentDiffered > 0)
                Ui.InlineError($"  ⚠ {result.ContentDiffered} 个文件内容不同，未覆盖");

            if (result.Failed > 0)
            {
                stats.Failed++;
                Ui.InlineError($"  ({result.Failed} 个文件失败)");
            }
        }
        catch (Exception ex)
        {
            Ui.InlineError($"✗ {ex.Message}");
            stats.Failed++;
        }
    }

    /// <summary>目录里已经有 .osu 文件即视为同步过。</summary>
    private static bool IsAlreadySynced(string destDir)
        => Directory.Exists(destDir) && Directory.GetFiles(destDir, "*.osu").Length > 0;

    private static void TryDeleteDirectory(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
        }
        catch
        {
            // 清理失败不影响统计
        }
    }
}
