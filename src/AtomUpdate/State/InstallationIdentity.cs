using System.Security.Cryptography;
using System.Text;
using AtomUpdate.Abstractions.Interfaces;

namespace AtomUpdate.State;

/// <summary>installationId（匿名 UUID，首次生成不重置）与 machineFingerprint（硬件哈希按应用隔离）。</summary>
public static class InstallationIdentity
{
    public const string InstallationIdKey = "installation";
    public const string FingerprintKey = "fingerprint";

    public static async Task<string> GetOrCreateInstallationIdAsync(IStateStore store, CancellationToken ct = default)
    {
        var existing = await store.GetStringAsync(InstallationIdKey, ct);
        if (!string.IsNullOrEmpty(existing))
        {
            return existing;
        }

        var id = Guid.NewGuid().ToString("D");
        await store.SetStringAsync(InstallationIdKey, id, ct);
        return id;
    }

    /// <summary>指纹 = SHA256(机器标识 + appId 盐) 前 32 个十六进制字符；机器标识跨平台取机器级稳定值。</summary>
    public static async Task<string> GetOrCreateFingerprintAsync(IStateStore store, string appId,
        CancellationToken ct = default)
    {
        var existing = await store.GetStringAsync(FingerprintKey, ct);
        if (!string.IsNullOrEmpty(existing))
        {
            return existing;
        }

        var seed = $"{ResolveMachineId()}|{appId}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(seed)));
        var fingerprint = hash[..32];
        await store.SetStringAsync(FingerprintKey, fingerprint, ct);
        return fingerprint;
    }

    private static string ResolveMachineId()
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Cryptography");
                if (key?.GetValue("MachineGuid") is string guid && !string.IsNullOrEmpty(guid))
                {
                    return guid;
                }
            }
            catch
            {
                // 注册表不可读时回退机器名
            }
        }

        return Environment.MachineName;
    }
}
