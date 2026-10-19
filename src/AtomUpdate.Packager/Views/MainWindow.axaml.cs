using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Styling;
using AtomUpdate.Packager.Localization;
using AtomUpdate.Packager.Services;

namespace AtomUpdate.Packager.Views;

/// <summary>主窗口：仅承载语言/主题菜单与向导；切换语言重建窗口（草稿自动保存后立即恢复）。</summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void LanguageZhClick(object? sender, RoutedEventArgs e) => RestartWithLanguage(UiLanguage.Zh);

    private void LanguageEnClick(object? sender, RoutedEventArgs e) => RestartWithLanguage(UiLanguage.En);

    private void ThemeLightClick(object? sender, RoutedEventArgs e) => ThemeService.Apply(ThemeVariant.Light);

    private void ThemeDarkClick(object? sender, RoutedEventArgs e) => ThemeService.Apply(ThemeVariant.Dark);

    private static void RestartWithLanguage(UiLanguage language)
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop ||
            desktop.MainWindow is null)
        {
            return;
        }

        ThemeService.SetLanguage(language);
        var old = desktop.MainWindow;
        desktop.MainWindow = new MainWindow
        {
            DataContext = new ViewModels.WizardViewModel()
        };
        desktop.MainWindow.Show();
        old.Close();
    }
}
