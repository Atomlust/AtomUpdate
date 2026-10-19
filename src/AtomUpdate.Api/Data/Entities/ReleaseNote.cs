namespace AtomUpdate.Api.Data.Entities;

public sealed class ReleaseNote : ITenantScoped
{
    public long Id { get; set; }
    public long TenantId { get; set; }
    public long ReleaseId { get; set; }
    public string Language { get; set; } = "en";
    public List<string> New { get; set; } = [];
    public List<string> Improvements { get; set; } = [];
    public List<string> Fixes { get; set; } = [];
    public List<string> Notes { get; set; } = [];
    public string? RawMarkdown { get; set; }

    public Release Release { get; set; } = null!;
}
