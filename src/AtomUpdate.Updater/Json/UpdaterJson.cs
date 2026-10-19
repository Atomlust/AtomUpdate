using System.Text.Json.Serialization;
using AtomUpdate.Updater.Configuration;
using AtomUpdate.Updater.Report;
using AtomUpdate.Updater.Replace;
using AtomUpdate.Updater.SelfUpdate;
using AtomUpdate.Updater.State;

namespace AtomUpdate.Updater.Json;

/// <summary>更新器 JSON 源生成上下文（Q12 AOT：全部序列化走 source-gen，禁反射）。</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = true)]
[JsonSerializable(typeof(UpdaterTaskFile))]
[JsonSerializable(typeof(UpdaterState))]
[JsonSerializable(typeof(InstallReport))]
[JsonSerializable(typeof(SelfUpdateManifest))]
[JsonSerializable(typeof(DeltaManifest))]
[JsonSerializable(typeof(BackupManifest))]
[JsonSerializable(typeof(List<string>))]
public partial class UpdaterJsonContext : JsonSerializerContext;
