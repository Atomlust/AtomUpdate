using System.Security.Cryptography;
using AtomUpdate.Api.Data;
using Microsoft.Data.Sqlite;

namespace AtomUpdate.Api.Auth;

/// <summary>解析后的 JWT 签名密钥与来源（显式配置 / 自动生成密钥文件）。</summary>
public sealed record JwtSigningKeyInfo(string Key, string Source);

/// <summary>JWT 签名密钥解析（零配置部署，docs/09）：显式 Auth:Jwt:SigningKey 优先；
/// 缺失时自动生成 64 字节随机密钥并持久化到密钥文件（重启复用，已签发 token 不失效）。
/// 密钥文件默认取 SQLite 库文件同目录（compose sqlite 形态即 /data 数据卷）；
/// 既无配置又无法落盘（只读文件系统）时启动即失败，提示显式配置。</summary>
public static class JwtSigningKeyProvider
{
    public const string DefaultFileName = "jwt-signing-key.txt";

    public static JwtSigningKeyInfo Resolve(IConfiguration configuration, string contentRootPath)
    {
        var explicitKey = configuration["Auth:Jwt:SigningKey"];
        if (!string.IsNullOrWhiteSpace(explicitKey))
        {
            return new JwtSigningKeyInfo(explicitKey, "配置 Auth:Jwt:SigningKey");
        }

        var path = ResolveAutoKeyFile(configuration, contentRootPath);
        try
        {
            // 自定义 AutoKeyFile 的父目录可能不存在：按需创建（默认场景目录已随数据卷就绪）
            var parent = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(parent) && !Directory.Exists(parent))
            {
                Directory.CreateDirectory(parent);
            }

            if (File.Exists(path))
            {
                var existing = File.ReadAllText(path).Trim();
                if (existing.Length >= 32)
                {
                    return new JwtSigningKeyInfo(existing, $"密钥文件 {path}");
                }
            }

            // 64 字节随机 → Base64（HS256 强度充分）；写入后重启复用
            var generated = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
            File.WriteAllText(path, generated);
            return new JwtSigningKeyInfo(generated, $"自动生成密钥文件 {path}");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                $"未配置 Auth:Jwt:SigningKey 且自动生成密钥无法写入 {path}。"
                + "请显式配置 JWT 签名密钥（compose：JWT_SIGNING_KEY），或把数据目录挂载为可写卷。", exception);
        }
    }

    /// <summary>密钥文件路径：Auth:Jwt:AutoKeyFile 显式指定（相对路径按 ContentRoot 解析）→
    /// SQLite 库文件同目录（零配置 sqlite 形态的持久化数据区）→ ContentRoot（npgsql 等无本地数据区场景）。</summary>
    private static string ResolveAutoKeyFile(IConfiguration configuration, string contentRootPath)
    {
        if (configuration["Auth:Jwt:AutoKeyFile"] is { Length: > 0 } configured)
        {
            return Path.IsPathRooted(configured) ? configured : Path.Combine(contentRootPath, configured);
        }

        var sqlite = DatabaseProviders.ResolveSqliteConnectionString(configuration);
        if (!string.IsNullOrWhiteSpace(sqlite))
        {
            var source = new SqliteConnectionStringBuilder(sqlite).DataSource;
            if (!string.IsNullOrWhiteSpace(source))
            {
                var dbPath = Path.IsPathRooted(source) ? source : Path.Combine(contentRootPath, source);
                var directory = Path.GetDirectoryName(dbPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    return Path.Combine(directory, DefaultFileName);
                }
            }
        }

        return Path.Combine(contentRootPath, DefaultFileName);
    }
}
