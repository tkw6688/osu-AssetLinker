namespace OsuAssetLinker.Tests;

public class FileJobExecutorTests
{
    [Fact]
    public void Sync_CreatesHardLink()
    {
        using var temp = new TempDirectory();
        string source = temp.WriteFile("src/a.png", "hello");
        string dest = temp.Combine("dst", "a.png");

        var result = FileJobExecutor.Execute([new FileJob(source, dest)], FileJobMode.Sync);

        Assert.Equal(1, result.Linked);
        Assert.True(File.Exists(dest));
        Assert.Equal("hello", File.ReadAllText(dest));
    }

    [Fact]
    public void Sync_MissingSource_CountsFailure()
    {
        using var temp = new TempDirectory();
        var job = new FileJob(temp.Combine("missing.png"), temp.Combine("dst", "missing.png"));

        var result = FileJobExecutor.Execute([job], FileJobMode.Sync);

        Assert.Equal(1, result.Failed);
        Assert.False(File.Exists(job.DestPath));
    }

    [Fact]
    public void Relink_ReplacesIdenticalRegularFile()
    {
        using var temp = new TempDirectory();
        string source = temp.WriteFile("src/a.png", "hello");
        string dest = temp.WriteFile("dst/a.png", "hello");

        var result = FileJobExecutor.Execute([new FileJob(source, dest)], FileJobMode.Relink);

        Assert.Equal(1, result.Relinked);
    }

    [Fact]
    public void Relink_ContentDiffers_DoesNotOverwrite()
    {
        using var temp = new TempDirectory();
        string source = temp.WriteFile("src/a.png", "hello");
        string dest = temp.WriteFile("dst/a.png", "edited");

        var result = FileJobExecutor.Execute([new FileJob(source, dest)], FileJobMode.Relink);

        Assert.Equal(1, result.ContentDiffered);
        Assert.Equal("edited", File.ReadAllText(dest));
    }

    [Fact]
    public void Relink_MissingSource_IsIgnored()
    {
        using var temp = new TempDirectory();
        string dest = temp.WriteFile("dst/a.png", "hello");
        var job = new FileJob(temp.Combine("missing.png"), dest);

        var result = FileJobExecutor.Execute([job], FileJobMode.Relink);

        Assert.Equal(0, result.Failed);
        Assert.Equal(0, result.Relinked);
    }

    [Fact]
    public void Relink_MissingDestination_IsNotCreated()
    {
        using var temp = new TempDirectory();
        string source = temp.WriteFile("src/a.png", "hello");
        var job = new FileJob(source, temp.Combine("dst", "a.png"));

        var result = FileJobExecutor.Execute([job], FileJobMode.Relink);

        Assert.Equal(0, result.Failed);
        Assert.Equal(0, result.Relinked);
        Assert.False(File.Exists(job.DestPath));
    }

    [Fact]
    public void Relink_AlreadyLinked_IsSkipped()
    {
        using var temp = new TempDirectory();
        string source = temp.WriteFile("src/a.png", "hello");
        string dest = temp.Combine("dst", "a.png");

        FileJobExecutor.Execute([new FileJob(source, dest)], FileJobMode.Sync);
        var result = FileJobExecutor.Execute([new FileJob(source, dest)], FileJobMode.Relink);

        Assert.Equal(0, result.Relinked);
        Assert.Equal(0, result.ContentDiffered);
        Assert.Equal(0, result.Failed);
    }
}
