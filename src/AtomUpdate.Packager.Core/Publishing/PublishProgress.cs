namespace AtomUpdate.Packager.Core.Publishing;

/// <summary>发布阶段（docs/05 §12 向导进度）。</summary>
public enum PublishStage
{
    Package,
    Delta,
    Upload,
    Push,
    Release
}

/// <summary>发布进度事件：阶段 + 人类可读细节 + 可选百分比（0~100）。</summary>
public sealed record PublishProgress(PublishStage Stage, string Detail, double? Percent = null);
