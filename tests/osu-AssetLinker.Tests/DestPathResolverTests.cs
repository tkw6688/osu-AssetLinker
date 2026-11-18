namespace OsuAssetLinker.Tests;

public class DestPathResolverTests
{
    private const string DestDir = @"C:\osu!\Songs\12345 Artist - Title";

    [Fact]
    public void SameFiles_ProduceSameMapping_AcrossResolvers()
    {
        string first = new string('a', 230) + "-1.png";
        string second = new string('a', 230) + "-2.png";

        string Map(DestPathResolver resolver)
            => resolver.Resolve(DestDir, first) + "|" + resolver.Resolve(DestDir, second);

        Assert.Equal(Map(new DestPathResolver()), Map(new DestPathResolver()));
    }

    [Fact]
    public void LongNames_AreDistinct_AndWithinLimit()
    {
        string first = new string('a', 230) + "-1.png";
        string second = new string('a', 230) + "-2.png";

        var resolver = new DestPathResolver();
        string a = resolver.Resolve(DestDir, first);
        string b = resolver.Resolve(DestDir, second);

        Assert.NotEqual(a, b);
        Assert.True(a.Length <= PathLength.MaxPath);
        Assert.True(b.Length <= PathLength.MaxPath);
        Assert.EndsWith(".png", a);
        Assert.EndsWith(".png", b);
    }

    [Fact]
    public void CaseInsensitiveCollision_GetsSuffix()
    {
        var resolver = new DestPathResolver();

        string first = resolver.Resolve(DestDir, "audio.mp3");
        string second = resolver.Resolve(DestDir, "AUDIO.mp3");

        Assert.NotEqual(first, second);
    }
}
