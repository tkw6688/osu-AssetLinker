using System.Text.RegularExpressions;
using Realms;
using Realms.Exceptions;

namespace OsuAssetLinker;

/// <summary>
/// 以只读方式打开 osu!lazer 的 Realm 数据库。
///
/// SchemaVersion 必须与数据库文件当前的版本<strong>完全一致</strong>，否则 Realm 会直接抛异常。
/// 而 lazer 每次升级都可能改变这个版本号（<c>osu.Game/Database/RealmAccess.cs</c> 里的
/// <c>schema_version</c>），所以这里不写死，而是从报错信息里读出真实版本再重试。
///
/// 已确认：版本不匹配时 Realm <strong>只会报错，不会迁移或写入</strong>，因此探测是安全的。
/// </summary>
public static class LazerRealm
{
    /// <summary>编写本工具时 lazer 的 schema 版本，仅作为首次尝试值。</summary>
    private const int KnownSchemaVersion = 52;

    private static readonly Type[] Schema =
    [
        typeof(BeatmapSetInfo),
        typeof(BeatmapInfo),
        typeof(BeatmapMetadata),
        typeof(RulesetInfo),
        typeof(SkinInfo),
        typeof(ScoreInfo),
        typeof(RealmUser),
        typeof(BeatmapCollection),
        typeof(RealmFile),
        typeof(RealmNamedFileUsage),
    ];

    /// <summary>匹配 Realm Core 的版本不匹配报错，从中取出数据库的真实版本号。</summary>
    private static readonly Regex VersionMismatch =
        new(@"does not equal last set version (\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static Realm Open(string realmFile)
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "lazer-sync");
        Directory.CreateDirectory(tempDir);

        int version = KnownSchemaVersion;
        RealmException? lastError = null;

        // 最多重试几次：第一次用已知版本，之后用报错里读出的真实版本。
        for (int attempt = 0; attempt < 4; attempt++)
        {
            var config = new RealmConfiguration(realmFile)
            {
                SchemaVersion = (ulong)version,
                IsReadOnly = true,
                FallbackPipePath = tempDir,
                Schema = Schema,
            };

            try
            {
                return Realm.GetInstance(config);
            }
            catch (RealmException ex)
            {
                lastError = ex;

                var match = VersionMismatch.Match(ex.Message);
                if (!match.Success)
                    throw BuildSchemaError(ex);

                int actual = int.Parse(match.Groups[1].Value);
                if (actual == version)
                    throw BuildSchemaError(ex);

                version = actual;
            }
        }

        throw BuildSchemaError(lastError!);
    }

    /// <summary>
    /// 版本不匹配已经被自动处理；走到这里的都是「本工具的模型与 lazer 对不上」，
    /// 需要人工更新 <see cref="LazerModels"/>。
    /// </summary>
    private static Exception BuildSchemaError(RealmException ex) => new InvalidOperationException(
        "无法读取 lazer 数据库：本工具的 Realm 模型与当前 lazer 版本不兼容。\n" +
        "这通常意味着 lazer 更新后改动/删除了本工具用到的字段，需要更新工具。\n\n" +
        "原始错误：\n" + ex.Message,
        ex);
}
