using System.Globalization;

namespace OsuAssetLinker;

/// <summary>
/// 定位 osu!lazer 的数据目录与 Realm 数据库。
///
/// lazer 支持把数据目录改到别处：自定义路径记录在默认目录的 <c>storage.ini</c>
/// （键 <c>StorageConfig.FullPath</c>）中。此时 <c>client.realm</c>、<c>files/</c> 等
/// 都在自定义目录下，而默认目录只留下 <c>storage.ini</c>、<c>framework.ini</c>、
/// <c>client.realm.lock</c>、<c>client.realm.management</c> 这些迁移时被故意跳过的文件。
/// </summary>
public static class LazerData
{
    private const string StorageIni = "storage.ini";
    private const string FullPathKey = "FullPath";

    /// <summary>lazer 默认数据目录（Windows: <c>%APPDATA%\osu</c>）。</summary>
    public static string DefaultDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "osu");

    /// <summary>读取 storage.ini 里配置的自定义数据目录；未配置或读取失败时返回 null。</summary>
    public static string? ReadCustomDataPath(string dataDir)
    {
        string iniPath = Path.Combine(dataDir, StorageIni);
        if (!File.Exists(iniPath))
            return null;

        try
        {
            foreach (string raw in File.ReadAllLines(iniPath))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] is '[' or ';' or '#')
                    continue;

                int equals = line.IndexOf('=');
                if (equals < 0 || !line[..equals].Trim().Equals(FullPathKey, StringComparison.OrdinalIgnoreCase))
                    continue;

                string value = line[(equals + 1)..].Trim();
                if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
                    value = value[1..^1];

                return string.IsNullOrWhiteSpace(value) ? null : value;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // storage.ini 损坏或不可读时，按「没有自定义目录」处理
        }

        return null;
    }

    /// <summary>
    /// 如果 <paramref name="candidate"/> 的 storage.ini 指向一个包含数据库的自定义目录，
    /// 返回该目录并把 <paramref name="redirected"/> 置为 true；否则原样返回 candidate。
    /// </summary>
    public static string ResolveDataDirectory(string candidate, out bool redirected)
    {
        redirected = false;

        if (!Directory.Exists(candidate))
            return candidate;

        string? custom = ReadCustomDataPath(candidate);
        if (string.IsNullOrEmpty(custom))
            return candidate;

        string resolved;
        try
        {
            resolved = Path.IsPathRooted(custom)
                ? Path.GetFullPath(custom)
                : Path.GetFullPath(Path.Combine(candidate, custom));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException or UnauthorizedAccessException)
        {
            // storage.ini 里的路径不合法时，保持使用传入的目录
            return candidate;
        }

        bool sameDirectory = string.Equals(
            Path.TrimEndingDirectorySeparator(resolved),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate)),
            StringComparison.OrdinalIgnoreCase);

        // lazer 只要配置了自定义目录就会优先使用它，这里也优先跟随 storage.ini；
        // 但只有真的找到数据库时才切换，避免把用户带到错误的目录。
        if (sameDirectory || FindRealmFile(resolved) == null)
            return candidate;

        redirected = true;
        return resolved;
    }

    /// <summary>
    /// 查找 lazer 的主数据库 <c>client.realm</c>。
    /// 开发版（DEBUG 构建）使用 <c>client_&lt;schemaVersion&gt;.realm</c>，一并支持；
    /// <c>client_&lt;时间戳&gt;_corrupt.realm</c> 等恢复备份会被排除。
    /// </summary>
    public static string? FindRealmFile(string dataDir)
    {
        string direct = Path.Combine(dataDir, "client.realm");
        if (File.Exists(direct))
            return direct;

        if (!Directory.Exists(dataDir))
            return null;

        try
        {
            return Directory.GetFiles(dataDir, "client*.realm")
                .Select(path => (path, version: ParseVersionedRealmName(Path.GetFileName(path))))
                .Where(candidate => candidate.version >= 0)
                .OrderByDescending(candidate => candidate.version)
                .Select(candidate => candidate.path)
                .FirstOrDefault();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>目录里有 lazer 的痕迹（即使数据库本体已经不在）。</summary>
    public static bool LooksLikeLazerDataDirectory(string dataDir)
        => File.Exists(Path.Combine(dataDir, StorageIni))
           || File.Exists(Path.Combine(dataDir, "framework.ini"))
           || HasLeftoverRealmArtifacts(dataDir);

    /// <summary>
    /// 目录里只有 Realm 的锁/管理文件、没有数据库本体。
    /// 这通常意味着 lazer 使用了别的数据目录（迁移时 .lock/.management 会被故意留下）。
    /// </summary>
    public static bool HasLeftoverRealmArtifacts(string dataDir)
        => File.Exists(Path.Combine(dataDir, "client.realm.lock"))
           || File.Exists(Path.Combine(dataDir, "client.realm.note"))
           || Directory.Exists(Path.Combine(dataDir, "client.realm.management"));

    /// <summary>找不到 client.realm 时给出更具体的原因；没有额外信息时返回 null。</summary>
    public static string? DescribeMissingRealm(string dataDir)
    {
        string? custom = ReadCustomDataPath(dataDir);
        if (!string.IsNullOrEmpty(custom))
        {
            return "  storage.ini 里配置了自定义数据目录：" + custom + "\n" +
                   "  请确认该目录存在且包含 client.realm，或直接把该目录作为 lazer 目录传入。";
        }

        if (HasLeftoverRealmArtifacts(dataDir))
        {
            return "  该目录里只有 client.realm.lock / client.realm.management，没有数据库本体 client.realm。\n" +
                   "  常见原因：lazer 数据目录被改到了别处（检查 storage.ini 或 lazer 设置 → 维护 → 更改数据文件夹），\n" +
                   "  或者 client.realm 已被删除 / 被安全软件隔离。";
        }

        return null;
    }

    /// <summary>解析 <c>client_&lt;数字&gt;.realm</c> 的版本号；其他文件名返回 -1。</summary>
    private static int ParseVersionedRealmName(string fileName)
    {
        const string prefix = "client_";
        const string suffix = ".realm";

        if (!fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
            !fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        {
            return -1;
        }

        string core = fileName[prefix.Length..^suffix.Length];
        return int.TryParse(core, NumberStyles.None, CultureInfo.InvariantCulture, out int version)
            ? version
            : -1;
    }
}
