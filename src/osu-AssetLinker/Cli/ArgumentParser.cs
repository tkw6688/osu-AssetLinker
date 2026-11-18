namespace OsuAssetLinker;

/// <summary>命令行参数解析。</summary>
public static class ArgumentParser
{
    public static CliParseResult Parse(string[] args)
    {
        string? lazerDir = null;
        string? stableDir = null;
        bool relink = false;
        bool dryRun = false;
        var features = new HashSet<SyncFeature>();

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            string? inlineValue = null;

            int equals = arg.IndexOf('=');
            if (equals > 0 && arg.StartsWith("--", StringComparison.Ordinal))
            {
                inlineValue = arg[(equals + 1)..];
                arg = arg[..equals];
            }

            switch (arg)
            {
                case "--lazer-dir":
                case "--stable-dir":
                {
                    string? value = inlineValue;
                    if (string.IsNullOrEmpty(value))
                    {
                        if (i + 1 >= args.Length || args[i + 1].StartsWith('-'))
                            return new(null, $"参数 {arg} 缺少目录值。");
                        value = args[++i];
                    }

                    if (string.IsNullOrWhiteSpace(value))
                        return new(null, $"参数 {arg} 缺少目录值。");

                    if (arg == "--lazer-dir")
                    {
                        if (lazerDir != null)
                            return new(null, "参数 --lazer-dir 重复指定。");
                        lazerDir = value;
                    }
                    else
                    {
                        if (stableDir != null)
                            return new(null, "参数 --stable-dir 重复指定。");
                        stableDir = value;
                    }

                    break;
                }

                case "--relink":
                    relink = true;
                    break;

                case "--dry-run":
                    dryRun = true;
                    break;

                case "--beats":
                    features.Add(SyncFeature.Beatmaps);
                    break;

                case "--skins":
                    features.Add(SyncFeature.Skins);
                    break;

                case "--all":
                    features.Add(SyncFeature.Beatmaps);
                    features.Add(SyncFeature.Skins);
                    break;

                default:
                    if (arg.StartsWith('-'))
                        return new(null, $"未知参数: {arg}\n提示: 运行 --help 查看用法。");

                    return new(null,
                        $"不支持位置参数: {arg}\n" +
                        "提示: 旧写法 [lazer目录] [Songs目录] 已改为命名参数，例如:\n" +
                        "  osu-AssetLinker --lazer-dir \"C:\\Users\\你\\AppData\\Roaming\\osu\" --stable-dir \"C:\\osu!\"");
            }
        }

        var ordered = new List<SyncFeature>();
        if (features.Contains(SyncFeature.Beatmaps)) ordered.Add(SyncFeature.Beatmaps);
        if (features.Contains(SyncFeature.Skins)) ordered.Add(SyncFeature.Skins);
        if (ordered.Count == 0) ordered.Add(SyncFeature.Beatmaps);

        return new(new CliOptions(lazerDir, stableDir, relink, dryRun, ordered), null);
    }
}
