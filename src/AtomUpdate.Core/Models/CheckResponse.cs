namespace AtomUpdate.Core.Models;

public sealed class CheckResponse
{
    public required bool HasUpdate { get; init; }
    public required string LatestVersion { get; init; }
    public bool? ForceUpdate { get; init; }
    public string? MinSupportedVersion { get; init; }
    public bool? PolicyUnchanged { get; init; }
    public UpdatePolicy? Policy { get; init; }
    public PackageAsset? Package { get; init; }
    public PackageAsset? FullPackage { get; init; }
    public ReleaseNotes? ReleaseNotes { get; init; }
}
