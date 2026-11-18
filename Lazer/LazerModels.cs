using Realms;

namespace OsuAssetLinker;

// ============================================================
//  osu!lazer Realm 模型（只声明本工具需要的字段）
//
//  ⚠ Realm 要求：声明的属性必须是数据库里真实存在、且类型一致的字段，
//    否则打开数据库时会直接报错。这里只做「子集」声明是安全的，
//    但如果 lazer 删改了我们用到的字段，就需要同步更新本文件。
// ============================================================

[MapTo("BeatmapSet")]
public partial class BeatmapSetInfo : RealmObject
{
    [PrimaryKey] public Guid ID { get; set; } = Guid.NewGuid();
    public int OnlineID { get; set; } = -1;
    public bool DeletePending { get; set; }
    public IList<BeatmapInfo> Beatmaps { get; } = null!;
    public IList<RealmNamedFileUsage> Files { get; } = null!;
}

[MapTo("Beatmap")]
public partial class BeatmapInfo : RealmObject
{
    [PrimaryKey] public Guid ID { get; set; } = Guid.NewGuid();
    public BeatmapMetadata? Metadata { get; set; }
    public BeatmapSetInfo? BeatmapSet { get; set; }
    public RulesetInfo? Ruleset { get; set; }
    public string DifficultyName { get; set; } = string.Empty;
    public string Hash { get; set; } = string.Empty;
    public string MD5Hash { get; set; } = string.Empty;
    public int OnlineID { get; set; } = -1;
}

[MapTo("BeatmapMetadata")]
public partial class BeatmapMetadata : RealmObject
{
    public string Title { get; set; } = string.Empty;
    public string TitleUnicode { get; set; } = string.Empty;
    public string Artist { get; set; } = string.Empty;
    public string ArtistUnicode { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Tags { get; set; } = string.Empty;
    public string AudioFile { get; set; } = string.Empty;
    public string BackgroundFile { get; set; } = string.Empty;
    public RealmUser? Author { get; set; }
}

[MapTo("Ruleset")]
public partial class RulesetInfo : RealmObject
{
    [PrimaryKey] public string ShortName { get; set; } = string.Empty;
    public int OnlineID { get; set; } = -1;
    public string Name { get; set; } = string.Empty;
}

[MapTo("Skin")]
public partial class SkinInfo : RealmObject
{
    [PrimaryKey] public Guid ID { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Creator { get; set; } = string.Empty;
    public string InstantiationInfo { get; set; } = string.Empty;
    public string Hash { get; set; } = string.Empty;

    /// <summary>内置皮肤（triangles / argon / classic / retro 等）为 true，不应导出。</summary>
    public bool Protected { get; set; }

    public bool DeletePending { get; set; }
    public IList<RealmNamedFileUsage> Files { get; } = null!;
}

[MapTo("Score")]
public partial class ScoreInfo : RealmObject
{
    [PrimaryKey] public Guid ID { get; set; } = Guid.NewGuid();
    public BeatmapInfo? BeatmapInfo { get; set; }
    public string BeatmapHash { get; set; } = string.Empty;
    public RulesetInfo? Ruleset { get; set; }
    public RealmUser? User { get; set; }
    public DateTimeOffset Date { get; set; }
    public string Hash { get; set; } = string.Empty;
    public bool DeletePending { get; set; }
    public IList<RealmNamedFileUsage> Files { get; } = null!;
}

/// <summary>无 [MapTo]，Realm 直接使用类名 "RealmUser"。</summary>
public partial class RealmUser : EmbeddedObject
{
    public int OnlineID { get; set; } = 1;
    public string Username { get; set; } = string.Empty;
    [MapTo("CountryCode")] public string CountryString { get; set; } = string.Empty;
}

/// <summary>无 [MapTo]，Realm 直接使用类名 "BeatmapCollection"。</summary>
public partial class BeatmapCollection : RealmObject
{
    [PrimaryKey] public Guid ID { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public IList<string> BeatmapMD5Hashes { get; } = null!;
    public DateTimeOffset LastModified { get; set; }
}

[MapTo("File")]
public partial class RealmFile : RealmObject
{
    [PrimaryKey] public string Hash { get; set; } = string.Empty;
}

/// <summary>无 [MapTo]，Realm 直接使用类名 "RealmNamedFileUsage"。</summary>
public partial class RealmNamedFileUsage : EmbeddedObject
{
    public string Filename { get; set; } = string.Empty;
    public RealmFile? File { get; set; }
}
