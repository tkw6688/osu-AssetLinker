namespace OsuAssetLinker.Tests;

public class PathLengthTests
{
    [Fact]
    public void ShortPath_IsReturnedUnchanged()
    {
        string path = @"C:\osu!\Songs\123 Artist - Title\audio.mp3";

        Assert.Equal(path, PathLength.TruncateForStable(path));
    }

    [Fact]
    public void LongPath_IsTruncatedWithinLimit_AndKeepsExtension()
    {
        string dir = @"C:\" + new string('d', 200);
        string path = Path.Combine(dir, new string('n', 100) + ".mp3");

        string result = PathLength.TruncateForStable(path);

        Assert.True(result.Length <= PathLength.MaxPath);
        Assert.EndsWith(".mp3", result);
        Assert.StartsWith(dir, result);
    }

    [Fact]
    public void ClaimedPath_GetsNumericSuffix()
    {
        string path = @"C:\osu!\Songs\123\audio.mp3";
        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { path };

        string result = PathLength.TruncateForStable(path, PathLength.MaxPath, claimed);

        Assert.NotEqual(path, result);
        Assert.EndsWith("audio (2).mp3", result);
    }

    [Fact]
    public void TooLongDirectory_Throws()
    {
        string path = Path.Combine(@"C:\" + new string('d', 260), "a.mp3");

        Assert.Throws<PathTooLongException>(() => PathLength.TruncateForStable(path));
    }
}
