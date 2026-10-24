using System.Reflection;

namespace AtomUpdate.Web.Client.Services;

/// <summary>
/// 当前部署版本。来源是程序集 InformationalVersion（Directory.Build.props 的
/// Version + 源码哈希，.NET SDK 默认注入），页脚展示用于识别部署版本与最新发布
/// 是否存在差异。形如 <c>v1.0.6 (0802dbb)</c>。
/// </summary>
public static class AppVersion
{
    public static string Display { get; } = Build();

    private static string Build()
    {
        var info = typeof(AppVersion).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrEmpty(info))
        {
            return "-";
        }

        var plus = info.IndexOf('+');
        var version = plus < 0 ? info : info[..plus];
        var commit = plus < 0 ? null : info[(plus + 1)..];

        return string.IsNullOrEmpty(commit)
            ? $"v{version}"
            : $"v{version} ({commit[..Math.Min(7, commit.Length)]})";
    }
}
