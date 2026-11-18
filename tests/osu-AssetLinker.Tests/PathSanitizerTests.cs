namespace OsuAssetLinker.Tests;

public class PathSanitizerTests
{
    [Fact]
    public void SanitizeName_ReplacesWindowsInvalidCharacters()
    {
        string result = PathSanitizer.SanitizeName("a<b>c:d\"e/f\\g|h?i*j");

        Assert.Equal("a_b_c_d_e_f_g_h_i_j", result);
    }

    [Fact]
    public void SanitizeName_TrimsWhitespaceAndTrailingDots()
    {
        Assert.Equal("name", PathSanitizer.SanitizeName("  name...  "));
        Assert.Equal("name", PathSanitizer.SanitizeName("name."));
    }

    [Fact]
    public void SanitizeFilePath_ProcessesEachComponent()
    {
        string result = PathSanitizer.SanitizeFilePath("sub/a:b.png");

        Assert.Equal(Path.Combine("sub", "a_b.png"), result);
    }

    [Fact]
    public void SanitizeFilePath_ReplacesEmptyComponents()
    {
        string result = PathSanitizer.SanitizeFilePath("a//b");

        Assert.Equal(string.Join(Path.DirectorySeparatorChar, "a", "_", "b"), result);
    }

    [Fact]
    public void SanitizeFilePath_CleansTrailingDotsPerComponent()
    {
        string result = PathSanitizer.SanitizeFilePath("dir./file.");

        Assert.Equal(Path.Combine("dir", "file"), result);
    }
}
