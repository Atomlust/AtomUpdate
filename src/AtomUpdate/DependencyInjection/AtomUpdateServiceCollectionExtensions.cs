using System.Collections;
using System.Net;
using AtomUpdate.Abstractions.Interfaces;
using AtomUpdate.Check;
using AtomUpdate.Configuration;
using AtomUpdate.Download;
using AtomUpdate.Install;
using AtomUpdate.Internal;
using AtomUpdate.Logging;
using AtomUpdate.Notify;
using AtomUpdate.Policy;
using AtomUpdate.Polling;
using AtomUpdate.Report;
using AtomUpdate.State;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using AtomUpdate.Platform;

namespace AtomUpdate.DependencyInjection;

/// <summary>AddAtomUpdate 服务装配（04 §3.1/§3.9）。配置五级优先级：内置默认 → appsettings.json → ATOMUPDATE_ 环境变量 → 代码覆盖 → 服务端下发策略（仅策略类字段）。</summary>
public static class AtomUpdateServiceCollectionExtensions
{
    public static IServiceCollection AddAtomUpdate(this IServiceCollection services,
        Action<AtomUpdateOptions>? configure = null)
    {
        var environmentConfiguration = BuildEnvironmentConfiguration();
        services.AddOptions<AtomUpdateOptions>()
            .BindConfiguration(AtomUpdateOptions.SectionName)
            .Configure(options =>
            {
                // 五级顺序：内置默认 → appsettings（BindConfiguration）→ ATOMUPDATE_ 环境变量 → 代码覆盖
                environmentConfiguration.Bind(options);
                configure?.Invoke(options);
            });

        // 具体实现直接依赖 AtomUpdateOptions 值对象
        services.TryAddSingleton(sp => sp.GetRequiredService<IOptions<AtomUpdateOptions>>().Value);

        // 必填项校验：配置缺失时在首次解析选项处报可读错误，而不是在 HttpClient 里抛 NullReferenceException
        services.AddOptions<AtomUpdateOptions>()
            .Validate(options =>
                Uri.TryCreate(options.ServerUrl?.TrimEnd('/') + "/", UriKind.Absolute, out var serverUri)
                && serverUri is { Scheme: "http" or "https" }
                && !string.IsNullOrWhiteSpace(options.ProjectKey)
                && !string.IsNullOrWhiteSpace(options.AppId)
                && !string.IsNullOrWhiteSpace(options.CurrentVersion),
                "AtomUpdate 配置无效：ServerUrl 必须是 http/https 绝对地址，ProjectKey/AppId/CurrentVersion 不能为空");

        services.TryAddSingleton<IPlatformInfo, PlatformInfo>();
        services.TryAddSingleton<IUpdateLogger>(sp =>
            new ExtensionsUpdateLogger(
                sp.GetService<ILoggerFactory>() ?? NullLoggerFactory.Instance));

        // 状态/策略缓存：按 appId 隔离
        services.TryAddSingleton<IStateStore>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<AtomUpdateOptions>>().Value;
            return new FileStateStore(options.AppId);
        });
        services.TryAddSingleton<PolicyCache>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<AtomUpdateOptions>>().Value;
            return new PolicyCache(options.AppId, options);
        });
        services.TryAddSingleton<IPolicyProvider, PolicyProvider>();

        // 分层服务（全部可替换，04 §3.9）
        services.TryAddSingleton<IUpdateChecker, UpdateChecker>();
        services.TryAddSingleton<IUpdateDownloader, UpdateDownloader>();
        services.TryAddSingleton<IUpdateInstaller, UpdateInstaller>();
        services.TryAddSingleton<IUpdateNotifier, ConsoleNotifier>();
        services.TryAddSingleton<IUpdateReporter>(sp => new UpdateReporter(
            sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<AtomUpdateOptions>(),
            sp.GetRequiredService<IUpdateLogger>()));

        services.TryAddSingleton<AtomUpdater>();
        services.TryAddSingleton<IAtomUpdater>(sp => sp.GetRequiredService<AtomUpdater>());
        services.AddHostedService<PollingService>();
        services.AddHostedService<StartupReportService>();

        ConfigureHttpClients(services);
        return services;
    }

    private const string EnvironmentPrefix = "ATOMUPDATE_";

    /// <summary>ATOMUPDATE_ 环境变量层（04 §3.7）：ATOMUPDATE_SERVERURL、嵌套用双下划线（ATOMUPDATE_POLLING__INTERVAL）。</summary>
    private static IConfiguration BuildEnvironmentConfiguration()
    {
        var values = new List<KeyValuePair<string, string?>>();
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            var name = (string)entry.Key;
            if (!name.StartsWith(EnvironmentPrefix, StringComparison.OrdinalIgnoreCase) ||
                name.Length <= EnvironmentPrefix.Length)
            {
                continue;
            }

            values.Add(new KeyValuePair<string, string?>(
                name[EnvironmentPrefix.Length..].Replace("__", ":"), (string?)entry.Value));
        }

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private static void ConfigureHttpClients(IServiceCollection services)
    {
        services.AddHttpClient(AtomUpdateHttp.ClientName, (sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<AtomUpdateOptions>>().Value;
            client.BaseAddress = new Uri(options.ServerUrl.TrimEnd('/') + "/");
            client.Timeout = options.Network.Timeout;

            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                options.Network.UserAgent ?? BuildDefaultUserAgent(sp));
        });

        // 下载：超时无限（由无进展超时控制）；代理仅显式配置时启用
        services.AddHttpClient(AtomUpdateHttp.DownloadClientName, (sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<AtomUpdateOptions>>().Value;
            client.Timeout = System.Threading.Timeout.InfiniteTimeSpan;
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                options.Network.UserAgent ?? BuildDefaultUserAgent(sp));
        }).ConfigurePrimaryHttpMessageHandler(sp =>
        {
            var options = sp.GetRequiredService<IOptions<AtomUpdateOptions>>().Value;
            var useProxy = !string.IsNullOrEmpty(options.Network.Proxy);
            return new HttpClientHandler
            {
                Proxy = useProxy ? new WebProxy(options.Network.Proxy!) : null,
                UseProxy = useProxy,
                UseDefaultCredentials = options.Network.UseDefaultCredentials,
                AutomaticDecompression = DecompressionMethods.All
            };
        });

        // 上报：15 秒超时（04 §3.10）
        services.AddHttpClient(AtomUpdateHttp.ReportClientName, (sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<AtomUpdateOptions>>().Value;
            client.BaseAddress = new Uri(options.ServerUrl.TrimEnd('/') + "/");
            client.Timeout = options.Report.Timeout;

            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                options.Network.UserAgent ?? BuildDefaultUserAgent(sp));
        });
    }

    private static string BuildDefaultUserAgent(IServiceProvider sp)
    {
        var platform = sp.GetRequiredService<IPlatformInfo>();
        return $"AtomUpdate/0.1.0 ({platform.Platform.ToString().ToLowerInvariant()}; {platform.Arch.ToString().ToLowerInvariant()})";
    }
}
