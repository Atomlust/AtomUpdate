using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using AtomUpdate.Packager.ViewModels;

namespace AtomUpdate.Packager.Views;

/// <summary>四步向导视图（docs/05 §4）：交互仅目录/文件选择，状态全在 WizardViewModel。</summary>
public partial class WizardView : UserControl
{
    public WizardView()
    {
        InitializeComponent();
    }

    private WizardViewModel? Vm => DataContext as WizardViewModel;

    private async void BrowseDirectoryClick(object? sender, RoutedEventArgs e)
    {
        if (Vm is null || sender is not Control { Tag: string target })
        {
            return;
        }

        var folders = await TopLevel.GetTopLevel(this)!.StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions { Title = target, AllowMultiple = false });
        if (folders.Count != 1 || folders[0].TryGetLocalPath() is not { } path)
        {
            return;
        }

        switch (target)
        {
            case "source": Vm.SourceDirectory = path; break;
            case "baseline": Vm.BaselineDirectory = path; break;
            case "output": Vm.OutputDirectory = path; break;
        }
    }
}
