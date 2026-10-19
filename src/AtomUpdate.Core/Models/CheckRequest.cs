using AtomUpdate.Core.Enums;

namespace AtomUpdate.Core.Models;

public sealed class CheckRequest
{
    public required string ProjectKey { get; init; }
    public required string AppId { get; init; }
    public required string CurrentVersion { get; init; }
    public int? BuildNumber { get; init; }
    public required UpdatePlatform Platform { get; init; }
    public required UpdateArch Arch { get; init; }
    public UpdateChannel? Channel { get; init; }
    public required string InstallationId { get; init; }
    public string? MachineFingerprint { get; init; }
    public int PolicyVersion { get; init; }
}
