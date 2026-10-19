using AtomUpdate.Core.Models.Manage;

namespace AtomUpdate.Web.Client.Services;

/// <summary>更新日志四段（新增/优化/修复/注意）的输入约定：每行一条要点，行内 Markdown 原样存取。
/// 行首的 "- " / "* " 项目符号由输入者自由书写，入库前剥掉——展示端（详情页 / 客户端通知）本身带列表符号。</summary>
public static class NoteSections
{
    public static IReadOnlyList<string> Parse(string? text) =>
        text is null
            ? []
            : [.. text.Split('\n').Select(StripBullet).Where(line => line.Length > 0)];

    public static string Format(IReadOnlyList<string>? items) => string.Join('\n', items ?? []);

    /// <summary>四段全空则不下发日志（null 表示不修改）。</summary>
    public static ReleaseNoteInput? ToInput(string? added, string? improved, string? fixes, string? noticed)
    {
        var sections = new[] { added, improved, fixes, noticed };
        if (sections.All(string.IsNullOrWhiteSpace))
        {
            return null;
        }

        return new ReleaseNoteInput
        {
            Language = "en",
            New = Parse(added),
            Improvements = Parse(improved),
            Fixes = Parse(fixes),
            Notes = Parse(noticed)
        };
    }

    private static string StripBullet(string line)
    {
        line = line.Trim();
        return line.StartsWith("- ") || line.StartsWith("* ") ? line[2..].Trim() : line;
    }
}
