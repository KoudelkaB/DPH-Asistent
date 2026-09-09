using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Dph.App.ViewModels;
using Dph.Core.Isds;

namespace Dph.App.Views;

public partial class MainWindow : Window
{
    private bool _isSyncingCounterpartySelection;
    private bool _closeSavesCompleted;

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
            viewModel.PickDatabaseBackupTargetAsync = PickDatabaseBackupTargetAsync;
            viewModel.PickDatabaseBackupSourceAsync = PickDatabaseBackupSourceAsync;
            viewModel.ConfirmAsync = ConfirmAsync;
            viewModel.ConfirmReexportAsync = ConfirmReexportAsync;
            viewModel.RequestTextAsync = RequestTextAsync;
            viewModel.CopyToClipboardAsync = CopyToClipboardAsync;
            viewModel.RequestIsdsCredentialsAsync = RequestIsdsCredentialsAsync;
            viewModel.ShowReportAsync = ShowReportAsync;
            viewModel.Issuing.PickPdfTargetAsync = PickPdfTargetAsync;
            viewModel.Issuing.ConfirmAsync = ConfirmAsync;
        }
    }

    private async Task<string?> PickPdfTargetAsync(string currentDirectory, string defaultFileName)
    {
        var startLocation = await TryGetStartLocationAsync(currentDirectory);
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Uložit fakturu jako PDF",
            SuggestedStartLocation = startLocation,
            SuggestedFileName = defaultFileName,
            DefaultExtension = "pdf",
            FileTypeChoices =
            [
                new FilePickerFileType("PDF dokument")
                {
                    Patterns = ["*.pdf"]
                }
            ]
        });

        return file?.Path.LocalPath;
    }

    private async Task CopyToClipboardAsync(string text)
    {
        var clipboard = GetTopLevel(this)?.Clipboard;
        if (clipboard is not null)
        {
            await clipboard.SetTextAsync(text);
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

    private async Task<string?> PickDatabaseBackupTargetAsync(string currentDirectory, string defaultFileName)
    {
        var startLocation = await TryGetStartLocationAsync(currentDirectory);
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Vyber soubor pro zálohu DB",
            SuggestedStartLocation = startLocation,
            SuggestedFileName = defaultFileName,
            DefaultExtension = "sqlite",
            FileTypeChoices =
            [
                new FilePickerFileType("SQLite databáze")
                {
                    Patterns = ["*.sqlite", "*.db"]
                }
            ]
        });

        return file?.Path.LocalPath;
    }

    private async Task<string?> PickDatabaseBackupSourceAsync(string currentDirectory)
    {
        var startLocation = await TryGetStartLocationAsync(currentDirectory);
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Vyber zálohu DB",
            AllowMultiple = false,
            SuggestedStartLocation = startLocation,
            FileTypeFilter =
            [
                new FilePickerFileType("SQLite databáze")
                {
                    Patterns = ["*.sqlite", "*.db"]
                },
                FilePickerFileTypes.All
            ]
        });

        return files.Count == 0 ? null : files[0].Path.LocalPath;
    }

    private async Task<IStorageFolder?> TryGetStartLocationAsync(string? currentDirectory)
    {
        return !string.IsNullOrWhiteSpace(currentDirectory) && Directory.Exists(currentDirectory)
            ? await StorageProvider.TryGetFolderFromPathAsync(currentDirectory)
            : null;
    }

    private async Task<bool> ConfirmAsync(string title, string message)
    {
        var cancelButton = new Button
        {
            Content = "Zrušit",
            MinWidth = 92
        };
        var continueButton = new Button
        {
            Content = "Pokračovat",
            MinWidth = 110,
            Background = Brushes.Black,
            Foreground = Brushes.White
        };
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Children = { cancelButton, continueButton }
        };
        Grid.SetRow(buttons, 1);

        var text = new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Content = new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap
            }
        };

        var content = new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto"),
            Margin = new Thickness(18),
            RowSpacing = 12,
            Children = { text, buttons }
        };

        var dialog = new Window
        {
            Title = title,
            Width = 520,
            // Delší potvrzení (např. seznam odesílaných podání) by se do pevné výšky nevešlo.
            Height = DialogHeight(message),
            MinWidth = 460,
            MinHeight = 180,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = true,
            Content = content
        };

        cancelButton.Click += (_, _) => dialog.Close(false);
        continueButton.Click += (_, _) => dialog.Close(true);

        return await dialog.ShowDialog<bool>(this);
    }

    // Odhad výšky podle počtu řádků; ScrollViewer dorovná, když se odhad netrefí.
    private static double DialogHeight(string message)
    {
        var lines = message.Split('\n').Sum(line => 1 + line.Length / 70);
        return Math.Clamp(140 + lines * 22, 190, 560);
    }

    private async Task ShowReportAsync(string title, string message)
    {
        var closeButton = new Button
        {
            Content = "Zavřít",
            MinWidth = 110,
            HorizontalAlignment = HorizontalAlignment.Right,
            Background = Brushes.Black,
            Foreground = Brushes.White
        };
        Grid.SetRow(closeButton, 1);

        var text = new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Content = new SelectableTextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap
            }
        };

        var dialog = new Window
        {
            Title = title,
            Width = 620,
            Height = DialogHeight(message),
            MinWidth = 480,
            MinHeight = 200,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new Grid
            {
                RowDefinitions = new RowDefinitions("*,Auto"),
                Margin = new Thickness(18),
                RowSpacing = 12,
                Children = { text, closeButton }
            }
        };

        closeButton.Click += (_, _) => dialog.Close();
        await dialog.ShowDialog(this);
    }

    private async Task<IsdsCredentials?> RequestIsdsCredentialsAsync(string title, string message, string defaultLogin)
    {
        var loginBox = new TextBox { Text = defaultLogin, PlaceholderText = "Uživatelské jméno" };
        var passwordBox = new TextBox { PasswordChar = '•', PlaceholderText = "Heslo" };
        var cancelButton = new Button { Content = "Zrušit", MinWidth = 92 };
        var continueButton = new Button
        {
            Content = "Přihlásit a odeslat",
            MinWidth = 150,
            Background = Brushes.Black,
            Foreground = Brushes.White,
            IsEnabled = false
        };

        void UpdateEnabled(object? sender, EventArgs e)
            => continueButton.IsEnabled = !string.IsNullOrWhiteSpace(loginBox.Text) && !string.IsNullOrEmpty(passwordBox.Text);

        loginBox.TextChanged += UpdateEnabled;
        passwordBox.TextChanged += UpdateEnabled;

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Children = { cancelButton, continueButton }
        };

        var fields = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("110,*"),
            RowDefinitions = new RowDefinitions("Auto,Auto"),
            ColumnSpacing = 8,
            RowSpacing = 8,
            Children =
            {
                new TextBlock { Text = "Jméno", VerticalAlignment = VerticalAlignment.Center },
                loginBox,
                new TextBlock { Text = "Heslo", VerticalAlignment = VerticalAlignment.Center },
                passwordBox
            }
        };
        Grid.SetColumn(loginBox, 1);
        Grid.SetRow(passwordBox, 1);
        Grid.SetColumn(passwordBox, 1);
        Grid.SetRow(fields.Children[2], 1);

        var content = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"),
            Margin = new Thickness(18),
            RowSpacing = 12,
            Children =
            {
                new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                fields,
                new TextBlock
                {
                    Text = "Schránky zabezpečené jednorázovým heslem (SMS/TOTP) nebo přihlášením certifikátem aplikace nepodporuje.",
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = Brushes.Gray
                },
                buttons
            }
        };
        Grid.SetRow(fields, 1);
        Grid.SetRow((Control)content.Children[2], 2);
        Grid.SetRow(buttons, 3);

        var dialog = new Window
        {
            Title = title,
            Width = 560,
            Height = 320,
            MinWidth = 520,
            MinHeight = 300,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Content = content
        };

        cancelButton.Click += (_, _) => dialog.Close(null);
        continueButton.Click += (_, _) => dialog.Close(new IsdsCredentials(loginBox.Text?.Trim() ?? "", passwordBox.Text ?? ""));

        var result = await dialog.ShowDialog<IsdsCredentials?>(this);
        // Heslo drží jen návratová hodnota; textové pole ať v paměti nezůstává vyplněné.
        passwordBox.Text = "";
        return result;
    }

    private async Task<string?> RequestTextAsync(string title, string message, string initialValue)
    {
        var textBox = new TextBox
        {
            Text = initialValue,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 96
        };
        var cancelButton = new Button { Content = "Zrušit", MinWidth = 92 };
        var continueButton = new Button
        {
            Content = "Pokračovat",
            MinWidth = 110,
            Background = Brushes.Black,
            Foreground = Brushes.White
        };
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Children = { cancelButton, continueButton }
        };
        Grid.SetRow(buttons, 2);

        var content = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            Margin = new Thickness(18),
            RowSpacing = 12,
            Children =
            {
                new TextBlock
                {
                    Text = message,
                    TextWrapping = TextWrapping.Wrap
                },
                textBox,
                buttons
            }
        };
        Grid.SetRow(textBox, 1);

        var dialog = new Window
        {
            Title = title,
            Width = 520,
            Height = 280,
            MinWidth = 500,
            MinHeight = 260,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Content = content
        };

        cancelButton.Click += (_, _) => dialog.Close(null);
        continueButton.Click += (_, _) => dialog.Close(textBox.Text);

        return await dialog.ShowDialog<string?>(this);
    }

    private void OnTaxOfficeDropDownOpened(object? sender, EventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            _ = viewModel.EnsureLiveTaxOfficeCatalogAsync();
        }
    }

    private async Task<ReexportChoice> ConfirmReexportAsync(string title, string message, string correctiveLabel)
    {
        var cancelButton = new Button { Content = "Zrušit", MinWidth = 92 };
        var regularButton = new Button { Content = "Řádné přiznání", MinWidth = 130 };
        var correctiveButton = new Button
        {
            Content = correctiveLabel,
            MinWidth = 140,
            Background = Brushes.Black,
            Foreground = Brushes.White
        };
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Children = { cancelButton, regularButton, correctiveButton }
        };
        Grid.SetRow(buttons, 1);

        var content = new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto"),
            Margin = new Thickness(18),
            Children =
            {
                new TextBlock
                {
                    Text = message,
                    TextWrapping = TextWrapping.Wrap,
                    VerticalAlignment = VerticalAlignment.Center
                },
                buttons
            }
        };

        var dialog = new Window
        {
            Title = title,
            Width = 520,
            Height = 230,
            MinWidth = 500,
            MinHeight = 200,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Content = content
        };

        cancelButton.Click += (_, _) => dialog.Close(ReexportChoice.Cancel);
        regularButton.Click += (_, _) => dialog.Close(ReexportChoice.Regular);
        correctiveButton.Click += (_, _) => dialog.Close(ReexportChoice.Corrective);

        return await dialog.ShowDialog<ReexportChoice>(this);
    }

    private async void OnCounterpartySelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isSyncingCounterpartySelection
            || sender is not ListBox listBox
            || DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        await viewModel.SelectCounterpartyAsync(listBox.SelectedItem as CounterpartyViewModel);

        _isSyncingCounterpartySelection = true;
        try
        {
            listBox.SelectedItem = viewModel.SelectedCounterparty;
        }
        finally
        {
            _isSyncingCounterpartySelection = false;
        }
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
        if (_closeSavesCompleted || DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        // Zavření se nejdřív zruší a okno se zavře až po dokončení ukládání – async handler sám
        // o sobě zavření nepozdrží a rozběhnuté zápisy by ukončení aplikace uřízlo.
        e.Cancel = true;
        try
        {
            await viewModel.SaveTaxSubjectAsync();
            await viewModel.SaveSelectedCounterpartyAsync();
            await viewModel.Issuing.SaveSelectedInvoiceAsync();
            await viewModel.SaveWindowSizeAsync(Bounds.Width, Bounds.Height);
        }
        finally
        {
            _closeSavesCompleted = true;
            Close();
        }
    }
}
