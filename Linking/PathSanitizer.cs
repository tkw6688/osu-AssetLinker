using System.Text;

namespace OsuAssetLinker;

/// <summary>文件名/路径分量清洗：替换 Windows 非法字符、去掉结尾点号与空格。</summary>
public static class PathSanitizer
{
    public static string SanitizeName(string name)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder();
        foreach (char c in name)
            sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
        // Windows 不允许文件名/目录名以 '.' 或 ' ' 结尾
        return sb.ToString().Trim().TrimEnd('.');
    }

    /// <summary>
    /// 对铺面内部文件路径的每个分量分别做非法字符替换 + 结尾点号清理。
    /// </summary>
    public static string SanitizeFilePath(string relativePath)
    {
        var parts = relativePath.Split('/');
        char[] invalid = Path.GetInvalidFileNameChars();
        for (int i = 0; i < parts.Length; i++)
        {
            var sb = new StringBuilder();
            foreach (char c in parts[i])
                sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
            parts[i] = sb.ToString().Trim().TrimEnd('.');
            if (string.IsNullOrEmpty(parts[i])) parts[i] = "_";
        }
        return string.Join(Path.DirectorySeparatorChar, parts);
    }
}
