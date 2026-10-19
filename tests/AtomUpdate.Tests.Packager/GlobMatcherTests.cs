using AtomUpdate.Packager.Core.Packaging;

namespace AtomUpdate.Tests.Packager;

public class GlobMatcherTests
{
    [Theory]
    [InlineData("app.dll", "*.pdb", false)]
    [InlineData("app.pdb", "*.pdb", true)]
    [InlineData("debug/app.pdb", "*.pdb", true)] // 无斜杠模式匹配任意段
    [InlineData("logs/app.log", "logs/**", true)]
    [InlineData("logs/nested/app.log", "logs/**", true)]
    [InlineData("src/logs/a.txt", "**/logs/**", true)]
    [InlineData("src/app.bin", "**/logs/**", false)]
    [InlineData("file.txt", "*.txt", true)]
    [InlineData("a/b/file.txt", "a?/file.txt", false)]
    [InlineData("ab/file.txt", "a?/file.txt", true)]
    [InlineData("bin/app.dll", "bin/*.dll", true)]
    [InlineData("bin/x/y/app.dll", "bin/*.dll", false)] // * 不跨段
    [InlineData(".git/HEAD", ".git/**", true)]
    [InlineData("src/.git/HEAD", "**/.git/**", true)]
    public void IsMatch_GlobSemantics(string path, string pattern, bool expected)
    {
        Assert.Equal(expected, GlobMatcher.IsMatch(path, pattern));
    }

    [Fact]
    public void IsExcluded_AnyPatternMatches()
    {
        Assert.True(GlobMatcher.IsExcluded("x/app.pdb", ["*.xml", "*.pdb"]));
        Assert.False(GlobMatcher.IsExcluded("x/app.dll", ["*.xml", "*.pdb"]));
    }

    [Fact]
    public void IsMatch_EmptyPattern_NeverMatches()
    {
        Assert.False(GlobMatcher.IsMatch("anything.txt", ""));
        Assert.False(GlobMatcher.IsMatch("anything.txt", "  "));
    }
}
