namespace AtomUpdate.Abstractions.Enums;

/// <summary>SDK 更新流程状态机（04 §3.5）。</summary>
public enum UpdateState
{
    /// <summary>空闲。</summary>
    Idle,
    /// <summary>正在检查。</summary>
    Checking,
    /// <summary>无可用更新。</summary>
    NoUpdate,
    /// <summary>有可用更新（未开始下载）。</summary>
    UpdateAvailable,
    /// <summary>前台下载中（用户可见，不可取消）。</summary>
    ForegroundDownloading,
    /// <summary>静默下载中。</summary>
    SilentDownloading,
    /// <summary>下载完成（校验通过）。</summary>
    Downloaded,
    /// <summary>就绪可安装。</summary>
    ReadyToInstall,
    /// <summary>安装已调度（等待主程序退出）。</summary>
    InstallScheduled,
    /// <summary>安装中（更新器执行）。</summary>
    Installing,
    /// <summary>等待重启。</summary>
    RestartPending,
    /// <summary>安装完成（新版本已运行）。</summary>
    Completed,
    /// <summary>失败（可重试回 Checking）。</summary>
    Failed,
    /// <summary>用户推迟（超期/超次进入强制路径）。</summary>
    Postponed
}
