using AtomUpdate.Core.Enums;
using AtomUpdate.Packager.Core.Models;

namespace AtomUpdate.Packager.Core.Packaging;

/// <summary>打包器接口：按包类型分发（MVP 实现 zip 全量与 Inno exe）。</summary>
public interface IPackager
{
    bool Supports(PackageType packageType);

    Task<PackageResult> PackageAsync(PackageContext context, CancellationToken cancellationToken = default);
}
