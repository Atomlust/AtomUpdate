using System.Globalization;
using System.Security.Cryptography;

namespace AtomUpdate.Api.Auth;

/// <summary>PBKDF2 自实现（docs/07 §4.2）：SHA-256、100k 迭代、16 字节随机盐、常数时间比较；
/// 存储格式 v1.{iterations}.{salt-b64}.{hash-b64} 版本化，便于日后升级参数。</summary>
public static class PasswordHasher
{
    private const int Iterations = 100_000;
    private const int SaltSize = 16;
    private const int KeySize = 32;

    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeySize);
        return FormattableString.Invariant($"v1.{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(key)}");
    }

    public static bool Verify(string password, string hash)
    {
        var parts = hash.Split('.');
        if (parts.Length != 4 || parts[0] != "v1" ||
            !int.TryParse(parts[1], CultureInfo.InvariantCulture, out var iterations) ||
            iterations < 1)
        {
            return false;
        }

        byte[] salt, expected;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expected = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
