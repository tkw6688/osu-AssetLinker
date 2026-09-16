using System.Security.Cryptography;

namespace OsuAssetLinker;

/// <summary>把 lazer 文件仓库里的单个文件同步到 stable 目录。</summary>
public static class FileSync
{
    public enum Outcome
    {
        /// <summary>新建了硬链接。</summary>
        Linked,

        /// <summary>硬链接不可用，退化为复制。</summary>
        Copied,

        /// <summary>目标已存在且无需处理。</summary>
        Skipped,

        /// <summary>--relink：把普通文件替换成了硬链接。</summary>
        Relinked,

        /// <summary>--relink：两边内容不同，为避免覆盖已跳过。</summary>
        ContentDiffers,
    }

    /// <summary>
    /// 建立 <paramref name="dest"/> → <paramref name="src"/> 的硬链接。
    /// 已存在时默认跳过；<paramref name="relink"/> 为 true 时把普通文件替换为硬链接。
    /// </summary>
    public static Outcome LinkOne(string src, string dest, bool relink)
    {
        if (!File.Exists(dest))
            return HardLink.TryCreate(dest, src) ? Outcome.Linked : Copy(src, dest);

        if (!relink)
            return Outcome.Skipped;

        // 链接数 > 1 说明已经是硬链接，无需处理
        if (HardLink.GetLinkCount(dest) > 1)
            return Outcome.Skipped;

        // 内容不同时绝不覆盖：stable 里那份可能是用户改过的（例如手改的 skin.ini），
        // 覆盖会造成数据丢失，这里只报告不动手。
        if (!ContentEquals(src, dest))
            return Outcome.ContentDiffers;

        // 替换：先删除再建链接，失败则回退复制，避免目标文件凭空消失
        File.Delete(dest);
        return HardLink.TryCreate(dest, src) ? Outcome.Relinked : Copy(src, dest);
    }

    /// <summary>比较两个文件内容是否一致（先比长度，再比 SHA-256）。</summary>
    public static bool ContentEquals(string pathA, string pathB)
    {
        if (new FileInfo(pathA).Length != new FileInfo(pathB).Length)
            return false;

        using var a = File.OpenRead(pathA);
        using var b = File.OpenRead(pathB);
        return SHA256.HashData(a).SequenceEqual(SHA256.HashData(b));
    }

    public static void Record(Outcome outcome, SyncStats stats)
    {
        switch (outcome)
        {
            case Outcome.Linked:
                stats.FilesLinked++;
                break;
            case Outcome.Copied:
                stats.FilesCopied++;
                break;
            case Outcome.Relinked:
                stats.FilesRelinked++;
                break;
            case Outcome.ContentDiffers:
                stats.FilesContentDiffer++;
                break;
        }
    }

    private static Outcome Copy(string src, string dest)
    {
        File.Copy(src, dest);
        return Outcome.Copied;
    }
}
