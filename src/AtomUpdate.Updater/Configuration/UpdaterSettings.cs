using System.Text.Json;
using System.Text.Json.Serialization;

namespace AtomUpdate.Updater.Configuration;

/// <summary>更新器配置节（04 §4.13）。五层合并：内置默认 → 安装目录 updater.default.json → --config → ATOMUPDATE_UPDATER_CONFIG 环境变量指向的文件 → 命令行。
/// verification 节仅取前两层（防同机攻击者经运行参数注入降级校验，2026-10-06 确认）。</summary>
public sealed class UpdaterSettings
{
    public UpdaterSection Updater { get; set; } = new();

    public VerificationSection Verification { get; set; } = new();

    public SelfUpdateSection SelfUpdate { get; set; } = new();

    public ElevationSection Elevation { get; set; } = new();

    public InstallSection Install { get; set; } = new();

    public RollbackSection Rollback { get; set; } = new();

    public RestartSection Restart { get; set; } = new();

    public ReportSection Report { get; set; } = new();

    public NetworkSection Network { get; set; } = new();
}

public sealed class UpdaterSection
{
    public string LogLevel { get; set; } = "information";

    /// <summary>null = 平台默认目录（04 §4.14）。</summary>
    public string? LogPath { get; set; }

    public int LogRetentionDays { get; set; } = 7;

    /// <summary>按 appId 命名互斥（Q14）；false 关闭互斥（测试/调试用）。</summary>
    public bool GlobalMutex { get; set; } = true;

    /// <summary>互斥等待秒数（0 = 不等待，直接 0x1001）。</summary>
    public int MutexTimeoutSeconds { get; set; }

    public bool DryRun { get; set; }
}

/// <summary>校验节（仅信任内置默认 + 安装目录 updater.default.json 两层）。</summary>
public sealed class VerificationSection
{
    /// <summary>默认不验签；置 true 后包未签名即失败。SHA256 始终强制（提供 --sha256 时）。</summary>
    public bool RequireSignature { get; set; }

    /// <summary>内置公钥集（keyId → PEM/SubjectPublicKeyInfo 的 RSA 公钥）。仅随客户端分发内嵌，不从 Api 动态获取。</summary>
    public Dictionary<string, string> PublicKeys { get; set; } = new();
}

public sealed class SelfUpdateSection
{
    public bool Enabled { get; set; } = true;

    /// <summary>更新器自升级模式（当前仅实现 dualProcess）。</summary>
    public string Mode { get; set; } = "dualProcess";

    public string? ServerUrl { get; set; }

    /// <summary>安装前检查自升级；失败（断网/Api 不可达）记日志跳过，继续原任务（Q16）。</summary>
    public bool CheckBeforeInstall { get; set; } = true;

    /// <summary>自升级清单地址（MVP 内部约定：GET 返回 {version,url,sha256}；独立分发契约二阶段再定，Q13）。空 = 跳过自升级检查。</summary>
    public string? ManifestUrl { get; set; }
}

public sealed class ElevationSection
{
    /// <summary>onDemand / always / never。</summary>
    public string Mode { get; set; } = "onDemand";

    /// <summary>unlimited 或数字（本进程内允许的连续提权重试次数；跨次授权循环由 SDK 负责）。</summary>
    public string Retry { get; set; } = "unlimited";

    public bool AllowExit { get; set; } = true;

    public bool SkipIfNotNeeded { get; set; } = true;
}

public sealed class InstallSection
{
    /// <summary>merge（只覆盖包内文件）/ replace（另删包外文件，等价 merge + deleteOrphans）。</summary>
    public string Strategy { get; set; } = "merge";

    public List<string> Exclude { get; set; } = [];

    public List<string> Preserve { get; set; } = [];

    public bool DeleteOrphans { get; set; }

    public bool BackupBeforeReplace { get; set; } = true;

    /// <summary>备份目录（null = 平台默认 backup/{appId}/{timestamp}/）。</summary>
    public string? BackupPath { get; set; }

    public int BackupRetentionDays { get; set; } = 3;

    /// <summary>占用冲突重试次数（Windows 文件被占用），超出 → 0x1006。</summary>
    public int ReplaceRetryCount { get; set; } = 3;

    public int ReplaceRetryDelayMs { get; set; } = 250;

    /// <summary>钩子映射：阶段 → 命令（单个字符串或字符串数组）。阶段见 04 §4.8。</summary>
    public Dictionary<string, HookCommandList> Hooks { get; set; } = new();

    public int HookTimeoutSeconds { get; set; } = 60;

    /// <summary>abort（中断并回滚）/ continue。</summary>
    public string HookFailPolicy { get; set; } = "abort";

    /// <summary>exe/msi 安装参数（任务包元数据 silentArgs/installArgs 优先，其次本节，再 --install-args）。</summary>
    public string? InstallArgs { get; set; }
}

public sealed class RollbackSection
{
    public bool Enabled { get; set; } = true;

    public bool OnFailure { get; set; } = true;

    /// <summary>成功时是否保留备份（默认 false：成功后删除备份）。</summary>
    public bool KeepBackupOnSuccess { get; set; }
}

public sealed class RestartSection
{
    public bool Enabled { get; set; } = true;

    public string Args { get; set; } = "--skip-update-check";

    public int DelayMs { get; set; } = 500;

    /// <summary>等待主程序退出的秒数（04 §5.2，超时强制继续）。</summary>
    public int WaitForExitMs { get; set; } = 5000;
}

public sealed class ReportSection
{
    public bool Enabled { get; set; } = true;

    /// <summary>null = 使用任务配置中的 serverUrl。</summary>
    public string? ServerUrl { get; set; }

    public int TimeoutSeconds { get; set; } = 15;

    public int RetryCount { get; set; } = 3;

    /// <summary>失败落状态文件 reported=false，等 SDK 启动补报。</summary>
    public bool FallbackToLocal { get; set; } = true;
}

public sealed class NetworkSection
{
    public string? Proxy { get; set; }

    public int TimeoutSeconds { get; set; } = 30;

    public int RetryCount { get; set; } = 3;
}

/// <summary>钩子命令列表：JSON 可写单个字符串或字符串数组。</summary>
[JsonConverter(typeof(HookCommandListConverter))]
public sealed class HookCommandList : List<string>
{
    public HookCommandList()
    {
    }

    public HookCommandList(IEnumerable<string> items) : base(items)
    {
    }
}

/// <summary>string | string[] → List&lt;string&gt;（AOT 友好手写转换器）。</summary>
public sealed class HookCommandListConverter : JsonConverter<HookCommandList>
{
    public override HookCommandList Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var result = new HookCommandList();
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
                result.Add(reader.GetString()!);
                break;
            case JsonTokenType.StartArray:
                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                {
                    if (reader.TokenType == JsonTokenType.String)
                    {
                        result.Add(reader.GetString()!);
                    }
                    else
                    {
                        throw new JsonException("钩子命令数组只接受字符串");
                    }
                }

                break;
            default:
                throw new JsonException("钩子命令必须是字符串或字符串数组");
        }

        return result;
    }

    public override void Write(Utf8JsonWriter writer, HookCommandList value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var item in value)
        {
            writer.WriteStringValue(item);
        }

        writer.WriteEndArray();
    }
}
