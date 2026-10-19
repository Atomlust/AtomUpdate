namespace AtomUpdate.Abstractions.Enums;

/// <summary>通知模式（04 §3.13）。Auto 按宿主形态选择：有 UI→InApp；无 UI→System Toast；控制台→Console。MVP 未实现项自动回落。</summary>
public enum NotifyMode
{
    /// <summary>自动选择。</summary>
    Auto,
    /// <summary>系统 Toast。</summary>
    System,
    /// <summary>托盘通知（二阶段）。</summary>
    Tray,
    /// <summary>应用内窗口（二阶段）。</summary>
    InApp,
    /// <summary>控制台输出。</summary>
    Console,
    /// <summary>不通知。</summary>
    None
}
