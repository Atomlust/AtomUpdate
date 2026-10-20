namespace AtomUpdate.Packager.Cli;

/// <summary>解析后的命令行（docs/05 §13）：命令名 + 选项多值映射（--arch 可重复）+ 解析错误。</summary>
public sealed class ParsedCli
{
    public required string? Command { get; init; }
    public bool HelpRequested { get; init; }
    public required IReadOnlyList<string> Errors { get; init; }
    public required IReadOnlyDictionary<string, IReadOnlyList<string>> Options { get; init; }

    public bool Has(string name) => Options.ContainsKey(name);

    public string? Value(string name) =>
        Options.TryGetValue(name, out var values) && values.Count > 0 ? values[0] : null;

    public IReadOnlyList<string> Values(string name) =>
        Options.TryGetValue(name, out var values) ? values : [];

    /// <summary>选项值 → 环境变量回退 → null。</summary>
    public string? ValueOrEnv(string name, string? envName) =>
        Value(name) ?? (envName is not null ? Environment.GetEnvironmentVariable(envName) : null);
}
