namespace AtomUpdate.Packager.Core;

/// <summary>打包工具错误码（docs/05 §14）：稳定字符串，随异常上抛，CLI/GUI 直接展示。</summary>
public static class PackagerErrorCodes
{
    public const string SourceNotFound = "PACKAGER_SOURCE_NOT_FOUND";
    public const string SourceEmpty = "PACKAGER_SOURCE_EMPTY";
    public const string UnsupportedPackageType = "PACKAGER_UNSUPPORTED_PACKAGE_TYPE";
    public const string IsccNotFound = "PACKAGER_ISCC_NOT_FOUND";
    public const string IsccFailed = "PACKAGER_ISCC_FAILED";
    public const string DeltaBaselineInvalid = "PACKAGER_DELTA_BASELINE_INVALID";
    public const string UploadFailed = "PACKAGER_UPLOAD_FAILED";
    public const string ApiFailed = "PACKAGER_API_FAILED";
    public const string ReleaseExists = "PACKAGER_RELEASE_EXISTS";
}

/// <summary>打包工具统一异常：错误码 + 消息（可选内层异常），CLI/GUI 捕获后按码映射展示与退出码。</summary>
public sealed class PackagerException(string code, string message, Exception? inner = null)
    : Exception(message, inner)
{
    public string Code { get; } = code;
}
