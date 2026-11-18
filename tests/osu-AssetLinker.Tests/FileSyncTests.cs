namespace OsuAssetLinker.Tests;

public class FileSyncTests
{
    [Fact]
    public void LinkOne_MissingDestination_CreatesLink()
    {
        using var temp = new TempDirectory();
        string source = temp.WriteFile("src/a.png", "hello");
        string destDir = temp.CreateDirectory("dst");
        string dest = Path.Combine(destDir, "a.png");

        var outcome = FileSync.LinkOne(source, dest, relink: false);

        Assert.Equal(FileSync.Outcome.Linked, outcome);
        Assert.True(File.Exists(dest));
    }

    [Fact]
    public void LinkOne_ExistingDestination_WithoutRelink_Skips()
    {
        using var temp = new TempDirectory();
        string source = temp.WriteFile("src/a.png", "hello");
        string dest = temp.WriteFile("dst/a.png", "hello");

        var outcome = FileSync.LinkOne(source, dest, relink: false);

        Assert.Equal(FileSync.Outcome.Skipped, outcome);
    }

    [Fact]
    public void LinkOne_IdenticalRegularFile_Relinks()
    {
        using var temp = new TempDirectory();
        string source = temp.WriteFile("src/a.png", "hello");
        string dest = temp.WriteFile("dst/a.png", "hello");

        var outcome = FileSync.LinkOne(source, dest, relink: true);

        Assert.Equal(FileSync.Outcome.Relinked, outcome);
        if (OperatingSystem.IsWindows())
            Assert.True(HardLink.GetLinkCount(dest) > 1);
    }

    [Fact]
    public void LinkOne_ContentDiffers_PreservesDestination()
    {
        using var temp = new TempDirectory();
        string source = temp.WriteFile("src/a.png", "hello");
        string dest = temp.WriteFile("dst/a.png", "edited");

        var outcome = FileSync.LinkOne(source, dest, relink: true);

        Assert.Equal(FileSync.Outcome.ContentDiffers, outcome);
        Assert.Equal("edited", File.ReadAllText(dest));
    }

    [Fact]
    public void LinkOne_Relink_DoesNotLeaveTempFiles()
    {
        using var temp = new TempDirectory();
        string source = temp.WriteFile("src/a.png", "hello");
        string destDir = temp.CreateDirectory("dst");
        string dest = temp.WriteFile("dst/a.png", "hello");

        FileSync.LinkOne(source, dest, relink: true);

        Assert.Empty(Directory.GetFiles(destDir, "~al*.tmp"));
    }
}
