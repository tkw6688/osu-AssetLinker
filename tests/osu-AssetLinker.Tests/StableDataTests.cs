namespace OsuAssetLinker.Tests;

public class StableDataTests
{
    [Fact]
    public void ReadBeatmapDirectory_ParsesRelativeValue()
    {
        using var temp = new TempDirectory();
        string cfg = temp.WriteFile("osu!.other.cfg", "BeatmapDirectory = Songs\r\n");

        var result = StableData.ReadBeatmapDirectory(temp.Root, cfg);

        Assert.Equal("Songs", result.RawValue);
        Assert.Equal(Path.Combine(temp.Root, "Songs"), result.AbsolutePath);
        Assert.Null(result.Error);
    }

    [Fact]
    public void ReadBeatmapDirectory_ParsesQuotedAbsoluteValue()
    {
        using var temp = new TempDirectory();
        string songs = temp.CreateDirectory("My Songs");
        string cfg = temp.WriteFile("osu!.other.cfg", $"BeatmapDirectory = \"{songs}\"\r\n");

        var result = StableData.ReadBeatmapDirectory(temp.Root, cfg);

        Assert.Equal($"\"{songs}\"", result.RawValue);
        Assert.Equal(songs, result.AbsolutePath);
    }

    [Fact]
    public void ReadBeatmapDirectory_ExpandsEnvironmentVariables()
    {
        using var temp = new TempDirectory();
        string songs = temp.CreateDirectory("Songs");
        string cfg = temp.WriteFile("cfg.ini", "BeatmapDirectory = %OSU_AL_TEST_SONGS%\r\n");

        string? original = Environment.GetEnvironmentVariable("OSU_AL_TEST_SONGS");
        try
        {
            Environment.SetEnvironmentVariable("OSU_AL_TEST_SONGS", songs);
            var result = StableData.ReadBeatmapDirectory(temp.Root, cfg);

            Assert.Equal(songs, result.AbsolutePath);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OSU_AL_TEST_SONGS", original);
        }
    }

    [Fact]
    public void ReadBeatmapDirectory_AcceptsColonSeparator()
    {
        using var temp = new TempDirectory();
        string cfg = temp.WriteFile("cfg.ini", "BeatmapDirectory: Songs\r\n");

        var result = StableData.ReadBeatmapDirectory(temp.Root, cfg);

        Assert.Equal("Songs", result.RawValue);
    }

    [Fact]
    public void ReadBeatmapDirectory_MissingKey_ReturnsEmptyResult()
    {
        using var temp = new TempDirectory();
        string cfg = temp.WriteFile("cfg.ini", "# comment\r\nUsername = tester\r\n");

        var result = StableData.ReadBeatmapDirectory(temp.Root, cfg);

        Assert.Null(result.RawValue);
        Assert.Null(result.AbsolutePath);
        Assert.Null(result.Error);
    }

    [Fact]
    public void FindConfigFile_PrefersCurrentUserConfig()
    {
        using var temp = new TempDirectory();
        string userCfg = temp.WriteFile($"osu!.{Environment.UserName}.cfg", string.Empty);
        string otherCfg = temp.WriteFile("osu!.other.cfg", string.Empty);
        File.SetLastWriteTimeUtc(otherCfg, DateTime.UtcNow.AddMinutes(5));

        Assert.Equal(userCfg, StableData.FindConfigFile(temp.Root));
    }

    [Fact]
    public void FindConfigFile_PrefersMostRecentlyModifiedOtherConfig()
    {
        using var temp = new TempDirectory();
        string older = temp.WriteFile("osu!.older.cfg", string.Empty);
        string newer = temp.WriteFile("osu!.newer.cfg", string.Empty);
        File.SetLastWriteTimeUtc(older, new DateTime(2020, 1, 1));
        File.SetLastWriteTimeUtc(newer, new DateTime(2021, 1, 1));

        Assert.Equal(newer, StableData.FindConfigFile(temp.Root));
    }

    [Fact]
    public void FindConfigFile_FallsBackToPlainConfig()
    {
        using var temp = new TempDirectory();
        string plain = temp.WriteFile("osu!.cfg", string.Empty);

        Assert.Equal(plain, StableData.FindConfigFile(temp.Root));
    }

    [Fact]
    public void NormalizeRoot_RedirectsSongsLeafToParent()
    {
        using var temp = new TempDirectory();
        temp.CreateDirectory("Songs");

        var info = StableData.NormalizeRoot(temp.Combine("Songs"));

        Assert.True(info.RedirectedFromLeaf);
        Assert.Equal(Path.GetFullPath(temp.Root), info.Root);
        Assert.NotNull(info.Note);
    }

    [Fact]
    public void NormalizeRoot_KeepsRegularDirectory()
    {
        using var temp = new TempDirectory();

        var info = StableData.NormalizeRoot(temp.Root);

        Assert.False(info.RedirectedFromLeaf);
        Assert.Equal(Path.GetFullPath(temp.Root), info.Root);
        Assert.Null(info.Note);
    }

    [Fact]
    public void NormalizeRoot_EmptyInput_ReturnsNote()
    {
        var info = StableData.NormalizeRoot("   ");

        Assert.Equal(string.Empty, info.Root);
        Assert.NotNull(info.Note);
    }

    [Fact]
    public void LooksLikeStableRoot_DetectsMarkers()
    {
        using var temp = new TempDirectory();

        Assert.False(StableData.LooksLikeStableRoot(temp.Root));

        temp.CreateDirectory("Songs");
        Assert.True(StableData.LooksLikeStableRoot(temp.Root));
    }

    [Fact]
    public void ResolveSongs_UsesConfiguredDirectory()
    {
        using var temp = new TempDirectory();
        string mirror = temp.CreateDirectory("Mirror");
        temp.WriteFile("osu!.other.cfg", $"BeatmapDirectory = {mirror}\r\n");

        var result = StableData.ResolveSongs(temp.Root);

        Assert.Equal(SongsSource.Config, result.Source);
        Assert.Equal(mirror, result.Path);
        Assert.True(result.Exists);
    }

    [Fact]
    public void ResolveSongs_FallsBackToDefaultSongs()
    {
        using var temp = new TempDirectory();
        string songs = temp.CreateDirectory("Songs");

        var result = StableData.ResolveSongs(temp.Root);

        Assert.Equal(SongsSource.Default, result.Source);
        Assert.Equal(songs, result.Path);
        Assert.True(result.Exists);
    }

    [Fact]
    public void ResolveSongs_WarnsWhenConfiguredDirectoryDiffersFromDefault()
    {
        using var temp = new TempDirectory();
        string mirror = temp.CreateDirectory("Mirror");
        temp.CreateDirectory("Songs");
        temp.WriteFile("osu!.other.cfg", $"BeatmapDirectory = {mirror}\r\n");

        var result = StableData.ResolveSongs(temp.Root);

        Assert.Equal(mirror, result.Path);
        Assert.NotNull(result.Warning);
    }
}
