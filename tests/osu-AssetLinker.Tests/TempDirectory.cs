namespace OsuAssetLinker.Tests;

/// <summary>测试用临时目录，Dispose 时递归删除。</summary>
public sealed class TempDirectory : IDisposable
{
    public string Root { get; }

    public TempDirectory()
    {
        Root = Path.Combine(Path.GetTempPath(), "osu-al-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    public string Combine(params string[] parts)
        => Path.Combine(new[] { Root }.Concat(parts).ToArray());

    public string CreateDirectory(params string[] parts)
    {
        string path = Combine(parts);
        Directory.CreateDirectory(path);
        return path;
    }

    public string WriteFile(string relativePath, string content)
    {
        string path = Combine(relativePath.Split('/'));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, true);
        }
        catch
        {
            // 清理失败不影响测试结果
        }
    }
}
