namespace AtomUpdate.Core.Models;

public sealed class ReleaseNotes
{
    public IReadOnlyList<string> New { get; init; } = [];
    public IReadOnlyList<string> Improvements { get; init; } = [];
    public IReadOnlyList<string> Fixes { get; init; } = [];
    public IReadOnlyList<string> Notes { get; init; } = [];
}
