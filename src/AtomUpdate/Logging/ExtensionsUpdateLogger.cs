using AtomUpdate.Abstractions.Interfaces;
using Microsoft.Extensions.Logging;

namespace AtomUpdate.Logging;

/// <summary>M.E.Logging 桥接实现（04 §3.3）。</summary>
public sealed class ExtensionsUpdateLogger(ILoggerFactory factory) : IUpdateLogger
{
    private readonly ILogger _logger = factory.CreateLogger("AtomUpdate");

    public void Debug(string message) => _logger.LogDebug("{Message}", message);

    public void Information(string message) => _logger.LogInformation("{Message}", message);

    public void Warning(string message, Exception? exception = null) =>
        _logger.LogWarning(exception, "{Message}", message);

    public void Error(string message, Exception? exception = null) =>
        _logger.LogError(exception, "{Message}", message);
}
