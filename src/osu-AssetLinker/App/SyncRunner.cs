using Realms;

namespace OsuAssetLinker;

/// <summary>一次同步任务的主流程：解析参数/向导 → 校验路径 → 打开数据库 → 执行 → 汇总。</summary>
public static class SyncRunner
{
    public static int Run(string[] args)
    {
        bool interactiveMode = ConsoleSession.IsInteractive && args.Length == 0;

        string lazerDataPath;
        StablePaths stablePaths;
        bool relink;
        bool dryRun;
        List<SyncFeature> features;

        if (interactiveMode)
        {
            var result = Wizard.Run();
            if (result == null)
            {
                Console.WriteLine();
                Ui.Warn("已取消。");
                return 0;
            }

            lazerDataPath = result.LazerPath;
            stablePaths = result.Stable;
            relink = result.Relink;
            dryRun = result.PreviewFirst;
            features = result.Features;
        }
        else
        {
            var parsed = ArgumentParser.Parse(args);
            if (parsed.Error != null)
            {
                Ui.Error($"错误: {parsed.Error}");
                return 1;
            }

            var options = parsed.Options!;
            features = options.Features;
            relink = options.Relink;
            dryRun = options.DryRun;

            if (options.LazerDir != null)
            {
                lazerDataPath = PathResolution.ResolveLazerDataPath(options.LazerDir);
            }
            else
            {
                string? detected = PathResolution.DetectLazerDataPath();
                if (detected == null)
                {
                    Ui.Error("错误: 无法自动检测 osu!lazer 数据目录。");
                    Ui.Dim("  请用 --lazer-dir \"...\" 指定。");
                    return 1;
                }

                lazerDataPath = detected;
                Ui.Info($"[自动检测] Lazer 数据目录: {lazerDataPath}");
            }

            if (!Directory.Exists(lazerDataPath))
            {
                Ui.Error($"错误: lazer 数据目录不存在: {lazerDataPath}");
                return 1;
            }

            string stableRoot;
            if (options.StableDir != null)
            {
                var info = StableData.NormalizeRoot(options.StableDir);
                if (info.Note != null)
                    Ui.Warn($"⚠ {info.Note}");
                stableRoot = info.Root;
            }
            else
            {
                var roots = StableData.FindRoots();
                if (roots.Count == 0)
                {
                    Ui.Error("错误: 无法自动检测 osu!stable 根目录。");
                    Ui.Dim("  已尝试: " + string.Join("、", StableData.CandidateRoots()));
                    Ui.Dim("  请用 --stable-dir \"C:\\osu!\" 指定。");
                    return 1;
                }

                stableRoot = roots[0];
                Ui.Info($"[自动检测] Stable 根目录: {stableRoot}");
                if (roots.Count > 1)
                    Ui.Dim("  检测到多个 stable 目录，当前使用第一个；可用 --stable-dir 指定其他位置。");
            }

            if (!Directory.Exists(stableRoot))
            {
                Ui.Error($"错误: stable 根目录不存在: {stableRoot}");
                return 1;
            }

            if (!StableData.LooksLikeStableRoot(stableRoot))
                Ui.Warn($"⚠ '{stableRoot}' 中没有发现 osu!stable 的典型文件/目录，仍将尝试继续。");

            var built = PathResolution.BuildStablePathsForCli(stableRoot, features);
            if (built == null)
                return 1;

            stablePaths = built;
        }

        if (relink && !interactiveMode)
            Ui.Accent("[模式] --relink：将对已有文件也替换为硬链接以释放磁盘空间");

        if (dryRun)
            Ui.Accent("[模式] 预览（--dry-run）：只报告将要执行的操作，不会写入任何文件");

        Console.WriteLine();

        // --- 2. 验证 lazer 路径 ---
        string? realmFile = LazerData.FindRealmFile(lazerDataPath);
        if (realmFile == null)
        {
            Ui.Error($"错误: 在 '{lazerDataPath}' 中找不到 client.realm 文件");
            if (LazerData.DescribeMissingRealm(lazerDataPath) is { } hint)
                Ui.Dim(hint);
            return 1;
        }

        string lazerFilesPath = Path.Combine(lazerDataPath, "files");
        if (!Directory.Exists(lazerFilesPath))
        {
            Ui.Error($"错误: Lazer files 目录不存在: {lazerFilesPath}");
            return 1;
        }

        // --- 3. 检测硬链接可用性（按实际目标分别判断）---
        var targets = new List<(string Label, string Path)>();
        if (features.Contains(SyncFeature.Beatmaps) && stablePaths.SongsPath != null)
            targets.Add(("铺面", stablePaths.SongsPath));
        if (features.Contains(SyncFeature.Skins))
            targets.Add(("皮肤", stablePaths.SkinsPath));

        var differentVolumes = new List<(string Label, string Path)>();
        bool allKnownSameVolume = targets.Count > 0;

        foreach (var target in targets)
        {
            bool? sameVolume = HardLink.SameVolume(
                HardLink.NearestExistingDirectory(lazerFilesPath),
                HardLink.NearestExistingDirectory(target.Path));

            if (sameVolume is false)
                differentVolumes.Add(target);
            else if (sameVolume is null)
                allKnownSameVolume = false;
        }

        foreach (var target in differentVolumes)
            Ui.Warn($"⚠ {target.Label}目标与 lazer 不在同一分区，将复制文件: {target.Path}");

        if (differentVolumes.Count > 0 && relink)
        {
            if (interactiveMode && ConsoleSession.Ask("是否关闭 --relink 并继续？", true))
            {
                relink = false;
                Ui.Warn("已关闭 --relink，将改用复制。");
            }
            else
            {
                Ui.Error("错误: --relink 需要硬链接支持，以下目标与 lazer 不在同一分区:");
                foreach (var target in differentVolumes)
                    Ui.Error($"  · {target.Label}: {target.Path}");
                Ui.Dim("  请移除 --relink，或将 stable 目录移到与 lazer 相同的分区。");
                return 1;
            }
        }

        if (targets.Count > 0 && differentVolumes.Count == 0)
        {
            if (allKnownSameVolume)
                Ui.Ok("✓ 硬链接可用（同一分区），文件将零额外磁盘占用");
            else
                Ui.Dim("无法判断卷信息，将按文件逐个回退（硬链接不可用时复制）");
        }

        Console.WriteLine();

        // --- 4. 打开 Realm 数据库（只读）---
        Console.WriteLine($"正在读取 Lazer 数据库: {realmFile}");

        Realm realm;
        try
        {
            realm = LazerRealm.Open(realmFile);
        }
        catch (Exception ex)
        {
            Ui.Error($"\n{ex.Message}");
            if (ex is not InvalidOperationException)
                Ui.Error("\n提示: 请先关闭 osu! lazer 再运行本工具");
            return 1;
        }

        // --- 5. 执行同步 ---
        try
        {
            using (realm)
            {
                List<SyncStats> RunAll(bool preview)
                {
                    var runResults = new List<SyncStats>();
                    var ctx = new SyncContext
                    {
                        Realm = realm,
                        LazerFilesPath = lazerFilesPath,
                        Stable = stablePaths,
                        Relink = relink,
                        DryRun = preview,
                    };

                    foreach (var feature in features)
                    {
                        runResults.Add(RunFeature(feature, ctx));
                        Console.WriteLine();
                    }

                    return runResults;
                }

                var results = RunAll(dryRun);

                // --- 6. 输出汇总 ---
                SummaryReporter.Print(results, dryRun);

                // 交互模式下预览后可以直接继续；CLI 模式保持「预览即退出」
                if (dryRun && interactiveMode && ConsoleSession.Ask("\n按以上预览执行吗？", true))
                {
                    Console.WriteLine();
                    results = RunAll(preview: false);
                    SummaryReporter.Print(results, dryRun: false);
                }

                return results.Any(r => r.HasFailures) ? 2 : 0;
            }
        }
        catch (Exception ex)
        {
            Ui.Error($"\n读取数据库失败: {ex.Message}");
            Ui.Error("\n提示: 请先关闭 osu! lazer 再运行本工具");
            return 1;
        }
    }

    private static SyncStats RunFeature(SyncFeature feature, SyncContext ctx)
    {
        Ui.Accent($"【{FeatureName(feature)}】");
        return feature switch
        {
            SyncFeature.Skins => SkinSyncer.Run(ctx),
            _ => BeatmapSyncer.Run(ctx),
        };
    }

    private static string FeatureName(SyncFeature feature) => feature switch
    {
        SyncFeature.Beatmaps => "铺面",
        SyncFeature.Skins => "皮肤",
        _ => feature.ToString(),
    };
}
