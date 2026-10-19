using Avalonia;
using Avalonia.Styling;
using AtomUpdate.Packager.Localization;

namespace AtomUpdate.Packager.Services;

/// <summary>亮暗主题切换（docs/05 §13 MVP 结构）：直接改 Application.RequestedThemeVariant。</summary>
public static class ThemeService
{
    public static ThemeVariant Current { get; private set; } = ThemeVariant.Default;

    public static void Apply(ThemeVariant variant)
    {
        Current = variant;
        if (Application.Current is not null)
        {
            Application.Current.RequestedThemeVariant = variant;
        }
    }

    public static void ApplyLight() => Apply(ThemeVariant.Light);

    public static void ApplyDark() => Apply(ThemeVariant.Dark);

    public static void SetLanguage(UiLanguage language) => Strings.Language = language;
}
