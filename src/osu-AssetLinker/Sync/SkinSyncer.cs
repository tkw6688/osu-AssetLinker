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
        bool canLink = HardLink.LikelySameVolume(ctx.LazerFilesPath, skinsPath);

        if (!Directory.Exists(skinsPath))
        {
            if (ctx.DryRun)
            {
                Ui.Warn($"[预览] 将创建 Skins 目录: {skinsPath}");
            }
            else
            {
                Ui.Warn($"创建 Skins 目录: {skinsPath}");
                Directory.CreateDirectory(skinsPath);
            }
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
                ? $"  [{index}/{stats.Total}] [relink] {Ui.Ellipsis(folderName, 50)}... "
                : $"  [{index}/{stats.Total}] {Ui.Ellipsis(folderName, 55)}... ";

            Ui.Inline(prefix);

            try
            {
                var result = FileJobExecutor.Execute(
                    FileJobExecutor.FromFiles(skin.Files, ctx.LazerFilesPath, destDir),
                    alreadyExists ? FileJobMode.Relink : FileJobMode.Sync,
                    ctx.DryRun, canLink);
                stats.Add(result);

                if (alreadyExists)
                {
                    if (result.Relinked > 0)
                    {
                        Ui.InlineAccent(ctx.DryRun
                            ? $"✓ 将释放 {result.Relinked} 个文件"
                            : $"✓ 释放 {result.Relinked} 个文件");
                        stats.Relinked++;
                    }
                    else
                    {
                        Ui.InlineDim("已是硬链接，跳过");
                        stats.Skipped++;
                    }

                    if (result.ContentDiffered > 0)
                        Ui.InlineError($"  ⚠ {result.ContentDiffered} 个文件内容不同，未覆盖");
                }
                else
                {
                    Ui.InlineOk(ctx.DryRun
                        ? $"✓ 将同步 {result.Linked + result.Copied + result.Failed} 个文件"
                        : $"✓ {result.Linked + result.Copied + result.Failed} 个文件");
                    stats.Added++;
                }

                if (result.Failed > 0)
                {
                    stats.Failed++;
                    Ui.InlineError($"  ({result.Failed} 个文件失败)");
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
        string name = PathSanitizer.SanitizeName(skin.Name);
        string creator = PathSanitizer.SanitizeName(skin.Creator);

        int maxNameLen = Math.Clamp(PathLength.MaxPath - ctx.SkinsPath.Length - 1 - FilenameReserve, 20, 200);

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
            candidates.Add(Clamp(PathSanitizer.SanitizeName(archiveName), maxNameLen));

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
}
