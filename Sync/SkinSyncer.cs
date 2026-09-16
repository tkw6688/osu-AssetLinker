namespace OsuAssetLinker;

/// <summary>
/// 把 lazer 里已导入的皮肤同步到 stable 的 Skins 目录。
///
/// lazer 的皮肤文件是扁平的（不含子目录），并且自带 skin.ini，
/// 所以直接按文件夹铺开即可被 stable 识别。
/// </summary>
public static class SkinSyncer
{
    /// <summary>给文件名预留的长度，避免皮肤名过长把 MAX_PATH 撑爆。</summary>
    private const int FilenameReserve = 60;

    public static SyncStats Run(SyncContext ctx)
    {
        var stats = new SyncStats { Label = "皮肤" };

        // Realm 的 LINQ 不支持 Files.Count，先取回内存再过滤
        var skins = ctx.Realm.All<SkinInfo>().ToList()
            .Where(s => !s.DeletePending && !s.Protected && s.Files.Count > 0)
            .ToList();

        stats.Total = skins.Count;
        Ui.Info($"共找到 {stats.Total} 个可导出的皮肤\n");

        if (skins.Count == 0)
            return stats;

        string skinsPath = ctx.SkinsPath;
        if (!Directory.Exists(skinsPath))
        {
            Ui.Warn($"创建 Skins 目录: {skinsPath}");
            Directory.CreateDirectory(skinsPath);
        }

        foreach (var skin in skins)
        {
            int index = stats.Added + stats.Skipped + stats.Relinked + stats.Failed + 1;

            string destDir = ResolveDestDir(ctx, skin);
            string folderName = Path.GetFileName(destDir);
            bool alreadyExists = Directory.Exists(destDir) && Directory.EnumerateFileSystemEntries(destDir).Any();

            if (alreadyExists && !ctx.Relink)
            {
                stats.Skipped++;
                continue;
            }

            string prefix = alreadyExists
                ? $"  [{index}/{stats.Total}] [relink] {PathHelper.Ellipsis(folderName, 50)}... "
                : $"  [{index}/{stats.Total}] {PathHelper.Ellipsis(folderName, 55)}... ";

            Ui.Inline(prefix);

            try
            {
                var (linked, copied, relinked, differed, failed) = alreadyExists
                    ? Relink(ctx, skin, destDir)
                    : Sync(ctx, skin, destDir);

                stats.FilesLinked += linked;
                stats.FilesCopied += copied;
                stats.FilesRelinked += relinked;
                stats.FilesContentDiffer += differed;
                stats.FilesFailed += failed;

                if (alreadyExists)
                {
                    if (relinked > 0)
                    {
                        Ui.InlineAccent($"✓ 释放 {relinked} 个文件");
                        stats.Relinked++;
                    }
                    else
                    {
                        Ui.InlineDim("已是硬链接，跳过");
                        stats.Skipped++;
                    }

                    if (differed > 0)
                        Ui.InlineError($"  ⚠ {differed} 个文件内容不同，未覆盖");
                }
                else
                {
                    Ui.InlineOk($"✓ {linked + copied + failed} 个文件");
                    stats.Added++;
                }

                if (failed > 0)
                {
                    stats.Failed++;
                    Ui.InlineError($"  ({failed} 个文件失败)");
                }

                Console.WriteLine();
            }
            catch (Exception ex)
            {
                Ui.InlineError($"✗ {ex.Message}");
                Console.WriteLine();
                stats.Failed++;
            }
        }

        return stats;
    }

    /// <summary>
    /// 皮肤目标文件夹。stable 的惯例是 <c>{名称} ({作者})</c>。
    /// 依次尝试几个候选名，命中已存在的文件夹就复用它，避免重复导入。
    /// </summary>
    private static string ResolveDestDir(SyncContext ctx, SkinInfo skin)
    {
        string name = PathHelper.SanitizeName(skin.Name);
        string creator = PathHelper.SanitizeName(skin.Creator);

        int maxNameLen = Math.Clamp(PathHelper.MaxPath - ctx.SkinsPath.Length - 1 - FilenameReserve, 20, 200);

        var candidates = new List<string>();

        // 1) "名称 (作者)" —— stable 导入 .osk 时的惯例
        //    名称里已经带了作者的就不要再叠一次
        if (!string.IsNullOrWhiteSpace(creator)
            && !name.EndsWith($" ({creator})", StringComparison.OrdinalIgnoreCase))
            candidates.Add(Clamp($"{name} ({creator})", maxNameLen));

        // 2) 只有名称
        candidates.Add(Clamp(name, maxNameLen));

        // 3) lazer 从 stable 导入时追加的 [原文件夹名]（见 SkinImporter.checkSkinIniMetadata）。
        //    用它可以直接对回 stable 里已有的文件夹，否则 lazer 改过名的皮肤会被重复导入一遍。
        string? archiveName = ExtractArchiveName(name);
        if (archiveName != null)
            candidates.Add(Clamp(PathHelper.SanitizeName(archiveName), maxNameLen));

        foreach (string candidate in candidates)
        {
            string dir = Path.Combine(ctx.SkinsPath, candidate);
            if (Directory.Exists(dir))
                return dir;
        }

        return Path.Combine(ctx.SkinsPath, candidates[0]);
    }

    /// <summary>
    /// 取出 lazer 追加在名字末尾的 <c>[原文件夹名]</c>。
    /// 没有这段后缀（例如皮肤本来就是从 .osk 导进来的）时返回 null。
    /// </summary>
    private static string? ExtractArchiveName(string skinName)
    {
        if (!skinName.EndsWith(']'))
            return null;

        int close = skinName.Length - 1;
        int open = skinName.LastIndexOf('[', close);

        if (open < 0 || open + 1 >= close)
            return null;

        return skinName[(open + 1)..close];
    }

    private static string Clamp(string name, int maxLength)
        => name.Length <= maxLength ? name : name[..maxLength].TrimEnd().TrimEnd('.');

    private static (long linked, long copied, long relinked, long differed, long failed) Sync(SyncContext ctx, SkinInfo skin, string destDir)
    {
        long linked = 0, copied = 0, failed = 0;
        Directory.CreateDirectory(destDir);

        foreach (var namedFile in skin.Files)
        {
            if (namedFile.File is null) continue;

            string sourcePath = LazerFileStore.ResolvePath(ctx.LazerFilesPath, namedFile.File.Hash);
            if (!File.Exists(sourcePath))
            {
                failed++;
                continue;
            }

            try
            {
                string destPath = ResolveDestPath(destDir, namedFile.Filename);
                Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);

                switch (FileSync.LinkOne(sourcePath, destPath, relink: false))
                {
                    case FileSync.Outcome.Linked: linked++; break;
                    case FileSync.Outcome.Copied: copied++; break;
                }
            }
            catch
            {
                failed++;
            }
        }

        return (linked, copied, 0, 0, failed);
    }

    private static (long linked, long copied, long relinked, long differed, long failed) Relink(SyncContext ctx, SkinInfo skin, string destDir)
    {
        long relinked = 0, differed = 0, failed = 0;

        foreach (var namedFile in skin.Files)
        {
            if (namedFile.File is null) continue;

            string sourcePath = LazerFileStore.ResolvePath(ctx.LazerFilesPath, namedFile.File.Hash);
            if (!File.Exists(sourcePath)) continue;

            string destPath = ResolveDestPath(destDir, namedFile.Filename);
            if (!File.Exists(destPath)) continue;

            try
            {
                switch (FileSync.LinkOne(sourcePath, destPath, relink: true))
                {
                    case FileSync.Outcome.Relinked: relinked++; break;
                    case FileSync.Outcome.ContentDiffers: differed++; break;
                }
            }
            catch
            {
                failed++;
            }
        }

        return (0, 0, relinked, differed, failed);
    }

    private static string ResolveDestPath(string destDir, string relativeFilename)
        => PathHelper.TruncateForStable(
            Path.Combine(destDir, PathHelper.SanitizeFilePath(relativeFilename)));
}
