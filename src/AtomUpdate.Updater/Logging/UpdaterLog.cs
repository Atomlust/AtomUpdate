using System.Text;

namespace AtomUpdate.Updater.Logging;

public enum UpdaterLogLevel
{
    Trace = 0,
    Debug = 1,
    Information = 2,
    Warning = 3,
    Error = 4,
    None = 5
}

/// <summary>更新器日志（04 §4.14）：按天切分、保留策略、敏感信息过滤；控制台 + 文件双写。</summary>
public sealed class UpdaterLog
{
    private readonly object _gate = new();
    private readonly string? _directory;
    private readonly int _retentionDays;

    public UpdaterLogLevel Level { get; }

    public string? FilePath { get; }

    private UpdaterLog(UpdaterLogLevel level, string? directory, int retentionDays)
    {
        Level = level;
        _directory = directory;
        _retentionDays = retentionDays;

        if (directory is null)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(directory);
            ApplyRetention(directory, retentionDays);
            FilePath = Path.Combine(directory, $"updater-{DateTime.Now:yyyyMMdd}.log");
        }
        catch (Exception)
        {
            // 日志文件不可用不阻断安装；退化为仅控制台
            _directory = null;
        }
    }

    public static UpdaterLog Create(string level, string? pathOverride, int retentionDays)
    {
        var parsed = ParseLevel(level);
        var directory = parsed == UpdaterLogLevel.None ? null : pathOverride ?? UpdaterPaths.LogRoot;
        return new UpdaterLog(parsed, directory, retentionDays);
    }

    /// <summary>无文件输出的控制台日志（参数解析失败等日志初始化前的场景）。</summary>
    public static UpdaterLog ConsoleOnly(UpdaterLogLevel level = UpdaterLogLevel.Information) => new(level, null, 0);

    public static UpdaterLogLevel ParseLevel(string value) => value.ToLowerInvariant() switch
    {
        "trace" => UpdaterLogLevel.Trace,
        "debug" => UpdaterLogLevel.Debug,
        "information" or "info" => UpdaterLogLevel.Information,
        "warning" or "warn" => UpdaterLogLevel.Warning,
        "error" => UpdaterLogLevel.Error,
        "critical" => UpdaterLogLevel.Error,
        "none" => UpdaterLogLevel.None,
        _ => UpdaterLogLevel.Information
    };

    public void Debug(string message) => Write(UpdaterLogLevel.Debug, message, null);

    public void Information(string message) => Write(UpdaterLogLevel.Information, message, null);

    public void Warning(string message, Exception? exception = null) =>
        Write(UpdaterLogLevel.Warning, message, exception);

    public void Error(string message, Exception? exception = null) => Write(UpdaterLogLevel.Error, message, exception);

    public void Write(UpdaterLogLevel level, string message, Exception? exception)
    {
        if (level < Level)
        {
            return;
        }

        var line = $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level.ToString().ToUpperInvariant()}] " +
                   Sanitize(message);
        if (exception is not null)
        {
            line += $" | {exception.GetType().Name}: {Sanitize(exception.Message)}";
        }

        lock (_gate)
        {
            Console.WriteLine(line);
            if (FilePath is not null)
            {
                try
                {
                    File.AppendAllText(FilePath, line + Environment.NewLine, Encoding.UTF8);
                }
                catch (Exception)
                {
                    // 磁盘异常时保持控制台输出
                }
            }
        }
    }

    /// <summary>敏感信息过滤：屏蔽 key=value 形式中的值（apiKey/token/password 等）。</summary>
    public static string Sanitize(string text)
    {
        string[] sensitive = ["apikey=", "api_key=", "key=", "token=", "password=", "pwd="];
        var result = text;
        foreach (var marker in sensitive)
        {
            var from = 0;
            while (true)
            {
                var index = result.IndexOf(marker, from, StringComparison.OrdinalIgnoreCase);
                if (index < 0)
                {
                    break;
                }

                var valueStart = index + marker.Length;
                var valueEnd = valueStart;
                while (valueEnd < result.Length && result[valueEnd] is not ('&' or ' ' or '"' or '\''))
                {
                    valueEnd++;
                }

                result = string.Concat(result.AsSpan(0, valueStart), "***", result.AsSpan(valueEnd));
                from = valueStart + 3;
            }
        }

        return result;
    }

    private static void ApplyRetention(string directory, int retentionDays)
    {
        if (retentionDays <= 0)
        {
            return;
        }

        var threshold = DateTime.UtcNow.AddDays(-retentionDays);
        foreach (var file in Directory.EnumerateFiles(directory, "updater-*.log"))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(file) < threshold)
                {
                    File.Delete(file);
                }
            }
            catch (Exception)
            {
                // 清理失败不影响启动
            }
        }
    }
}
