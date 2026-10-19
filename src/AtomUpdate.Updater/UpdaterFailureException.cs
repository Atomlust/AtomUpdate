using AtomUpdate.Updater.Replace;

namespace AtomUpdate.Updater;

/// <summary>更新器步骤失败（携带退出码，04 §4.6）。</summary>
public sealed class UpdaterFailureException(int exitCode, string message, Exception? inner = null)
    : Exception(message, inner)
{
    public int ExitCode { get; } = exitCode;

    /// <summary>替换阶段失败时携带的回滚清单（备份已完成，供上层回滚；未进入替换阶段时为 null）。</summary>
    public BackupManifest? RollbackManifest { get; init; }
}
