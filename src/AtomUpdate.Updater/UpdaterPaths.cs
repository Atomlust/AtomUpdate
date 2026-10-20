using System.Runtime.InteropServices;

namespace AtomUpdate.Updater;

/// <summary>平台目录解析（04 §4.4/§4.14）。路径契约须与 SDK 侧 PlatformPaths 一致：state/{appId}/ 与 SDK 补报读取同一位置。</summary>
public static class UpdaterPaths
{
    public static string AtomUpdateRoot
    {
        get
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "AtomUpdate");
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "Library", "Application Support", "AtomUpdate");
            }

            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".config", "AtomUpdate");
        }
    }

    /// <summary>状态根（04 §4.4）。</summary>
    public static string StateRoot => Path.Combine(AtomUpdateRoot, "state");

    /// <summary>日志根（04 §4.14：Windows LocalAppData\AtomUpdate\logs\updater；macOS ~/Library/Logs；Linux ~/.local/share）。</summary>
    public static string LogRoot
    {
        get
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "AtomUpdate", "logs", "updater");
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "Library", "Logs", "AtomUpdate", "updater");
            }

            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".local", "share", "AtomUpdate", "logs", "updater");
        }
    }

    /// <summary>默认备份目录（可被 install.backupPath 覆盖）：backup/{appId}/{yyyyMMddHHmmss}/。</summary>
    public static string DefaultBackupDirectory(string appId, DateTimeOffset timestamp) =>
        Path.Combine(AtomUpdateRoot, "backup", Sanitize(appId), timestamp.ToString("yyyyMMddHHmmss"));

    /// <summary>跨平台非法字符补充：Linux/macOS 的 Path.GetInvalidFileNameChars 不含 ':' 与 '\\'，
    /// 但 state/{appId}/ 路径契约要求三平台产出相同目录名（SDK 补报按同一位置读取），统一并入黑名单。</summary>
    private static readonly char[] ExtraInvalidChars = [':', '\\'];

    public static string Sanitize(string value) =>
        string.IsNullOrEmpty(value)
            ? "default"
            : string.Join('_', value.Split(
                Path.GetInvalidFileNameChars().Concat(ExtraInvalidChars).ToArray(),
                StringSplitOptions.RemoveEmptyEntries));
}
