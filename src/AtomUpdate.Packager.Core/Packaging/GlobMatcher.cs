using System.Text;
using System.Text.RegularExpressions;

namespace AtomUpdate.Packager.Core.Packaging;

/// <summary>轻量 glob 匹配（docs/05 §2 偏差：手写替代，不引 glob 库）。
/// 支持 *（段内）、**（跨段或独立段）、?（单字符）；大小写不敏感（Windows 路径惯例）；
/// 无斜杠模式匹配任意路径段（如 "logs" 排除任意层级的 logs）。</summary>
public static class GlobMatcher
{
    public static bool IsMatch(string relativePath, string pattern)
    {
        var path = relativePath.Replace('\\', '/').TrimStart('/');
        var glob = pattern.Replace('\\', '/').Trim('/');

        if (glob.Length == 0)
        {
            return false;
        }

        // 无斜杠：匹配任意单段（目录名或文件名）
        if (!glob.Contains('/'))
        {
            return path.Split('/').Any(segment => MatchesRegex(ConvertToRegex(glob), segment));
        }

        return MatchesRegex(ConvertToRegex(glob), path);
    }

    public static bool IsExcluded(string relativePath, IReadOnlyList<string> patterns) =>
        patterns.Any(pattern => IsMatch(relativePath, pattern));

    private static string ConvertToRegex(string glob)
    {
        var builder = new StringBuilder("^");
        for (var i = 0; i < glob.Length; i++)
        {
            var c = glob[i];
            if (c == '*')
            {
                if (i + 1 < glob.Length && glob[i + 1] == '*')
                {
                    if (i + 2 < glob.Length && glob[i + 2] == '/')
                    {
                        builder.Append("(?:[^/]+/)*"); // "**/" 匹配零或多段
                        i += 2;
                    }
                    else
                    {
                        builder.Append(".*");
                        i += 1;
                    }
                }
                else
                {
                    builder.Append("[^/]*");
                }
            }
            else if (c == '?')
            {
                builder.Append("[^/]");
            }
            else
            {
                builder.Append(Regex.Escape(c.ToString()));
            }
        }

        builder.Append('$');
        return builder.ToString();
    }

    private static bool MatchesRegex(string pattern, string value) =>
        Regex.IsMatch(value, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
}
