using System.Security.Cryptography;
using System.Text;
using AtomUpdate.Updater.Configuration;
using AtomUpdate.Updater.Logging;

namespace AtomUpdate.Updater.Verification;

/// <summary>二次校验（04 §4.5/§5.1/05 §8）：SHA256 强制（提供即校验）；签名按 verification.requireSignature 可选启用。
/// 签名内容 = UTF8("{sha256}|{toVersion}|{platform}|{arch}")（各字段小写，M4/M5 实现口径）。算法：RSA PKCS#1 v1.5 + SHA256。
/// 公钥集仅信任内置默认与安装目录 updater.default.json（防同机注入，2026-10-06 确认）。</summary>
public static class PackageVerifier
{
    public static string ComputeSha256(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    public static void Verify(UpdaterTaskFile task, UpdaterLog log)
    {
        if (!string.IsNullOrWhiteSpace(task.Sha256))
        {
            var actual = ComputeSha256(task.PackagePath!);
            if (!string.Equals(actual, task.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new UpdaterFailureException(ExitCodes.VerificationFailed,
                    $"SHA256 校验失败：期望 {task.Sha256}，实际 {actual}");
            }

            log.Information($"SHA256 校验通过：{actual}");
        }
        else
        {
            log.Warning("任务未提供 --sha256，跳过哈希校验（SDK 正常链路总会提供）");
        }

        if (!task.Verification.RequireSignature)
        {
            if (!string.IsNullOrWhiteSpace(task.Signature))
            {
                log.Debug("包携带签名，但 verification.requireSignature=false，跳过验证");
            }

            return;
        }

        if (string.IsNullOrWhiteSpace(task.Sha256))
        {
            throw new UpdaterFailureException(ExitCodes.VerificationFailed, "签名验证已启用但缺少 sha256，无法构造签名内容");
        }

        if (string.IsNullOrWhiteSpace(task.Signature))
        {
            throw new UpdaterFailureException(ExitCodes.VerificationFailed, "签名验证已启用但更新包未签名");
        }

        if (string.IsNullOrWhiteSpace(task.KeyId) ||
            !task.Verification.PublicKeys.TryGetValue(task.KeyId, out var publicKey) ||
            string.IsNullOrWhiteSpace(publicKey))
        {
            throw new UpdaterFailureException(ExitCodes.VerificationFailed,
                $"未找到受信公钥：{task.KeyId}（公钥集只能经内置默认或安装目录 updater.default.json 配置）");
        }

        byte[] signature;
        try
        {
            signature = Convert.FromBase64String(task.Signature);
        }
        catch (FormatException exception)
        {
            throw new UpdaterFailureException(ExitCodes.VerificationFailed, "签名不是合法 Base64", exception);
        }

        using var rsa = RSA.Create();
        try
        {
            rsa.ImportFromPem(publicKey);
        }
        catch (Exception exception)
        {
            throw new UpdaterFailureException(ExitCodes.VerificationFailed, "公钥格式无效（期望 PEM RSA 公钥）", exception);
        }

        var content = Encoding.UTF8.GetBytes(SignatureContent(task));
        if (!rsa.VerifyData(content, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
        {
            throw new UpdaterFailureException(ExitCodes.VerificationFailed, "签名验证失败");
        }

        log.Information($"签名验证通过（keyId={task.KeyId}）");
    }

    public static string SignatureContent(UpdaterTaskFile task) =>
        $"{task.Sha256?.ToLowerInvariant()}|{task.ToVersion}|{task.Platform?.ToLowerInvariant()}|{task.Arch?.ToLowerInvariant()}";
}
