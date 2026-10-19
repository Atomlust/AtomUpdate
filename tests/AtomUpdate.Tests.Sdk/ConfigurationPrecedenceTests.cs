using AtomUpdate.Configuration;
using AtomUpdate.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AtomUpdate.Tests.Sdk;

/// <summary>配置五级优先级（04 §3.7）：内置默认 → appsettings → ATOMUPDATE_ 环境变量 → 代码覆盖 → 服务端策略。</summary>
public class ConfigurationPrecedenceTests : IDisposable
{
    private const string EnvPrefix = "ATOMUPDATE_";

    private readonly List<string> _setEnvKeys = [];

    private void SetEnv(string suffix, string value)
    {
        var key = EnvPrefix + suffix;
        Environment.SetEnvironmentVariable(key, value);
        _setEnvKeys.Add(key);
    }

    private AtomUpdateOptions Resolve(Action<AtomUpdateOptions>? configure = null,
        IDictionary<string, string?>? settings = null)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings ?? new Dictionary<string, string?>())
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton(configuration);
        services.AddAtomUpdate(configure);

        return services.BuildServiceProvider().GetRequiredService<IOptions<AtomUpdateOptions>>().Value;
    }

    [Fact]
    public void Defaults_ApplyWhenNothingConfigured()
    {
        var options = Resolve(configure: o =>
        {
            o.ServerUrl = "http://localhost:5062";
            o.ProjectKey = "p";
            o.AppId = "a";
            o.CurrentVersion = "1.0.0";
        });

        Assert.Equal(TimeSpan.FromHours(6), options.Polling.Interval);
        Assert.Equal(60, options.Download.StallTimeoutSeconds);
        Assert.False(options.Verify.RequireSignature);
        Assert.Equal(3, options.Download.RetryCount);
    }

    [Fact]
    public void Settings_BindOverDefaults()
    {
        var options = Resolve(settings: new Dictionary<string, string?>
        {
            ["AtomUpdate:ServerUrl"] = "http://settings",
            ["AtomUpdate:ProjectKey"] = "p",
            ["AtomUpdate:AppId"] = "a",
            ["AtomUpdate:CurrentVersion"] = "1.0.0",
            ["AtomUpdate:Channel"] = "beta",
            ["AtomUpdate:Polling:Interval"] = "00:30:00"
        });

        Assert.Equal("http://settings", options.ServerUrl);
        Assert.Equal(Core.Enums.UpdateChannel.Beta, options.Channel);
        Assert.Equal(TimeSpan.FromMinutes(30), options.Polling.Interval);
    }

    [Fact]
    public void Environment_BindsFlatAndNestedKeys()
    {
        SetEnv("SERVERURL", "http://from-env");
        SetEnv("POLLING__INTERVAL", "00:15:00");

        var options = Resolve(settings: new Dictionary<string, string?>
        {
            ["AtomUpdate:ServerUrl"] = "http://settings",
            ["AtomUpdate:ProjectKey"] = "p",
            ["AtomUpdate:AppId"] = "a",
            ["AtomUpdate:CurrentVersion"] = "1.0.0",
            ["AtomUpdate:Polling:Interval"] = "00:30:00"
        });

        Assert.Equal("http://from-env", options.ServerUrl);
        Assert.Equal(TimeSpan.FromMinutes(15), options.Polling.Interval);
    }

    [Fact]
    public void CodeOverride_WinsOverEnvironment()
    {
        SetEnv("SERVERURL", "http://from-env");

        var options = Resolve(configure: o => o.ServerUrl = "http://from-code",
            settings: new Dictionary<string, string?>
            {
                ["AtomUpdate:ProjectKey"] = "p",
                ["AtomUpdate:AppId"] = "a",
                ["AtomUpdate:CurrentVersion"] = "1.0.0"
            });

        Assert.Equal("http://from-code", options.ServerUrl);
    }

    /// <summary>必填项缺失时报可读的选项校验错误，而不是 HttpClient 里的 NullReferenceException（E2E 回归）。</summary>
    [Fact]
    public void MissingRequiredSettings_ThrowsReadableError()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddAtomUpdate(o =>
        {
            o.ProjectKey = "p";
            o.AppId = "a";
            o.CurrentVersion = "1.0.0";
        });
        using var provider = services.BuildServiceProvider();

        var exception = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<AtomUpdateOptions>>().Value);

        Assert.Contains("ServerUrl", exception.Message);
    }

    /// <summary>检查/上报客户端共用 ServerUrl：相对路由要能拼成绝对 URI；上报客户端漏配 BaseAddress 时 E2E 曾抛 InvalidOperationException。</summary>
    [Fact]
    public void NamedClients_UseServerUrlAsBaseAddress()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["AtomUpdate:ServerUrl"] = "http://localhost:5062",
                ["AtomUpdate:ProjectKey"] = "p",
                ["AtomUpdate:AppId"] = "a",
                ["AtomUpdate:CurrentVersion"] = "1.0.0"
            }).Build());
        services.AddAtomUpdate();
        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IHttpClientFactory>();

        Assert.Equal(new Uri("http://localhost:5062/"),
            factory.CreateClient(AtomUpdate.Check.AtomUpdateHttp.ClientName).BaseAddress);
        Assert.Equal(new Uri("http://localhost:5062/"),
            factory.CreateClient(AtomUpdate.Check.AtomUpdateHttp.ReportClientName).BaseAddress);
        // 下载客户端不带 BaseAddress：包地址由服务端下发，必须是绝对 URL
        Assert.Null(factory.CreateClient(AtomUpdate.Check.AtomUpdateHttp.DownloadClientName).BaseAddress);
    }

    public void Dispose()
    {
        foreach (var key in _setEnvKeys)
        {
            Environment.SetEnvironmentVariable(key, null);
        }
    }
}
