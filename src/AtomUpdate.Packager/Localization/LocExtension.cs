namespace AtomUpdate.Packager.Localization;

/// <summary>XAML 本地化标记（docs/05 §4，Avalonia 12 duck-typing 写法，无需基类）：
/// {ext:L SelectApp} → Strings.T("SelectApp")，语言切换随窗口重建生效。</summary>
public sealed class LocExtension
{
    public LocExtension()
    {
    }

    public LocExtension(string key) => Key = key;

    public string Key { get; set; } = string.Empty;

    public string ProvideValue(IServiceProvider serviceProvider) => Strings.T(Key);
}
