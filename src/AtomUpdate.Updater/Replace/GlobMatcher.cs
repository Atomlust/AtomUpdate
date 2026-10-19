namespace AtomUpdate.Updater.Replace;

/// <summary>glob 匹配（04 §4.9）：精确、单层 logs/*、递归 data/**、任意层级 **/*.log；Linux 大小写敏感，Windows/macOS 不敏感。</summary>
public static class GlobMatcher
{
    public static bool IsMatch(string pattern, string relativePath, bool caseSensitive)
    {
        var patternSegments = Normalize(pattern).Split('/', StringSplitOptions.RemoveEmptyEntries);
        var pathSegments = Normalize(relativePath).Split('/', StringSplitOptions.RemoveEmptyEntries);
        return Match(patternSegments, 0, pathSegments, 0, caseSensitive);
    }

    public static bool IsMatchAny(IEnumerable<string> patterns, string relativePath, bool caseSensitive)
    {
        foreach (var pattern in patterns)
        {
            if (!string.IsNullOrWhiteSpace(pattern) && IsMatch(pattern, relativePath, caseSensitive))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>默认大小写策略：Windows/macOS 不敏感，Linux 敏感（04 §4.9）。</summary>
    public static bool DefaultCaseSensitive => OperatingSystem.IsLinux();

    private static string Normalize(string path) => path.Replace('\\', '/').Trim('/');

    private static bool Match(string[] pattern, int pi, string[] path, int si, bool caseSensitive)
    {
        while (pi < pattern.Length)
        {
            var segment = pattern[pi];
            if (segment == "**")
            {
                if (pi == pattern.Length - 1)
                {
                    return true;
                }

                for (var skip = si; skip <= path.Length; skip++)
                {
                    if (Match(pattern, pi + 1, path, skip, caseSensitive))
                    {
                        return true;
                    }
                }

                return false;
            }

            if (si >= path.Length || !SegmentMatch(segment, path[si], caseSensitive))
            {
                return false;
            }

            pi++;
            si++;
        }

        return si == path.Length;
    }

    /// <summary>单段通配：* 任意字符（不含 /），? 单字符。</summary>
    private static bool SegmentMatch(string pattern, string text, bool caseSensitive)
    {
        int p = 0, t = 0, starP = -1, starT = 0;
        while (t < text.Length)
        {
            if (p < pattern.Length && (pattern[p] == '?' || CharEquals(pattern[p], text[t], caseSensitive)))
            {
                p++;
                t++;
            }
            else if (p < pattern.Length && pattern[p] == '*')
            {
                starP = p++;
                starT = t;
            }
            else if (starP >= 0)
            {
                p = starP + 1;
                t = ++starT;
            }
            else
            {
                return false;
            }
        }

        while (p < pattern.Length && pattern[p] == '*')
        {
            p++;
        }

        return p == pattern.Length;
    }

    private static bool CharEquals(char a, char b, bool caseSensitive) =>
        caseSensitive
            ? a == b
            : char.ToLowerInvariant(a) == char.ToLowerInvariant(b);
}
