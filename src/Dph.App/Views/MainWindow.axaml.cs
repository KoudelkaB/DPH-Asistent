using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Dph.App.ViewModels;

namespace Dph.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Opened += OnOpened;
        Closing += OnClosing;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.PickExportDirectoryAsync = PickExportDirectoryAsync;
        }
    }

    private async Task<string?> PickExportDirectoryAsync(string? currentDirectory)
    {
        IStorageFolder? startLocation = null;
        if (!string.IsNullOrWhiteSpace(currentDirectory) && Directory.Exists(currentDirectory))
        {
            startLocation = await StorageProvider.TryGetFolderFromPathAsync(currentDirectory);
        }

        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Vyber složku pro export XML",
            AllowMultiple = false,
            SuggestedStartLocation = startLocation
        });

        return folders.Count == 0 ? null : folders[0].Path.LocalPath;
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        var size = await viewModel.LoadWindowSizeAsync();
        if (size is null)
        {
            return;
        }

        Width = Math.Max(MinWidth, size.Value.Width);
        Height = Math.Max(MinHeight, size.Value.Height);
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        await viewModel.SaveWindowSizeAsync(Bounds.Width, Bounds.Height);
    }
}
