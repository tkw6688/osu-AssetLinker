namespace OsuAssetLinker;

/// <summary>要同步的内容。</summary>
public enum SyncFeature
{
    Beatmaps,
    Skins,
}

/// <summary>解析后的命令行选项。</summary>
public sealed record CliOptions(string? LazerDir, string? StableDir, bool Relink, List<SyncFeature> Features);

/// <summary>参数解析结果：成功时 <see cref="Options"/> 非空，失败时 <see cref="Error"/> 说明原因。</summary>
public sealed record CliParseResult(CliOptions? Options, string? Error);
