namespace AtomUpdate.Packager.Cli;

/// <summary>CLI 退出码（docs/05 §16）：0 成功 / 1 参数 / 2 配置 / 3 打包 / 5 上传 / 6 推送 / 7 发布 / 8 未知。</summary>
public static class PackagerExitCodes
{
    public const int Success = 0;
    public const int Arguments = 1;
    public const int Configuration = 2;
    public const int Packaging = 3;
    public const int Signing = 4;
    public const int Upload = 5;
    public const int Push = 6;
    public const int Publish = 7;
    public const int Unknown = 8;
}
