using AtomUpdate.Api.Auth;
using Microsoft.Extensions.Configuration;

namespace AtomUpdate.Tests.Api;

/// <summary>JWT 签名密钥解析（docs/09 零配置）：显式配置优先 → 自动生成密钥文件并复用 → 无法落盘明确失败。</summary>
public class JwtSigningKeyProviderTests
{
    private static IConfiguration Config(Dictionary<string, string?> data) =>
        new ConfigurationBuilder().AddInMemoryCollection(data).Build();

    [Fact]
    public void ExplicitKey_TakesPriority_AndCreatesNoFile()
    {
        var dir = NewTempDir();
        try
        {
            var info = JwtSigningKeyProvider.Resolve(
                Config(new() { ["Auth:Jwt:SigningKey"] = "explicit-key-at-least-32-chars-long!!" }), dir);

            Assert.Equal("explicit-key-at-least-32-chars-long!!", info.Key);
            Assert.False(File.Exists(Path.Combine(dir, JwtSigningKeyProvider.DefaultFileName)));
        }
        finally
        {
            Cleanup(dir);
        }
    }

    [Fact]
    public void WithoutConfig_GeneratesKeyFile_AndReusesOnNextResolve()
    {
        var dir = NewTempDir();
        try
        {
            // 默认取 SQLite 库文件同目录（零配置 sqlite 形态的数据区约定）
            var config = Config(new() { ["ConnectionStrings:Sqlite"] = $"Data Source={Path.Combine(dir, "atomupdate.db")}" });

            var first = JwtSigningKeyProvider.Resolve(config, dir);
            Assert.True(first.Key.Length >= 32);
            var file = Path.Combine(dir, JwtSigningKeyProvider.DefaultFileName);
            Assert.True(File.Exists(file));

            var second = JwtSigningKeyProvider.Resolve(config, dir);
            Assert.Equal(first.Key, second.Key);
            Assert.Contains("密钥文件", second.Source);
        }
        finally
        {
            Cleanup(dir);
        }
    }

    [Fact]
    public void AutoKeyFile_OverridesDefaultPath()
    {
        var dir = NewTempDir();
        try
        {
            // AutoKeyFile 父目录不存在时由 provider 按需创建
            var config = Config(new()
            {
                ["ConnectionStrings:Sqlite"] = $"Data Source={Path.Combine(dir, "atomupdate.db")}",
                ["Auth:Jwt:AutoKeyFile"] = Path.Combine(dir, "keys", "custom-key.txt")
            });

            var info = JwtSigningKeyProvider.Resolve(config, dir);
            Assert.True(File.Exists(Path.Combine(dir, "keys", "custom-key.txt")));
            Assert.True(info.Key.Length >= 32);
            Assert.False(File.Exists(Path.Combine(dir, JwtSigningKeyProvider.DefaultFileName)));
        }
        finally
        {
            Cleanup(dir);
        }
    }

    [Fact]
    public void UnwritablePath_ThrowsWithClearMessage()
    {
        var dir = NewTempDir();
        try
        {
            // AutoKeyFile 指向"以既有文件为父目录"的路径 → 写入必然失败（DirectoryNotFound/IOException）
            var blocker = Path.Combine(dir, "blocker.txt");
            File.WriteAllText(blocker, "not a directory");
            var config = Config(new() { ["Auth:Jwt:AutoKeyFile"] = Path.Combine(blocker, "key.txt") });

            var exception = Assert.Throws<InvalidOperationException>(
                () => JwtSigningKeyProvider.Resolve(config, dir));
            Assert.Contains("Auth:Jwt:SigningKey", exception.Message);
        }
        finally
        {
            Cleanup(dir);
        }
    }

    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"atomupdate-jwkey-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void Cleanup(string dir)
    {
        try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
    }
}
