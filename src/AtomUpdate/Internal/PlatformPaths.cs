using System.Runtime.InteropServices;

namespace AtomUpdate.Internal;

/// <summary>平台目录与路径解析（04 §3.14/§7 缓存状态路径矩阵）。</summary>
public static class PlatformPaths
{
    public static string AtomUpdateRoot
    {
        get
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                return Path.Combine(localAppData, "AtomUpdate");
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "Library", "Application Support", "AtomUpdate");
            }

            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".config", "AtomUpdate");
        }
    }

    /// <summary>应用隔离目录（state/{appId}/ 或 cache/{appId}/ 之上的 appId 段）。appId 非法字符替换为 '_'。</summary>
    public static string AppScoped(string rootKind, string appId, params string[] segments)
    {
        var path = Path.Combine(new[] { AtomUpdateRoot, rootKind, Sanitize(appId) }.Concat(segments).ToArray());
        Directory.CreateDirectory(path);
        return path;
    }

    public static string Sanitize(string appId) =>
        string.Join('_', appId.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));

    /// <summary>SDK 缓存根（可被 Cache.CacheDirectory 覆盖）。</summary>
    public static string ResolveCacheRoot(string? configured) => configured ?? Path.Combine(AtomUpdateRoot, "cache");

    /// <summary>SDK 状态根。</summary>
    public static string ResolveStateRoot() => Path.Combine(AtomUpdateRoot, "state");
}
