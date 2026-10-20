namespace AtomUpdate.Packager.Core;

/// <summary>打包工具工作目录布局（docs/05 §12/§16）：{work}/drafts（packs 由打包输出目录决定）。</summary>
public sealed class PackagerPaths
{
    public PackagerPaths(string workDirectory)
    {
        Root = Path.GetFullPath(workDirectory);
        DraftsDirectory = Path.Combine(Root, "drafts");
    }

    public string Root { get; }

    public string DraftsDirectory { get; }

    public void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(DraftsDirectory);
    }
}
