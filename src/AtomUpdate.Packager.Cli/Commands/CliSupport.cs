using AtomUpdate.Core.Enums;
using AtomUpdate.Packager.Core;
using NuGet.Versioning;

namespace AtomUpdate.Packager.Cli.Commands;

/// <summary>命令参数语义错误（区别于解析结构错误）→ 退出码 1（docs/05 §13）。</summary>
internal sealed class CliArgumentException(string message) : Exception(message);

/// <summary>版本已创建、仅通道发布阶段失败 → 退出码 7（docs/05 §13）。</summary>
internal sealed class CliPublishStageException(string message, Exception inner) : Exception(message, inner);

/// <summary>命令公共解析助手：必填/枚举/数字/语义版本校验（docs/05 §13）。</summary>
internal static class CliSupport
{
    public static PackagerPaths ResolvePaths(ParsedCli cli)
    {
        var workDir = cli.Value(CliCatalog.WorkDir) ??
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "atomupdate", "packager");
        var paths = new PackagerPaths(workDir);
        paths.EnsureCreated();
        return paths;
    }

    public static string Require(ParsedCli cli, string option, string? envName = null, string? label = null)
    {
        var value = cli.ValueOrEnv(option, envName);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new CliArgumentException($"缺少必填选项 --{label ?? option}");
        }

        return value.Trim();
    }

    public static string? Optional(ParsedCli cli, string option, string? envName = null)
    {
        var value = cli.ValueOrEnv(option, envName);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    public static long RequireLong(ParsedCli cli, string option, string? envName, string label)
    {
        var raw = Require(cli, option, envName, label);
        if (!long.TryParse(raw, out var value) || value <= 0)
        {
            throw new CliArgumentException($"--{label} 必须是正整数，实际：{raw}");
        }

        return value;
    }

    public static int? OptionalInt(ParsedCli cli, string option)
    {
        var raw = Optional(cli, option);
        if (raw is null)
        {
            return null;
        }

        if (!int.TryParse(raw, out var value) || value <= 0)
        {
            throw new CliArgumentException($"--{option} 必须是正整数，实际：{raw}");
        }

        return value;
    }

    public static string RequireSemVer(ParsedCli cli, string option)
    {
        var raw = Require(cli, option);
        if (!SemanticVersion.TryParse(raw, out _))
        {
            throw new CliArgumentException($"--{option} 不是合法语义版本（如 1.2.0），实际：{raw}");
        }

        return raw;
    }

    public static string? OptionalSemVer(ParsedCli cli, string option)
    {
        var raw = Optional(cli, option);
        if (raw is null)
        {
            return null;
        }

        if (!SemanticVersion.TryParse(raw, out _))
        {
            throw new CliArgumentException($"--{option} 不是合法语义版本，实际：{raw}");
        }

        return raw;
    }

    public static T ParseEnum<T>(string raw, string option) where T : struct, Enum
    {
        if (Enum.TryParse<T>(raw, ignoreCase: true, out var value) && Enum.IsDefined(value))
        {
            return value;
        }

        var allowed = string.Join("/", Enum.GetNames<T>().Select(name => name.ToLowerInvariant()));
        throw new CliArgumentException($"--{option} 取值非法：{raw}（允许：{allowed}）");
    }

    public static IReadOnlyList<T> ParseEnumList<T>(IReadOnlyList<string> rawValues, string option)
        where T : struct, Enum
    {
        var result = new List<T>();
        foreach (var raw in rawValues)
        {
            // 支持 --channels stable,beta 的逗号紧凑写法
            foreach (var part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var value = ParseEnum<T>(part, option);
                if (!result.Contains(value))
                {
                    result.Add(value);
                }
            }
        }

        return result;
    }

    public static string RequireDirectory(ParsedCli cli, string option)
    {
        var raw = Require(cli, option);
        var full = Path.GetFullPath(raw);
        if (!Directory.Exists(full))
        {
            throw new PackagerException(PackagerErrorCodes.SourceNotFound,
                $"目录不存在：--{option} {full}");
        }

        return full;
    }

    public static string? OptionalDirectory(ParsedCli cli, string option)
    {
        var raw = Optional(cli, option);
        return raw is null ? null : Path.GetFullPath(raw);
    }
}
