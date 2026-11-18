using System.Reflection;

namespace OsuAssetLinker;

/// <summary>程序名称、版本与构建信息（用于 --version）。</summary>
public static class AppInfo
{
    public const string Name = "osu-AssetLinker";

    public static string Version => SplitVersion().Version;

    /// <summary>构建时嵌入的提交哈希；本地或无 SourceLink 的构建为 null。</summary>
    public static string? Commit => SplitVersion().Commit;

    /// <summary><c>--version</c> 的完整输出。</summary>
    public static string Display
        => Commit is { Length: > 0 } commit
            ? $"{Name} {Version} (commit {commit[..Math.Min(8, commit.Length)]})"
            : $"{Name} {Version}";

    private static (string Version, string? Commit) SplitVersion()
    {
        string? informational = typeof(AppInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (string.IsNullOrEmpty(informational))
            return (typeof(AppInfo).Assembly.GetName().Version?.ToString() ?? "unknown", null);

        int plus = informational.IndexOf('+');
        if (plus < 0)
            return (informational, null);

        string version = informational[..plus];
        string commit = informational[(plus + 1)..];
        return (version, commit.Length > 0 ? commit : null);
    }
}
