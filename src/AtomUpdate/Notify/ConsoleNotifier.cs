using AtomUpdate.Abstractions.Enums;
using AtomUpdate.Abstractions.Interfaces;
using AtomUpdate.Abstractions.Models;
using AtomUpdate.Configuration;

namespace AtomUpdate.Notify;

/// <summary>控制台通知（04 §3.13 MVP）。Auto 在无 UI 宿主下回落 Console；Toast/InApp/Tray 二阶段。</summary>
public sealed class ConsoleNotifier(AtomUpdateOptions options) : IUpdateNotifier
{
    public Task NotifyAsync(NotificationContext context, CancellationToken ct = default)
    {
        var mode = ResolveMode(options.Notify.Mode);
        if (mode == NotifyMode.None)
        {
            return Task.CompletedTask;
        }

        var title = context.Title ?? options.Notify.TitleTemplate.Replace("{version}", context.Version ?? "");
        var line = $"[AtomUpdate] {title}";
        if (!string.IsNullOrEmpty(context.Body))
        {
            line += Environment.NewLine + context.Body;
        }

        if (options.Notify.ShowReleaseNotes && context.ReleaseNotes is { } notes)
        {
            var sections = new (string Title, IReadOnlyList<string> Items)[]
            {
                ("新增", notes.New),
                ("改进", notes.Improvements),
                ("修复", notes.Fixes),
                ("其他", notes.Notes)
            };

            var text = string.Join(Environment.NewLine, sections
                .Where(s => s.Items.Count > 0)
                .Select(s => s.Title + "：" + Environment.NewLine + string.Join(Environment.NewLine,
                    s.Items.Select(i => "  - " + i))));
            if (!string.IsNullOrEmpty(text))
            {
                line += Environment.NewLine + text;
            }
        }

        Console.WriteLine(line);
        return Task.CompletedTask;
    }

    public static NotifyMode ResolveMode(NotifyMode configured) => configured switch
    {
        NotifyMode.Auto => NotifyMode.Console,
        NotifyMode.System or NotifyMode.Tray or NotifyMode.InApp => NotifyMode.Console, // MVP 回落
        _ => configured
    };
}
