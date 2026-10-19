namespace AtomUpdate.Abstractions.Enums;

/// <summary>通知类型（04 §3.3 IUpdateNotifier）。</summary>
public enum NotificationType
{
    /// <summary>有可用更新。</summary>
    UpdateAvailable,
    /// <summary>下载完成。</summary>
    DownloadCompleted,
    /// <summary>已就绪可安装。</summary>
    UpdateReady,
    /// <summary>下载失败。</summary>
    DownloadFailed,
    /// <summary>安装失败。</summary>
    InstallFailed
}
