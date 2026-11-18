namespace OsuAssetLinker.Tests;

public class LazerDataTests
{
    [Fact]
    public void ReadCustomDataPath_ReadsFullPathKey()
    {
        using var temp = new TempDirectory();
        temp.WriteFile("storage.ini", "[Storage]\r\nFullPath = D:\\osu-lazer\r\n");

        Assert.Equal(@"D:\osu-lazer", LazerData.ReadCustomDataPath(temp.Root));
    }

    [Fact]
    public void ReadCustomDataPath_IsCaseInsensitive_AndUnquotes()
    {
        using var temp = new TempDirectory();
        temp.WriteFile("storage.ini", "fullpath = \"D:\\osu lazer\"\r\n");

        Assert.Equal(@"D:\osu lazer", LazerData.ReadCustomDataPath(temp.Root));
    }

    [Fact]
    public void ReadCustomDataPath_EmptyValue_ReturnsNull()
    {
        using var temp = new TempDirectory();
        temp.WriteFile("storage.ini", "[Storage]\r\nFullPath =\r\n");

        Assert.Null(LazerData.ReadCustomDataPath(temp.Root));
    }

    [Fact]
    public void ReadCustomDataPath_MissingFile_ReturnsNull()
    {
        using var temp = new TempDirectory();

        Assert.Null(LazerData.ReadCustomDataPath(temp.Root));
    }

    [Fact]
    public void FindRealmFile_PrefersPlainClientRealm()
    {
        using var temp = new TempDirectory();
        string direct = temp.WriteFile("client.realm", string.Empty);
        temp.WriteFile("client_53.realm", string.Empty);

        Assert.Equal(direct, LazerData.FindRealmFile(temp.Root));
    }

    [Fact]
    public void FindRealmFile_PicksHighestVersionedRealm()
    {
        using var temp = new TempDirectory();
        temp.WriteFile("client_52.realm", string.Empty);
        string newer = temp.WriteFile("client_53.realm", string.Empty);

        Assert.Equal(newer, LazerData.FindRealmFile(temp.Root));
    }

    [Fact]
    public void FindRealmFile_IgnoresCorruptBackups()
    {
        using var temp = new TempDirectory();
        temp.WriteFile("client_53_20260101_corrupt.realm", string.Empty);

        Assert.Null(LazerData.FindRealmFile(temp.Root));
    }

    [Fact]
    public void ResolveDataDirectory_FollowsStorageIniWhenRealmExists()
    {
        using var temp = new TempDirectory();
        string custom = temp.CreateDirectory("custom");
        temp.WriteFile("custom/client.realm", string.Empty);
        temp.WriteFile("storage.ini", $"FullPath = {custom}\r\n");

        string resolved = LazerData.ResolveDataDirectory(temp.Root, out bool redirected);

        Assert.True(redirected);
        Assert.Equal(Path.GetFullPath(custom), resolved);
    }

    [Fact]
    public void ResolveDataDirectory_StaysWhenCustomDirectoryHasNoRealm()
    {
        using var temp = new TempDirectory();
        string custom = temp.CreateDirectory("custom");
        temp.WriteFile("storage.ini", $"FullPath = {custom}\r\n");

        string resolved = LazerData.ResolveDataDirectory(temp.Root, out bool redirected);

        Assert.False(redirected);
        Assert.Equal(temp.Root, resolved);
    }

    [Fact]
    public void LooksLikeLazerDataDirectory_DetectsStorageIni()
    {
        using var temp = new TempDirectory();
        Assert.False(LazerData.LooksLikeLazerDataDirectory(temp.Root));

        temp.WriteFile("storage.ini", string.Empty);
        Assert.True(LazerData.LooksLikeLazerDataDirectory(temp.Root));
    }

    [Fact]
    public void HasLeftoverRealmArtifacts_DetectsLockFile()
    {
        using var temp = new TempDirectory();
        Assert.False(LazerData.HasLeftoverRealmArtifacts(temp.Root));

        temp.WriteFile("client.realm.lock", string.Empty);
        Assert.True(LazerData.HasLeftoverRealmArtifacts(temp.Root));
    }
}
