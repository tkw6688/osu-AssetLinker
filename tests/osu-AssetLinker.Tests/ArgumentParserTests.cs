namespace OsuAssetLinker.Tests;

public class ArgumentParserTests
{
    [Fact]
    public void Default_SyncsBeatmapsOnly()
    {
        var parsed = ArgumentParser.Parse([]);

        Assert.Null(parsed.Error);
        var options = Assert.IsType<CliOptions>(parsed.Options);
        Assert.Equal(new[] { SyncFeature.Beatmaps }, options.Features);
        Assert.False(options.Relink);
        Assert.False(options.DryRun);
        Assert.Null(options.LazerDir);
        Assert.Null(options.StableDir);
    }

    [Fact]
    public void ParsesNamedPaths_InBothForms()
    {
        var parsed = ArgumentParser.Parse(["--lazer-dir", @"C:\lazer", @"--stable-dir=D:\osu!"]);

        Assert.Null(parsed.Error);
        var options = Assert.IsType<CliOptions>(parsed.Options);
        Assert.Equal(@"C:\lazer", options.LazerDir);
        Assert.Equal(@"D:\osu!", options.StableDir);
    }

    [Fact]
    public void All_EnablesBothFeatures_InStableOrder()
    {
        var parsed = ArgumentParser.Parse(["--skins", "--beats"]);

        var options = Assert.IsType<CliOptions>(parsed.Options);
        Assert.Equal(new[] { SyncFeature.Beatmaps, SyncFeature.Skins }, options.Features);
    }

    [Fact]
    public void SkinsOnly()
    {
        var parsed = ArgumentParser.Parse(["--skins"]);

        var options = Assert.IsType<CliOptions>(parsed.Options);
        Assert.Equal(new[] { SyncFeature.Skins }, options.Features);
    }

    [Fact]
    public void Relink_IsParsed()
    {
        var parsed = ArgumentParser.Parse(["--relink"]);

        var options = Assert.IsType<CliOptions>(parsed.Options);
        Assert.True(options.Relink);
    }

    [Fact]
    public void DryRun_IsParsed()
    {
        var parsed = ArgumentParser.Parse(["--dry-run"]);

        var options = Assert.IsType<CliOptions>(parsed.Options);
        Assert.True(options.DryRun);
    }

    [Theory]
    [InlineData("--lazer-dir")]
    [InlineData("--stable-dir")]
    public void MissingPathValue_ReturnsError(string option)
    {
        var parsed = ArgumentParser.Parse([option]);

        Assert.Null(parsed.Options);
        Assert.NotNull(parsed.Error);
    }

    [Fact]
    public void PathValueStartingWithDash_IsTreatedAsMissing()
    {
        var parsed = ArgumentParser.Parse(["--lazer-dir", "--skins"]);

        Assert.Null(parsed.Options);
        Assert.NotNull(parsed.Error);
    }

    [Theory]
    [InlineData("--lazer-dir")]
    [InlineData("--stable-dir")]
    public void DuplicatePath_ReturnsError(string option)
    {
        var parsed = ArgumentParser.Parse([option, @"C:\a", option, @"C:\b"]);

        Assert.Null(parsed.Options);
        Assert.Contains("重复", parsed.Error);
    }

    [Fact]
    public void UnknownOption_ReturnsError()
    {
        var parsed = ArgumentParser.Parse(["--nope"]);

        Assert.Null(parsed.Options);
        Assert.Contains("未知参数", parsed.Error);
    }

    [Fact]
    public void PositionalArgument_ReturnsError()
    {
        var parsed = ArgumentParser.Parse([@"C:\lazer"]);

        Assert.Null(parsed.Options);
        Assert.Contains("位置参数", parsed.Error);
    }
}
