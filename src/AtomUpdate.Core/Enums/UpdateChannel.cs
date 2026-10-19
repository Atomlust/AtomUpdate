namespace AtomUpdate.Core.Enums;

/// <summary>全局预设通道（M2.5 起：无通道实体，发布矩阵 = 版本 × 通道）。</summary>
public enum UpdateChannel
{
    /// <summary>开发版</summary>
    Dev,

    /// <summary>预览版</summary>
    Preview,

    /// <summary>公测版</summary>
    Beta,

    /// <summary>候选版</summary>
    Rc,

    /// <summary>正式稳定版</summary>
    Stable
}
